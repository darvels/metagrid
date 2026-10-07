using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;
using MetaGrid.Tests.TestSupport;

namespace MetaGrid.Tests;

public sealed class GuideSubscriptionTests
{
    [Fact]
    public void StableKey_Is_Hero_And_Role_Specific()
    {
        Assert.Equal("91:Support", GuideSubscriptionRecord.CreateStableKey(91, GuideRole.Support));
        Assert.NotEqual(
            GuideSubscriptionRecord.CreateStableKey(91, GuideRole.Support),
            GuideSubscriptionRecord.CreateStableKey(91, GuideRole.Mid));
    }

    [Fact]
    public void Normalize_Deduplicates_By_Hero_And_Role_And_Trims_Metadata()
    {
        var normalized = GuideSubscriptionCollection.Normalize(
        [
            new GuideSubscriptionRecord
            {
                HeroId = 91,
                HeroInternalName = " npc_dota_hero_wisp ",
                HeroDisplayName = " Io ",
                Role = GuideRole.Support,
                IsEnabled = false,
                ProviderName = " D2PT "
            },
            new GuideSubscriptionRecord
            {
                HeroId = 91,
                HeroInternalName = "npc_dota_hero_wisp",
                HeroDisplayName = "Io",
                Role = GuideRole.Support,
                IsEnabled = true,
                ProviderName = " OpenDota "
            },
            new GuideSubscriptionRecord
            {
                HeroId = 91,
                HeroInternalName = "npc_dota_hero_wisp",
                HeroDisplayName = "Io",
                Role = GuideRole.Mid,
                IsEnabled = true
            }
        ]);

        Assert.Equal(2, normalized.Count);
        Assert.Equal("OpenDota", normalized[1].ProviderName);
        Assert.True(normalized[1].IsEnabled);
        Assert.Equal("npc_dota_hero_wisp", normalized[1].HeroInternalName);
        Assert.Equal("Io", normalized[1].HeroDisplayName);
    }

    [Fact]
    public async Task SettingsService_Persists_Normalized_Guide_Subscriptions()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var service = new SettingsService(paths, new FileLoggingService(paths));

        await service.SaveAsync(new AppSettings
        {
            GuideSubscriptions =
            [
                new GuideSubscriptionRecord
                {
                    HeroId = 74,
                    HeroInternalName = " npc_dota_hero_invoker ",
                    HeroDisplayName = " Invoker ",
                    Role = GuideRole.Mid,
                    IsEnabled = true,
                    ProviderName = " D2PT "
                },
                new GuideSubscriptionRecord
                {
                    HeroId = 74,
                    HeroInternalName = "npc_dota_hero_invoker",
                    HeroDisplayName = "Invoker",
                    Role = GuideRole.Mid,
                    IsEnabled = false,
                    ProviderName = " OpenDota "
                }
            ]
        }, CancellationToken.None);

        var loaded = await service.LoadAsync(CancellationToken.None);

        Assert.Single(loaded.GuideSubscriptions);
        Assert.Equal("Invoker", loaded.GuideSubscriptions[0].HeroDisplayName);
        Assert.Equal("OpenDota", loaded.GuideSubscriptions[0].ProviderName);
        Assert.False(loaded.GuideSubscriptions[0].IsEnabled);
    }

    [Fact]
    public async Task FakeHeroCatalogService_Provides_Ordered_Hero_List_With_Icons()
    {
        var service = new FakeHeroCatalogService(3);

        var all = await service.LoadAllAsync(CancellationToken.None);

        Assert.Equal(3, all.Count);
        Assert.All(all, hero => Assert.False(string.IsNullOrWhiteSpace(hero.IconPath)));
        Assert.Equal(new[] { "Hero 1", "Hero 2", "Hero 3" }, all.Select(hero => hero.LocalizedName));
    }

    [Fact]
    public async Task RoleTogglePersistence_PreservesOtherRoles_AndInstalledGuideWhenDisabled()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var service = new SettingsService(paths, new FileLoggingService(paths));
        var settings = new AppSettings
        {
            GuideSubscriptions =
            [
                new GuideSubscriptionRecord { HeroId = 91, Role = GuideRole.Mid, IsEnabled = true, RemoteFile = "guides/wisp_metagrid_mid_.build", InstalledHash = "verified" },
                new GuideSubscriptionRecord { HeroId = 91, Role = GuideRole.Support, IsEnabled = true }
            ]
        };
        await service.SaveAsync(settings, CancellationToken.None);
        settings = await service.LoadAsync(CancellationToken.None);
        settings.GuideSubscriptions.Single(record => record.Role == GuideRole.Mid).IsEnabled = false;
        await service.SaveAsync(settings, CancellationToken.None);
        var loaded = await service.LoadAsync(CancellationToken.None);
        var mid = loaded.GuideSubscriptions.Single(record => record.Role == GuideRole.Mid);
        Assert.False(mid.IsEnabled);
        Assert.Equal("guides/wisp_metagrid_mid_.build", mid.RemoteFile);
        Assert.Equal("verified", mid.InstalledHash);
        Assert.True(loaded.GuideSubscriptions.Single(record => record.Role == GuideRole.Support).IsEnabled);
    }
}
