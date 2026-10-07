using System.Text.Json;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;

namespace MetaGrid.Tests;

public sealed class UniversalGuideAuditTests
{
    private static JsonDocument Index() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "d2pt-build-index-captured.json")));
    private static InstalledDotaGuideCatalog KezCatalog() => JsonSerializer.Deserialize<InstalledDotaGuideCatalog>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "kez-mid-installed-catalog.json")))!;

    [Theory]
    [InlineData(145, GuideRole.Mid, "Mage Slayer Core")]
    [InlineData(145, GuideRole.Carry, "Desolator")]
    public void Discovery_SelectsRealMostPlayedBuild(int hero, GuideRole role, string title)
    {
        using var index = Index();
        var build = D2ptLiveGuideParser.ParseIndex(index.RootElement, hero, role);
        Assert.Equal(hero, build.HeroId);
        Assert.Equal(role, build.Role);
        Assert.Equal(title, build.SourceTitle);
        Assert.Equal("https://dota2protracker.com/builds", build.SourceUrl);
        Assert.Equal(role == GuideRole.Mid ? "7.41e" : "7.41c", build.GameplayVersion);
        Assert.Equal(64, build.CanonicalSourceHash.Length);
    }

    [Theory]
    [InlineData(GuideRole.Offlane)]
    [InlineData(GuideRole.Support)]
    [InlineData(GuideRole.HardSupport)]
    public void MissingRole_IsExplicitNoBuild_NotUnsupported(GuideRole role)
    {
        using var index = Index();
        Assert.Throws<D2ptNoGuideBuildException>(() => D2ptLiveGuideParser.ParseIndex(index.RootElement, 145, role));
    }

    [Fact]
    public void KezMid_CurrentValveTalentStartAndLegalSourceSkills_Roundtrip()
    {
        using var index = Index();
        var build = D2ptLiveGuideParser.ParseIndex(index.RootElement, 145, GuideRole.Mid);
        var subscription = new GuideSubscriptionRecord { HeroId = 145, HeroDisplayName = "Kez", HeroInternalName = "npc_dota_hero_kez", Role = GuideRole.Mid };
        var mapped = DotaGuideMappingResolver.ResolveInstalled(InstalledGuideMappingTests.Account(), subscription, build, default, (_, _, _) => KezCatalog());
        Assert.True(mapped.Succeeded, mapped.Error);
        Assert.False(build.SkillOrder.SequenceEqual(mapped.Build!.SkillOrder));
        Assert.All(mapped.Build.SkillOrder.Select((skill, level) => (skill, level)), entry =>
            Assert.Contains(build.SkillCandidates[entry.level], c => c.Ability == entry.skill));
        Assert.Equal(10, mapped.Build.SkillOrder.Count);
        Assert.Equal(4, mapped.Build.TalentChoices.Count);
        Assert.Equal(build.ItemGroups.Sum(g => g.ItemIds.Count), mapped.Build.ItemGroups.Sum(g => g.ItemIds.Count));
        var serializer = new ValveGuideSerializer();
        var parsed = serializer.Parse(serializer.Serialize(mapped.Build, 1, DateTimeOffset.UtcNow).Text);
        Assert.Equal(mapped.Build.SkillOrder, parsed.SkillOrder);
        Assert.True(GuideDeletionOwnership.IsOwned(InstalledGuideMappingTests.Account(), subscription,
            "guides/kez_metagrid_mid_.build", parsed));
        Assert.False(GuideDeletionOwnership.IsOwned(InstalledGuideMappingTests.Account(), subscription,
            "guides/wisp_metagrid_mid_.build", parsed));
        subscription.HeroId = 1;
        Assert.False(GuideDeletionOwnership.IsOwned(InstalledGuideMappingTests.Account(), subscription,
            "guides/kez_metagrid_mid_.build", parsed));
    }

    [Theory]
    [InlineData("item")]
    [InlineData("talent")]
    public void MissingCurrentToken_IsNotSilentlyDropped(string kind)
    {
        using var index = Index();
        var build = D2ptLiveGuideParser.ParseIndex(index.RootElement, 145, GuideRole.Mid);
        if (kind == "item") build.ItemGroups[0].ItemIds.Add("unknown_item");
        else build.TalentChoices[0].TalentId = "unknown_talent";
        var subscription = new GuideSubscriptionRecord { HeroId = 145, HeroDisplayName = "Kez", HeroInternalName = "npc_dota_hero_kez", Role = GuideRole.Mid };
        Assert.False(DotaGuideMappingResolver.ResolveInstalled(InstalledGuideMappingTests.Account(), subscription, build, default, (_, _, _) => KezCatalog()).Succeeded);
    }

    [Fact]
    public void OlderHeroAndIo_AreDiscoveredFromSameIndex()
    {
        using var index = Index();
        Assert.Equal(1, D2ptLiveGuideParser.ParseIndex(index.RootElement, 1, GuideRole.Carry).HeroId);
        Assert.Equal(91, D2ptLiveGuideParser.ParseIndex(index.RootElement, 91, GuideRole.Mid).HeroId);
    }

    [Fact]
    public async Task NonIoSubscription_ReachesProvider_AndNoBuildStopsBeforeSteam()
    {
        var provider = new MissingBuildProvider();
        var service = new GuideSubscriptionService(provider, null!, new ValveGuideSerializer(), null!, new SilentLogger());
        var record = new GuideSubscriptionRecord { HeroId = 145, HeroInternalName = "npc_dota_hero_kez", HeroDisplayName = "Kez", Role = GuideRole.Offlane, IsEnabled = true };
        var result = await service.SyncAsync(InstalledGuideMappingTests.Account(), record, default);
        Assert.True(provider.Called);
        Assert.Equal(GuideSubscriptionStatus.NoBuild, result.Status);
        Assert.Null(result.RemoteFile);
    }

    [Theory]
    [InlineData(AppLanguage.English, "No D2PT build")]
    [InlineData(AppLanguage.Russian, "Нет билда D2PT")]
    public void NoBuild_IsLocalizedAndNeverClaimsUnsupported(AppLanguage language, string expected)
        => Assert.Equal(expected, GuideStatusPresentation.Label(new UiTextService { Language = language }, true, GuideSubscriptionStatus.NoBuild));

    private sealed class MissingBuildProvider : ID2ptGuideProvider
    {
        public bool Called { get; private set; }
        public Task<NormalizedHeroGuideBuild> FetchAsync(GuideSubscriptionRecord record, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Called = true;
            throw new D2ptNoGuideBuildException(record.HeroId, record.Role);
        }
    }

    private sealed class SilentLogger : ILoggingService
    {
        public string GetLogsDirectory() => string.Empty;
        public Task LogAsync(LogLevelKind level, string message, object? data = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
