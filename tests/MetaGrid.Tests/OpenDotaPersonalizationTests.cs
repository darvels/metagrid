using System.Text;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;
using MetaGrid.Tests.TestSupport;

namespace MetaGrid.Tests;

public sealed class OpenDotaPersonalizationTests
{
    [Theory]
    [InlineData("70388657", "70388657")]
    [InlineData("https://www.opendota.com/players/70388657", "70388657")]
    [InlineData("https://opendota.com/players/70388657", "70388657")]
    [InlineData("https://www.dotabuff.com/players/70388657", "70388657")]
    [InlineData("https://dotabuff.com/players/70388657", "70388657")]
    public void ProfileInputParser_Accepts_Supported_Formats(string input, string expectedAccountId)
    {
        var parser = new PlayerProfileInputParser();

        var result = parser.Parse(input);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedAccountId, result.AccountId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-4")]
    [InlineData("https://example.com/players/70388657")]
    [InlineData("https://www.opendota.com/players/not-a-number")]
    [InlineData("https://www.dotabuff.com/players/")]
    public void ProfileInputParser_Rejects_Invalid_Formats(string input)
    {
        var parser = new PlayerProfileInputParser();

        var result = parser.Parse(input);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task PersonalHeroSelector_Uses_Inclusive_Thresholds_And_Deterministic_Order()
    {
        var selector = new PersonalHeroSelector(new FakeHeroCatalogService(300));
        var stats = new[]
        {
            new OpenDotaPlayerHeroStats { HeroId = 10, Games = 10, Wins = 6, LastPlayedUnixSeconds = 1 },
            new OpenDotaPlayerHeroStats { HeroId = 11, Games = 9, Wins = 9, LastPlayedUnixSeconds = 1 },
            new OpenDotaPlayerHeroStats { HeroId = 12, Games = 100, Wins = 53, LastPlayedUnixSeconds = 1 },
            new OpenDotaPlayerHeroStats { HeroId = 13, Games = 100, Wins = 52, LastPlayedUnixSeconds = 1 },
            new OpenDotaPlayerHeroStats { HeroId = 14, Games = 22, Wins = 15, LastPlayedUnixSeconds = 1 },
            new OpenDotaPlayerHeroStats { HeroId = 15, Games = 22, Wins = 15, LastPlayedUnixSeconds = 1 },
            new OpenDotaPlayerHeroStats { HeroId = 16, Games = 20, Wins = 14, LastPlayedUnixSeconds = 1 },
            new OpenDotaPlayerHeroStats { HeroId = 17, Games = 18, Wins = 12, LastPlayedUnixSeconds = 1 },
            new OpenDotaPlayerHeroStats { HeroId = 18, Games = 17, Wins = 11, LastPlayedUnixSeconds = 1 },
            new OpenDotaPlayerHeroStats { HeroId = 19, Games = 16, Wins = 10, LastPlayedUnixSeconds = 1 },
            new OpenDotaPlayerHeroStats { HeroId = 20, Games = 15, Wins = 8, LastPlayedUnixSeconds = 1 }
        };

        var selection = await selector.SelectAsync("70388657", "Tester", stats, DateTimeOffset.Parse("2026-08-29T12:00:00Z"), CancellationToken.None);

        Assert.Equal(PersonalHeroSelection.CurrentRuleVersion, selection.RuleVersion);
        Assert.Equal(PersonalHeroSelection.CurrentMinGamesInclusive, selection.MinGamesInclusive);
        Assert.Equal(PersonalHeroSelection.CurrentMinWinRateInclusive, selection.MinWinRateInclusive);
        Assert.Equal(new[] { 16, 14, 15, 17, 18, 19, 10 }, selection.SelectedHeroes.Select(hero => hero.HeroId).ToArray());
        Assert.DoesNotContain(selection.SelectedHeroes, hero => hero.HeroId is 11 or 13 or 20);
    }

    [Fact]
    public async Task PersonalizationService_Uses_SelectedSteamAccount_Id_Directly_As_OpenDota_Account()
    {
        using var temp = new TemporaryDirectory();
        var client = new RecordingOpenDotaClient(
            new OpenDotaPlayerProfileResponse { AccountId = "123456789", PersonaName = "Fixture player", IsProfileUnavailable = false },
            [new OpenDotaPlayerHeroStats { HeroId = 8, Games = 10, Wins = 6, LastPlayedUnixSeconds = 1 }]);
        var service = CreateService(temp.Path, client);
        var settings = new AppSettings
        {
            PersonalizationEnabled = true,
            PersonalizationAccountSourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount
        };

        var result = await service.ResolveAsync(settings, new PersonalizationAccountContext
        {
            SourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount,
            AccountId = "123456789",
            DisplayName = "Fixture player"
        }, forceRefresh: true, CancellationToken.None);

        Assert.Equal(2, client.RequestedAccountIds.Count);
        Assert.All(client.RequestedAccountIds, accountId => Assert.Equal("123456789", accountId));
        Assert.Equal(PersonalizationStatus.Ready, result.Status);
        Assert.Equal("123456789", result.AccountId);
        Assert.Equal(PersonalizationAccountSourceMode.SelectedSteamAccount, result.SourceMode);
    }

    [Fact]
    public async Task PersonalizationService_No_Selected_Steam_Account_Skips_OpenDota_Fetch()
    {
        using var temp = new TemporaryDirectory();
        var client = new RecordingOpenDotaClient(
            new OpenDotaPlayerProfileResponse { AccountId = "70388657", PersonaName = "Tester", IsProfileUnavailable = false },
            []);
        var service = CreateService(temp.Path, client);
        var settings = new AppSettings
        {
            PersonalizationEnabled = true,
            PersonalizationAccountSourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount
        };

        var result = await service.ResolveAsync(settings, new PersonalizationAccountContext
        {
            SourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount
        }, forceRefresh: false, CancellationToken.None);

        Assert.Equal(PersonalizationStatus.ProfileUnavailable, result.Status);
        Assert.Equal("Select a Steam account to enable personal heroes.", result.Message);
        Assert.Empty(client.RequestedAccountIds);
    }

    [Fact]
    public async Task PersonalizationService_Uses_Fresh_CurrentRule_Cache_Without_Network()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var cacheService = new PersonalHeroCacheService(paths);
        await cacheService.SaveAsync(new PersonalizationCacheEntry
        {
            AccountId = "70388657",
            DisplayName = "Tester",
            FetchedAt = DateTimeOffset.Parse("2026-08-29T10:00:00Z"),
            RuleVersion = PersonalHeroSelection.CurrentRuleVersion,
            WindowDays = PersonalHeroSelection.CurrentWindowDays,
            MinGamesInclusive = PersonalHeroSelection.CurrentMinGamesInclusive,
            MinWinRateInclusive = PersonalHeroSelection.CurrentMinWinRateInclusive,
            MaxHeroes = PersonalHeroSelection.CurrentMaxHeroes,
            Status = PersonalizationStatus.Ready,
            SelectedHeroes =
            [
                new PersonalHeroRecord { HeroId = 8, HeroName = "Hero 8", Games = 40, Wins = 30, WinRate = 0.75d }
            ]
        }, CancellationToken.None);

        var client = new ThrowingOpenDotaClient();
        var service = new PersonalizationService(
            new PlayerProfileInputParser(),
            client,
            new PersonalHeroSelector(new FakeHeroCatalogService(300)),
            cacheService,
            new FakeAppClock(DateTimeOffset.Parse("2026-08-29T12:00:00Z")),
            new FileLoggingService(paths));

        var result = await service.ResolveAsync(new AppSettings
        {
            PersonalizationEnabled = true,
            PersonalizationAccountSourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount
        }, new PersonalizationAccountContext
        {
            SourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount,
            AccountId = "70388657",
            DisplayName = "Tester"
        }, forceRefresh: false, CancellationToken.None);

        Assert.Equal(PersonalizationStatus.Ready, result.Status);
        Assert.True(result.UsedCache);
        Assert.Equal(0, client.ProfileCalls);
        Assert.Equal(0, client.HeroCalls);
    }

    [Fact]
    public async Task PersonalizationService_Rejects_Old_Rule_Cache_And_Refreshes_Live_Data()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var cacheService = new PersonalHeroCacheService(paths);
        await cacheService.SaveAsync(new PersonalizationCacheEntry
        {
            AccountId = "70388657",
            DisplayName = "Tester",
            FetchedAt = DateTimeOffset.Parse("2026-08-29T11:00:00Z"),
            RuleVersion = "opendota-90d-v1",
            WindowDays = 90,
            MinGamesInclusive = 15,
            MinWinRateInclusive = 0.55d,
            MaxHeroes = 7,
            Status = PersonalizationStatus.Ready,
            SelectedHeroes =
            [
                new PersonalHeroRecord { HeroId = 1, HeroName = "Old Hero", Games = 20, Wins = 12, WinRate = 0.60d }
            ]
        }, CancellationToken.None);

        var client = new RecordingOpenDotaClient(
            new OpenDotaPlayerProfileResponse { AccountId = "70388657", PersonaName = "Tester", IsProfileUnavailable = false },
            [new OpenDotaPlayerHeroStats { HeroId = 8, Games = 10, Wins = 6, LastPlayedUnixSeconds = 1 }]);
        var service = CreateService(temp.Path, client);

        var result = await service.ResolveAsync(new AppSettings
        {
            PersonalizationEnabled = true,
            PersonalizationAccountSourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount
        }, new PersonalizationAccountContext
        {
            SourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount,
            AccountId = "70388657",
            DisplayName = "Tester"
        }, forceRefresh: false, CancellationToken.None);

        Assert.Equal(PersonalizationStatus.Ready, result.Status);
        Assert.False(result.UsedCache);
        Assert.Equal(2, client.RequestedAccountIds.Count);
        Assert.Contains(result.Selection!.SelectedHeroes, hero => hero.HeroId == 8);
    }

    [Fact]
    public async Task PersonalizationService_Changing_Selected_Account_Does_Not_Reuse_Previous_Account_Cache()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var cacheService = new PersonalHeroCacheService(paths);
        await cacheService.SaveAsync(new PersonalizationCacheEntry
        {
            AccountId = "123456789",
            DisplayName = "Account A",
            FetchedAt = DateTimeOffset.Parse("2026-08-29T10:00:00Z"),
            RuleVersion = PersonalHeroSelection.CurrentRuleVersion,
            WindowDays = PersonalHeroSelection.CurrentWindowDays,
            MinGamesInclusive = PersonalHeroSelection.CurrentMinGamesInclusive,
            MinWinRateInclusive = PersonalHeroSelection.CurrentMinWinRateInclusive,
            MaxHeroes = PersonalHeroSelection.CurrentMaxHeroes,
            Status = PersonalizationStatus.Ready,
            SelectedHeroes =
            [
                new PersonalHeroRecord { HeroId = 8, HeroName = "Hero 8", Games = 40, Wins = 30, WinRate = 0.75d }
            ]
        }, CancellationToken.None);

        var client = new RecordingOpenDotaClient(
            new OpenDotaPlayerProfileResponse { AccountId = "70388657", PersonaName = "Account B", IsProfileUnavailable = false },
            [new OpenDotaPlayerHeroStats { HeroId = 10, Games = 10, Wins = 6, LastPlayedUnixSeconds = 1 }]);
        var service = new PersonalizationService(
            new PlayerProfileInputParser(),
            client,
            new PersonalHeroSelector(new FakeHeroCatalogService(300)),
            cacheService,
            new FakeAppClock(DateTimeOffset.Parse("2026-08-29T12:00:00Z")),
            new FileLoggingService(paths));

        var result = await service.ResolveAsync(new AppSettings
        {
            PersonalizationEnabled = true,
            PersonalizationAccountSourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount
        }, new PersonalizationAccountContext
        {
            SourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount,
            AccountId = "70388657",
            DisplayName = "Account B"
        }, forceRefresh: false, CancellationToken.None);

        Assert.False(result.UsedCache);
        Assert.All(result.Selection!.SelectedHeroes, hero => Assert.NotEqual(8, hero.HeroId));
        Assert.All(client.RequestedAccountIds, accountId => Assert.Equal("70388657", accountId));
    }

    [Fact]
    public async Task PersonalizationService_Uses_Cached_Row_When_Live_Refresh_Fails()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var cacheService = new PersonalHeroCacheService(paths);
        await cacheService.SaveAsync(new PersonalizationCacheEntry
        {
            AccountId = "70388657",
            DisplayName = "Tester",
            FetchedAt = DateTimeOffset.Parse("2026-08-29T01:00:00Z"),
            RuleVersion = PersonalHeroSelection.CurrentRuleVersion,
            WindowDays = PersonalHeroSelection.CurrentWindowDays,
            MinGamesInclusive = PersonalHeroSelection.CurrentMinGamesInclusive,
            MinWinRateInclusive = PersonalHeroSelection.CurrentMinWinRateInclusive,
            MaxHeroes = PersonalHeroSelection.CurrentMaxHeroes,
            Status = PersonalizationStatus.Ready,
            SelectedHeroes =
            [
                new PersonalHeroRecord { HeroId = 8, HeroName = "Hero 8", Games = 40, Wins = 30, WinRate = 0.75d }
            ]
        }, CancellationToken.None);

        var service = new PersonalizationService(
            new PlayerProfileInputParser(),
            new ThrowingOpenDotaClient(),
            new PersonalHeroSelector(new FakeHeroCatalogService(300)),
            cacheService,
            new FakeAppClock(DateTimeOffset.Parse("2026-08-29T12:00:00Z")),
            new FileLoggingService(paths));

        var result = await service.ResolveAsync(new AppSettings
        {
            PersonalizationEnabled = true,
            PersonalizationAccountSourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount
        }, new PersonalizationAccountContext
        {
            SourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount,
            AccountId = "70388657",
            DisplayName = "Tester"
        }, forceRefresh: false, CancellationToken.None);

        Assert.Equal(PersonalizationStatus.Cached, result.Status);
        Assert.True(result.UsedCache);
        Assert.Single(result.Selection!.SelectedHeroes);
    }

    [Fact]
    public async Task UpdateService_Uses_EffectiveHash_For_Personalized_Checks_And_BaseHash_Stays_Stable()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var backupService = new BackupService(paths);
        var historyService = new HistoryService(paths);
        var logging = new FileLoggingService(paths);
        var dotaGridService = new DotaGridService();
        var baseSnapshot = LoadOfficialSnapshot();
        var provider = new StubProvider(baseSnapshot);
        var account = CreateMissingGridAccountFixture(paths, "123456789");
        var accountAResolution = new PersonalizationResolution
        {
            Status = PersonalizationStatus.Ready,
            SourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount,
            Message = "Personal heroes ready.",
            AccountId = "123456789",
            Selection = new PersonalHeroSelection
            {
                AccountId = "123456789",
                FetchedAt = DateTimeOffset.Parse("2026-08-29T12:00:00Z"),
                SelectedHeroes =
                [
                    new PersonalHeroRecord { HeroId = 8, HeroName = "Hero 8", Games = 40, Wins = 30, WinRate = 0.75d }
                ]
            }
        };
        var accountBResolution = new PersonalizationResolution
        {
            Status = PersonalizationStatus.Ready,
            SourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount,
            Message = "Personal heroes ready.",
            AccountId = "70388657",
            Selection = new PersonalHeroSelection
            {
                AccountId = "70388657",
                FetchedAt = DateTimeOffset.Parse("2026-08-29T12:05:00Z"),
                SelectedHeroes =
                [
                    new PersonalHeroRecord { HeroId = 10, HeroName = "Hero 10", Games = 30, Wins = 18, WinRate = 0.60d }
                ]
            }
        };

        var personalization = new RoutedPersonalizationService(accountAResolution, accountBResolution);
        var updateService = new UpdateService(provider, dotaGridService, backupService, historyService, logging, new FakeHeroCatalogService(500), personalization, new PersonalizedGridComposer(dotaGridService));
        var settings = new AppSettings
        {
            PersonalizationEnabled = true,
            PersonalizationAccountSourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount
        };

        var accountAResult = await updateService.CheckForUpdatesAsync([account], settings, forceWrite: false, CancellationToken.None);
        account.IsSelected = false;
        var accountB = CreateMissingGridAccountFixture(paths, "70388657");
        var accountBResult = await updateService.CheckForUpdatesAsync([accountB], settings, forceWrite: false, CancellationToken.None);

        Assert.Equal(baseSnapshot.Hash, accountAResult.BaseSourceHash);
        Assert.Equal(baseSnapshot.Hash, accountBResult.BaseSourceHash);
        Assert.NotEqual(accountAResult.EffectiveGridHash, accountBResult.EffectiveGridHash);
    }

    [Fact]
    public async Task UpdateService_Personalization_Disabled_Keeps_Effective_Hash_Equal_To_Base()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var backupService = new BackupService(paths);
        var historyService = new HistoryService(paths);
        var logging = new FileLoggingService(paths);
        var dotaGridService = new DotaGridService();
        var baseSnapshot = LoadOfficialSnapshot();
        var provider = new StubProvider(baseSnapshot);
        var account = CreateMissingGridAccountFixture(paths, "123456789");
        var updateService = new UpdateService(provider, dotaGridService, backupService, historyService, logging, new FakeHeroCatalogService(500), new RoutedPersonalizationService(), new PersonalizedGridComposer(dotaGridService));

        var result = await updateService.CheckForUpdatesAsync([account], new AppSettings
        {
            PersonalizationEnabled = false,
            PersonalizationAccountSourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount
        }, forceWrite: false, CancellationToken.None);

        Assert.Equal(result.BaseSourceHash, result.EffectiveGridHash);
    }

    [Fact]
    public async Task Manual_Mode_Remains_Isolated_From_Selected_Account_Changes()
    {
        using var temp = new TemporaryDirectory();
        var personalization = new RoutedPersonalizationService(
            new PersonalizationResolution
            {
                Status = PersonalizationStatus.Ready,
                SourceMode = PersonalizationAccountSourceMode.ManualAccount,
                Message = "Personal heroes ready.",
                AccountId = "70388657",
                Selection = new PersonalHeroSelection
                {
                    AccountId = "70388657",
                    FetchedAt = DateTimeOffset.Parse("2026-08-29T12:00:00Z"),
                    SelectedHeroes = []
                }
            });

        var result = await personalization.ResolveAsync(new AppSettings
        {
            PersonalizationEnabled = true,
            PersonalizationAccountSourceMode = PersonalizationAccountSourceMode.ManualAccount,
            PersonalizationManualAccountId = "70388657"
        }, new PersonalizationAccountContext
        {
            SourceMode = PersonalizationAccountSourceMode.ManualAccount,
            AccountId = "70388657"
        }, forceRefresh: false, CancellationToken.None);

        Assert.Equal(PersonalizationAccountSourceMode.ManualAccount, result.SourceMode);
        Assert.Equal("70388657", result.AccountId);
    }

    [Fact]
    public void PersonalizedGridComposer_Aligns_AllHeroes_With_Personal_Row_And_Preserves_Content()
    {
        var service = new DotaGridService();
        var baseSnapshot = LoadOfficialSnapshot();
        var composer = new PersonalizedGridComposer(service);

        var result = composer.Compose(baseSnapshot, new PersonalizationResolution
        {
            Status = PersonalizationStatus.Ready,
            SourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount,
            Message = "Personal heroes ready.",
            AccountId = "70388657",
            Selection = new PersonalHeroSelection
            {
                AccountId = "70388657",
                FetchedAt = DateTimeOffset.Parse("2026-08-29T12:00:00Z"),
                SelectedHeroes =
                [
                    new PersonalHeroRecord { HeroId = 8, HeroName = "Hero 8", Games = 40, Wins = 30, WinRate = 0.75d },
                    new PersonalHeroRecord { HeroId = 6, HeroName = "Hero 6", Games = 21, Wins = 13, WinRate = 0.619d }
                ]
            }
        });

        var allRoles = result.Layouts.First(layout => layout.Name.Contains("All Roles", StringComparison.OrdinalIgnoreCase));
        var baseAllRoles = baseSnapshot.Layouts.First(layout => layout.Name.Contains("All Roles", StringComparison.OrdinalIgnoreCase));
        var personalCategory = allRoles.Categories[0];
        var carryCategory = allRoles.Categories.First(category => category.Name == "Carry");
        var midCategory = allRoles.Categories.First(category => category.Name == "Mid");
        var offlaneCategory = allRoles.Categories.First(category => category.Name == "Offlane");
        var supportCategory = allRoles.Categories.First(category => category.Name == "Support");
        var hardSupportCategory = allRoles.Categories.First(category => category.Name == "Hard Support");
        var allHeroesCategory = allRoles.Categories.First(category => category.Name == "All Heroes");
        var baseAllHeroesCategory = baseAllRoles.Categories.First(category => category.Name == "All Heroes");

        Assert.Equal("MY BEST HEROES", personalCategory.Name);
        Assert.Equal([8, 6], personalCategory.HeroIds);
        Assert.Equal(personalCategory.YPosition, allHeroesCategory.YPosition);
        Assert.Equal(95, carryCategory.YPosition);
        Assert.Equal(190, midCategory.YPosition);
        Assert.Equal(285, offlaneCategory.YPosition);
        Assert.Equal(380, supportCategory.YPosition);
        Assert.Equal(475, hardSupportCategory.YPosition);
        Assert.True(carryCategory.YPosition >= personalCategory.YPosition + personalCategory.Height);

        Assert.Equal(baseAllHeroesCategory.Name, allHeroesCategory.Name);
        Assert.Equal(baseAllHeroesCategory.HeroIds, allHeroesCategory.HeroIds);
        Assert.Equal(baseAllHeroesCategory.XPosition, allHeroesCategory.XPosition);
        Assert.Equal(0, allHeroesCategory.YPosition);
        Assert.Equal(baseAllHeroesCategory.Width, allHeroesCategory.Width);
        Assert.Equal(baseAllHeroesCategory.Height, allHeroesCategory.Height);
        Assert.Equal(600, allHeroesCategory.Height);
        Assert.Equal(600, allHeroesCategory.YPosition + allHeroesCategory.Height);
    }

    [Fact]
    public void PersonalizedGridComposer_Without_Personal_Heroes_Preserves_Original_AllHeroes_Geometry()
    {
        var baseSnapshot = LoadOfficialSnapshot();
        var composer = new PersonalizedGridComposer(new DotaGridService());

        var result = composer.Compose(baseSnapshot, new PersonalizationResolution
        {
            Status = PersonalizationStatus.NoQualifyingHeroes,
            SourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount,
            Message = "No qualifying heroes in last 90 days.",
            AccountId = "70388657",
            Selection = new PersonalHeroSelection
            {
                AccountId = "70388657",
                FetchedAt = DateTimeOffset.Parse("2026-08-29T12:00:00Z"),
                SelectedHeroes = []
            }
        });

        var baseAllRoles = baseSnapshot.Layouts.First(layout => layout.Name.Contains("All Roles", StringComparison.OrdinalIgnoreCase));
        var resultAllRoles = result.Layouts.First(layout => layout.Name.Contains("All Roles", StringComparison.OrdinalIgnoreCase));
        var baseAllHeroes = baseAllRoles.Categories.First(category => category.Name == "All Heroes");
        var resultAllHeroes = resultAllRoles.Categories.First(category => category.Name == "All Heroes");

        Assert.Equal(baseSnapshot.Hash, result.Hash);
        Assert.Equal(baseAllHeroes.YPosition, resultAllHeroes.YPosition);
        Assert.Equal(baseAllRoles.Categories.Count, resultAllRoles.Categories.Count);
        Assert.DoesNotContain(resultAllRoles.Categories, category => category.Name == "MY BEST HEROES");
    }

    [Fact]
    public void PersonalizedGridComposer_Is_Deterministic_For_Identical_Input()
    {
        var service = new DotaGridService();
        var baseSnapshot = LoadOfficialSnapshot();
        var composer = new PersonalizedGridComposer(service);
        var personalization = new PersonalizationResolution
        {
            Status = PersonalizationStatus.Ready,
            SourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount,
            Message = "Personal heroes ready.",
            AccountId = "70388657",
            Selection = new PersonalHeroSelection
            {
                AccountId = "70388657",
                FetchedAt = DateTimeOffset.Parse("2026-08-29T12:00:00Z"),
                SelectedHeroes =
                [
                    new PersonalHeroRecord { HeroId = 8, HeroName = "Hero 8", Games = 40, Wins = 30, WinRate = 0.75d },
                    new PersonalHeroRecord { HeroId = 6, HeroName = "Hero 6", Games = 21, Wins = 13, WinRate = 0.619d }
                ]
            }
        };

        var first = composer.Compose(baseSnapshot, personalization);
        var second = composer.Compose(baseSnapshot, personalization);

        Assert.Equal(first.Hash, second.Hash);
        var firstAllRoles = first.Layouts.First(layout => layout.Name.Contains("All Roles", StringComparison.OrdinalIgnoreCase));
        var secondAllRoles = second.Layouts.First(layout => layout.Name.Contains("All Roles", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            firstAllRoles.Categories.Select(category => $"{category.Name}:{category.XPosition}:{category.YPosition}:{category.Width}:{category.Height}"),
            secondAllRoles.Categories.Select(category => $"{category.Name}:{category.XPosition}:{category.YPosition}:{category.Width}:{category.Height}"));
    }

    [Fact]
    public void PersonalizedGridComposer_Preserves_RoleSpecific_Layouts_Unchanged()
    {
        var service = new DotaGridService();
        var baseSnapshot = LoadOfficialSnapshot();
        var composer = new PersonalizedGridComposer(service);

        var result = composer.Compose(baseSnapshot, new PersonalizationResolution
        {
            Status = PersonalizationStatus.Ready,
            SourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount,
            Message = "Personal heroes ready.",
            AccountId = "70388657",
            Selection = new PersonalHeroSelection
            {
                AccountId = "70388657",
                FetchedAt = DateTimeOffset.Parse("2026-08-29T12:00:00Z"),
                SelectedHeroes =
                [
                    new PersonalHeroRecord { HeroId = 8, HeroName = "Hero 8", Games = 40, Wins = 30, WinRate = 0.75d }
                ]
            }
        });

        foreach (var baseLayout in baseSnapshot.Layouts.Where(layout => !layout.Name.Contains("All Roles", StringComparison.OrdinalIgnoreCase)))
        {
            var resultLayout = result.Layouts.First(layout => string.Equals(layout.Name, baseLayout.Name, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(
                baseLayout.Categories.Select(category => $"{category.Name}:{category.XPosition}:{category.YPosition}:{category.Width}:{category.Height}:{string.Join(",", category.HeroIds)}"),
                resultLayout.Categories.Select(category => $"{category.Name}:{category.XPosition}:{category.YPosition}:{category.Width}:{category.Height}:{string.Join(",", category.HeroIds)}"));
        }
    }

    [Fact]
    public async Task PersonalizedGridComposer_RoundTrips_Corrected_Effective_Grid_Through_Temp_Serialization()
    {
        using var temp = new TemporaryDirectory();
        var dotaGridService = new DotaGridService();
        var baseSnapshot = LoadOfficialSnapshot();
        var composer = new PersonalizedGridComposer(dotaGridService);
        var effectiveSnapshot = composer.Compose(baseSnapshot, new PersonalizationResolution
        {
            Status = PersonalizationStatus.Ready,
            SourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount,
            Message = "Personal heroes ready.",
            AccountId = "70388657",
            Selection = new PersonalHeroSelection
            {
                AccountId = "70388657",
                FetchedAt = DateTimeOffset.Parse("2026-08-29T12:00:00Z"),
                SelectedHeroes =
                [
                    new PersonalHeroRecord { HeroId = 8, HeroName = "Hero 8", Games = 40, Wins = 30, WinRate = 0.75d },
                    new PersonalHeroRecord { HeroId = 6, HeroName = "Hero 6", Games = 21, Wins = 13, WinRate = 0.619d }
                ]
            }
        });

        var path = Path.Combine(temp.Path, "hero_grid_config.json");
        await File.WriteAllTextAsync(path, dotaGridService.Serialize(dotaGridService.ApplySnapshot(new DotaHeroGridFile(), effectiveSnapshot)));
        var json = await File.ReadAllTextAsync(path);
        Assert.True(dotaGridService.TryValidate(json, out _));

        var reread = await dotaGridService.ReadAsync(path, CancellationToken.None);
        var extracted = dotaGridService.ExtractManagedGrid(reread);
        Assert.NotNull(extracted);
        Assert.Equal(6, extracted!.Layouts.Count);

        var allRoles = reread.Configs.First(layout => layout.ConfigName.Contains("All Roles", StringComparison.OrdinalIgnoreCase));
        var personal = allRoles.Categories.First(category => category.CategoryName == "MY BEST HEROES");
        var allHeroes = allRoles.Categories.First(category => category.CategoryName == "All Heroes");
        var carry = allRoles.Categories.First(category => category.CategoryName == "Carry");
        var hardSupport = allRoles.Categories.First(category => category.CategoryName == "Hard Support");

        Assert.Equal(0, personal.YPosition);
        Assert.Equal(0, allHeroes.YPosition);
        Assert.Equal(600, allHeroes.Height);
        Assert.Equal(600, allHeroes.YPosition + allHeroes.Height);
        Assert.Equal(95, carry.YPosition);
        Assert.Equal(475, hardSupport.YPosition);
        Assert.Equal(effectiveSnapshot.Hash, dotaGridService.ComputeCanonicalHash(extracted));
    }

    private static PersonalizationService CreateService(string tempRoot, IOpenDotaClient client)
    {
        var paths = new TestAppPaths(tempRoot);
        return new PersonalizationService(
            new PlayerProfileInputParser(),
            client,
            new PersonalHeroSelector(new FakeHeroCatalogService(300)),
            new PersonalHeroCacheService(paths),
            new FakeAppClock(DateTimeOffset.Parse("2026-08-29T12:00:00Z")),
            new FileLoggingService(paths));
    }

    private static HeroGridSnapshot LoadOfficialSnapshot()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "dota2protracker_hero_grid_high_winrate_config.json");
        var payload = File.ReadAllText(path);
        var validation = D2ptOfficialGridPayloadValidator.Validate(payload, Path.GetFileName(path));
        Assert.True(validation.IsValid, validation.Message);

        return new HeroGridSnapshot
        {
            SourceName = validation.Snapshot!.SourceName,
            ProviderName = validation.Snapshot.ProviderName,
            SourceStrategy = "OfficialDownloadPayload",
            Preset = validation.Snapshot.Preset,
            PatchLabel = validation.Snapshot.PatchLabel,
            CapturedAt = validation.Snapshot.CapturedAt,
            Hash = validation.SemanticHash!,
            Layouts = validation.Snapshot.Layouts,
            RankBracket = validation.Snapshot.RankBracket,
            PeriodStart = validation.Snapshot.PeriodStart,
            PeriodEnd = validation.Snapshot.PeriodEnd,
            MinimumSampleMatches = validation.Snapshot.MinimumSampleMatches,
            UsesDerivedPositions = validation.Snapshot.UsesDerivedPositions,
            PositionDefinition = validation.Snapshot.PositionDefinition,
            RoleResults = validation.Snapshot.RoleResults,
            ProviderStatus = ProviderStatus.Online,
            OriginKind = GridOriginKind.NativeD2pt,
            SourceDetails = validation.Snapshot.SourceDetails,
            RawPayloadBytes = Encoding.UTF8.GetBytes(payload),
            ParsedGrid = validation.GridFile
        };
    }

    private static SteamAccount CreateMissingGridAccountFixture(TestAppPaths paths, string accountId)
    {
        var accountDir = Path.Combine(paths.RootDirectory, "steam", accountId, "570", "remote", "cfg");
        Directory.CreateDirectory(accountDir);
        return new SteamAccount
        {
            AccountId = accountId,
            SteamRootPath = Path.Combine(paths.RootDirectory, "steam"),
            UserDataPath = Path.Combine(paths.RootDirectory, "steam", accountId),
            DotaConfigDirectory = accountDir,
            DisplayName = $"Steam {accountId}",
            HasDotaUserData = true,
            HasHeroGridConfig = false,
            IsSelected = true
        };
    }

    private sealed class FakeAppClock(DateTimeOffset now) : IAppClock
    {
        public DateTimeOffset Now { get; } = now;
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingOpenDotaClient(OpenDotaPlayerProfileResponse profile, IReadOnlyList<OpenDotaPlayerHeroStats> heroes) : IOpenDotaClient
    {
        public List<string> RequestedAccountIds { get; } = [];

        public Task<OpenDotaPlayerProfileResponse> GetPlayerProfileAsync(string accountId, CancellationToken cancellationToken)
        {
            RequestedAccountIds.Add(accountId);
            return Task.FromResult(profile);
        }

        public Task<IReadOnlyList<OpenDotaPlayerHeroStats>> GetPlayerHeroesAsync(string accountId, CancellationToken cancellationToken)
        {
            RequestedAccountIds.Add(accountId);
            return Task.FromResult(heroes);
        }
    }

    private sealed class ThrowingOpenDotaClient : IOpenDotaClient
    {
        public int ProfileCalls { get; private set; }
        public int HeroCalls { get; private set; }

        public Task<OpenDotaPlayerProfileResponse> GetPlayerProfileAsync(string accountId, CancellationToken cancellationToken)
        {
            ProfileCalls++;
            throw new HttpRequestException("offline");
        }

        public Task<IReadOnlyList<OpenDotaPlayerHeroStats>> GetPlayerHeroesAsync(string accountId, CancellationToken cancellationToken)
        {
            HeroCalls++;
            throw new HttpRequestException("offline");
        }
    }

    private sealed class RoutedPersonalizationService(params PersonalizationResolution[] resolutions) : IPersonalizationService
    {
        private readonly Queue<PersonalizationResolution> _resolutions = new(resolutions);

        public ProfileInputParseResult ParseProfileInput(string? input) => ProfileInputParseResult.Success(input ?? "70388657");
        public Task<PersonalizationResolution> ConnectAsync(string profileInput, CancellationToken cancellationToken) => Task.FromResult(Next());
        public Task<PersonalizationResolution> ResolveAsync(AppSettings settings, PersonalizationAccountContext accountContext, bool forceRefresh, CancellationToken cancellationToken) => Task.FromResult(Next());
        public Task<PersonalizationResolution> RefreshAsync(AppSettings settings, PersonalizationAccountContext accountContext, CancellationToken cancellationToken) => Task.FromResult(Next());
        public Task DisconnectAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;

        private PersonalizationResolution Next()
            => _resolutions.Count > 0
                ? _resolutions.Dequeue()
                : new PersonalizationResolution
                {
                    Status = PersonalizationStatus.Disabled,
                    SourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount,
                    Message = "Personal heroes are disabled."
                };
    }
}
