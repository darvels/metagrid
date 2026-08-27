using System.Text;
using System.Security.Cryptography;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;
using MetaGrid.Tests.TestSupport;

namespace MetaGrid.Tests;

public sealed class OfficialD2ptHeroGridProviderTests
{
    [Fact]
    public void OfficialFixture_PassesProviderValidation()
    {
        var payload = LoadOfficialFixture();

        var validation = D2ptOfficialGridPayloadValidator.Validate(payload, "dota2protracker_hero_grid_high_winrate_config.json");

        Assert.True(validation.IsValid, validation.Message);
        Assert.NotNull(validation.GridFile);
        Assert.NotNull(validation.Snapshot);
        Assert.Equal(3, validation.GridFile!.Version);
        Assert.Equal(6, validation.GridFile.Configs.Count);
        Assert.Equal("7F34FDF06EB4C3657D2641C30E014D993C044C7ECE1FF0E98EAB848182848384", validation.SemanticHash);
        Assert.Equal(ComputeSha256(payload), validation.RawSha256);
    }

    [Fact]
    public void OfficialFixture_PassesCanonicalValidation_AndRetainsFullStructure()
    {
        var payload = LoadOfficialFixture();
        var validation = D2ptOfficialGridPayloadValidator.Validate(payload, "dota2protracker_hero_grid_high_winrate_config.json");
        Assert.True(validation.IsValid, validation.Message);

        var snapshot = new HeroGridSnapshot
        {
            SourceName = validation.Snapshot!.SourceName,
            ProviderName = validation.Snapshot.ProviderName,
            SourceStrategy = validation.Snapshot.SourceStrategy,
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
            RoleResults = [],
            ProviderStatus = ProviderStatus.Online,
            OriginKind = GridOriginKind.NativeD2pt,
            SourceDetails = validation.Snapshot.SourceDetails,
            RawSha256 = validation.RawSha256,
            OriginalFilename = validation.Snapshot.OriginalFilename,
            ConfigCount = validation.Snapshot.ConfigCount,
            PayloadSizeBytes = validation.Snapshot.PayloadSizeBytes,
            ParsedGrid = validation.GridFile,
            RawPayloadBytes = Encoding.UTF8.GetBytes(payload)
        };

        var service = new DotaGridService();
        var canonical = service.CanonicalizeSnapshot(snapshot);
        var canonicalHash = service.ComputeCanonicalHash(canonical);

        Assert.NotEmpty(service.NormalizeCanonical(canonical));
        Assert.Equal(validation.SemanticHash, canonicalHash);
        Assert.Equal(6, canonical.Layouts.Count);
        Assert.Contains(canonical.Layouts, layout => layout.Name.Contains("All Roles", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(canonical.Layouts, layout => layout.Name.Contains("Carry", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(canonical.Layouts, layout => layout.Name.Contains("Mid", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(canonical.Layouts, layout => layout.Name.Contains("Offlane", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(canonical.Layouts, layout => layout.Name.Contains("Support", StringComparison.OrdinalIgnoreCase) && !layout.Name.Contains("Hard", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(canonical.Layouts, layout => layout.Name.Contains("Hard Support", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OfficialFixture_PreservesAllHeroes_AndMatchupCategories()
    {
        var validation = D2ptOfficialGridPayloadValidator.Validate(LoadOfficialFixture(), "dota2protracker_hero_grid_high_winrate_config.json");
        Assert.True(validation.IsValid, validation.Message);

        var snapshot = validation.Snapshot!;
        var allRoles = snapshot.Layouts.First(layout => layout.Name.Contains("All Roles", StringComparison.OrdinalIgnoreCase));
        var carry = snapshot.Layouts.First(layout => layout.Name.EndsWith(" - Carry", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(allRoles.Categories, category => category.Name == "All Heroes");
        Assert.Contains(carry.Categories, category => category.Name == "Top Heroes Pos 1");
        Assert.Contains(carry.Categories, category => category.Name == "Best with");
        Assert.Contains(carry.Categories, category => category.Name == "Worst with");
        Assert.Contains(carry.Categories, category => category.Name == "Best against");
        Assert.Contains(carry.Categories, category => category.Name == "Worst against");
    }

    [Fact]
    public void OfficialFixture_PreservesOrderedHeroIds_AndGeometry()
    {
        var validation = D2ptOfficialGridPayloadValidator.Validate(LoadOfficialFixture(), "dota2protracker_hero_grid_high_winrate_config.json");
        Assert.True(validation.IsValid, validation.Message);

        var carry = validation.Snapshot!.Layouts.First(layout => layout.Name.EndsWith(" - Carry", StringComparison.OrdinalIgnoreCase));
        var topHeroes = carry.Categories.First(category => category.Name == "Top Heroes Pos 1");

        Assert.Equal([11, 48, 12, 67, 54, 18, 109], topHeroes.HeroIds);
        Assert.Equal(0, topHeroes.XPosition);
        Assert.Equal(0, topHeroes.YPosition);
        Assert.Equal(65, topHeroes.Width);
        Assert.Equal(525, topHeroes.Height);
    }

    [Fact]
    public async Task FetchAsync_ReturnsValidatedOfficialSnapshot_AndPreservesRawPayload()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var cache = new GridSnapshotCacheService(paths);
        var logging = new FileLoggingService(paths);
        var payload = LoadOfficialFixture();
        var retrieval = new FakeOfficialRetrievalService(new D2ptOfficialGridRetrievalResult
        {
            Succeeded = true,
            ProviderStatus = ProviderStatus.Online,
            Classification = "OfficialDownloadPayload",
            Message = "Download succeeded.",
            RawJson = payload,
            SuggestedFilename = "dota2protracker_hero_grid_high_winrate_config.json",
            RuntimeVersion = "test-runtime",
            FinalUrl = "https://dota2protracker.com/meta-hero-grids",
            Title = "Meta Hero Grids",
            PageLoaded = true,
            HighWinrateButtonFound = true,
            OfficialDownloadTriggered = true,
            DownloadCompleted = true,
            ElapsedMs = 250
        });

        var provider = new OfficialD2ptHeroGridProvider(retrieval, cache, logging);

        var snapshot = await provider.FetchAsync(HeroGridPreset.HighWinrate, CancellationToken.None);
        var cachedPayload = await cache.LoadRawPayloadAsync(CancellationToken.None);

        Assert.Equal("OfficialDownloadPayload", snapshot.SourceStrategy);
        Assert.NotNull(snapshot.ParsedGrid);
        Assert.NotNull(snapshot.RawPayloadBytes);
        Assert.Equal(payload, cachedPayload);
        Assert.Equal(payload, Encoding.UTF8.GetString(snapshot.RawPayloadBytes!));
        Assert.Equal(6, snapshot.ParsedGrid!.Configs.Count);
    }

    [Fact]
    public async Task FetchAsync_UsesCachedSnapshot_WhenLiveRetrievalFails()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var cache = new GridSnapshotCacheService(paths);
        var logging = new FileLoggingService(paths);
        var seedPayload = LoadOfficialFixture();
        var seedValidation = D2ptOfficialGridPayloadValidator.Validate(seedPayload, "dota2protracker_hero_grid_high_winrate_config.json");
        Assert.True(seedValidation.IsValid);

        await cache.SaveAsync(new HeroGridSnapshot
        {
            SourceName = seedValidation.Snapshot!.SourceName,
            ProviderName = seedValidation.Snapshot.ProviderName,
            SourceStrategy = "OfficialDownloadPayload",
            Preset = seedValidation.Snapshot.Preset,
            PatchLabel = seedValidation.Snapshot.PatchLabel,
            CapturedAt = seedValidation.Snapshot.CapturedAt,
            Hash = seedValidation.SemanticHash!,
            Layouts = seedValidation.Snapshot.Layouts,
            RankBracket = seedValidation.Snapshot.RankBracket,
            PeriodStart = seedValidation.Snapshot.PeriodStart,
            PeriodEnd = seedValidation.Snapshot.PeriodEnd,
            MinimumSampleMatches = seedValidation.Snapshot.MinimumSampleMatches,
            UsesDerivedPositions = seedValidation.Snapshot.UsesDerivedPositions,
            PositionDefinition = seedValidation.Snapshot.PositionDefinition,
            RoleResults = [],
            ProviderStatus = ProviderStatus.Online,
            OriginKind = GridOriginKind.NativeD2pt,
            SourceDetails = seedValidation.Snapshot.SourceDetails,
            RawSha256 = seedValidation.RawSha256,
            OriginalFilename = seedValidation.Snapshot.OriginalFilename,
            ConfigCount = seedValidation.Snapshot.ConfigCount,
            PayloadSizeBytes = seedValidation.Snapshot.PayloadSizeBytes,
            ParsedGrid = seedValidation.GridFile,
            RawPayloadBytes = Encoding.UTF8.GetBytes(seedPayload)
        }, seedPayload, CancellationToken.None);

        var retrieval = new FakeOfficialRetrievalService(D2ptOfficialGridRetrievalResult.Failure(
            ProviderStatus.CloudflareBlocked,
            "CloudflareChallenge",
            "Cloudflare blocked the official page."));

        var provider = new OfficialD2ptHeroGridProvider(retrieval, cache, logging);
        var snapshot = await provider.FetchAsync(HeroGridPreset.HighWinrate, CancellationToken.None);

        Assert.Equal(GridOriginKind.Cached, snapshot.OriginKind);
        Assert.Equal(ProviderStatus.Cached, snapshot.ProviderStatus);
        Assert.Equal("CachedOfficialDownloadPayload", snapshot.SourceStrategy);
        Assert.Equal(seedValidation.SemanticHash, snapshot.Hash);
        Assert.NotNull(snapshot.ParsedGrid);
        Assert.NotNull(snapshot.RawPayloadBytes);
    }

    [Fact]
    public async Task UpdateService_AcceptsOfficialSnapshot_WithoutRoleResults()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logging = new FileLoggingService(paths);
        var backupService = new BackupService(paths);
        var historyService = new HistoryService(paths);
        var dotaGridService = new DotaGridService();
        var validation = D2ptOfficialGridPayloadValidator.Validate(LoadOfficialFixture(), "dota2protracker_hero_grid_high_winrate_config.json");
        Assert.True(validation.IsValid, validation.Message);

        var snapshot = new HeroGridSnapshot
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
            RoleResults = [],
            ProviderStatus = ProviderStatus.Online,
            OriginKind = GridOriginKind.NativeD2pt,
            SourceDetails = validation.Snapshot.SourceDetails,
            RawSha256 = validation.RawSha256,
            OriginalFilename = validation.Snapshot.OriginalFilename,
            ConfigCount = validation.Snapshot.ConfigCount,
            PayloadSizeBytes = validation.Snapshot.PayloadSizeBytes,
            ParsedGrid = validation.GridFile,
            RawPayloadBytes = Encoding.UTF8.GetBytes(LoadOfficialFixture())
        };

        var updateService = new UpdateService(new StubProvider(snapshot), dotaGridService, backupService, historyService, logging, new FakeHeroCatalogService(500));
        var account = CreateMissingGridAccountFixture(paths);
        var result = await updateService.CheckForUpdatesAsync([account], new AppSettings(), false, CancellationToken.None);

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.NotNull(result.ConfirmedInstallSnapshot);
        Assert.Equal(snapshot.Hash, result.GridHash);
    }

    [Fact]
    public void ProviderValidation_Fails_ForInvalidJson()
    {
        var result = D2ptOfficialGridPayloadValidator.Validate("{ invalid", "dota2protracker_hero_grid_high_winrate_config.json");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ProviderValidation_Fails_ForHtmlChallenge()
    {
        var result = D2ptOfficialGridPayloadValidator.Validate("<html>Just a moment...</html>", "dota2protracker_hero_grid_high_winrate_config.json");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ProviderValidation_Fails_ForEmptyConfigs()
    {
        var result = D2ptOfficialGridPayloadValidator.Validate("{\"version\":3,\"configs\":[]}", "dota2protracker_hero_grid_high_winrate_config.json");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ProviderValidation_Fails_ForUnsupportedVersion()
    {
        var result = D2ptOfficialGridPayloadValidator.Validate("{\"version\":2,\"configs\":[{\"config_name\":\"Carry\",\"categories\":[{\"category_name\":\"Carry\",\"hero_ids\":[1],\"x_position\":0,\"y_position\":0,\"width\":100,\"height\":100}]}]}", "dota2protracker_hero_grid_high_winrate_config.json");
        Assert.False(result.IsValid);
    }

    private static string LoadOfficialFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "dota2protracker_hero_grid_high_winrate_config.json");
        return File.ReadAllText(path);
    }

    private static string ComputeSha256(string payload)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    private static SteamAccount CreateMissingGridAccountFixture(TestAppPaths paths)
    {
        var accountDir = Path.Combine(paths.RootDirectory, "steam", "123456789", "570", "remote", "cfg");
        Directory.CreateDirectory(accountDir);
        return new SteamAccount
        {
            AccountId = "123456789",
            SteamRootPath = Path.Combine(paths.RootDirectory, "steam"),
            UserDataPath = Path.Combine(paths.RootDirectory, "steam", "123456789"),
            DotaConfigDirectory = accountDir,
            DisplayName = "Steam 123456789",
            HasDotaUserData = true,
            HasHeroGridConfig = false,
            IsSelected = true
        };
    }

    private sealed class FakeOfficialRetrievalService(D2ptOfficialGridRetrievalResult result) : ID2ptOfficialGridRetrievalService
    {
        public Task<D2ptOfficialGridRetrievalResult> RetrieveOfficialHighWinrateAsync(bool forceRefresh, CancellationToken cancellationToken)
            => Task.FromResult(result);
    }
}
