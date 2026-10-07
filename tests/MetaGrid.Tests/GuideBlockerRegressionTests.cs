using System.Text.Json;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;

namespace MetaGrid.Tests;

public sealed class GuideBlockerRegressionTests
{
    [Theory]
    [InlineData(102, GuideRole.HardSupport)]
    [InlineData(65, GuideRole.Support)]
    [InlineData(58, GuideRole.Support)]
    [InlineData(121, GuideRole.HardSupport)]
    [InlineData(52, GuideRole.Support)]
    [InlineData(136, GuideRole.HardSupport)]
    [InlineData(114, GuideRole.Mid)]
    [InlineData(88, GuideRole.HardSupport)]
    [InlineData(84, GuideRole.HardSupport)]
    [InlineData(86, GuideRole.HardSupport)]
    [InlineData(20, GuideRole.Support)]
    [InlineData(37, GuideRole.HardSupport)]
    [InlineData(30, GuideRole.Support)]
    public void RealPartialRecommendations_MapAndRoundtripWithoutInventedSections(int hero, GuideRole role)
    {
        var (source, result) = Map(hero, role);
        Assert.True(result.Succeeded, result.Error);
        Assert.NotEmpty(source.RawTalentCandidates);
        var serializer = new ValveGuideSerializer();
        var parsed = serializer.Parse(serializer.Serialize(result.Build!, 1, DateTimeOffset.UtcNow).Text);
        Assert.Equal(result.Build!.ItemGroups.SelectMany(g => g.ItemIds), parsed.ItemGroups.SelectMany(g => g.ItemIds));
        Assert.Equal(source.TalentChoices.Select(t => t.TalentId), parsed.TalentChoices.Select(t => t.TalentId));
        Assert.All(parsed.TalentChoices, choice => Assert.Contains(source.RawTalentCandidates, c => c.TalentId == choice.TalentId));
        Assert.Equal(result.Build!.SkillOrder, parsed.SkillOrder);
    }

    [Theory]
    [InlineData(62, GuideRole.Support)]
    [InlineData(145, GuideRole.Carry)]
    [InlineData(54, GuideRole.Carry)]
    public void HistoricalLearnLevels_DoNotOverrideCurrentDisplayedTalentTree(int hero, GuideRole role)
    {
        var (_, result) = Map(hero, role);
        Assert.True(result.Succeeded, result.Error);
        Assert.NotNull(result.Build);
    }

    [Theory]
    [InlineData("+0.350s Duration", "0,35 s duration", true)]
    [InlineData("\u221220s Cooldown", "-20 s cooldown", true)]
    [InlineData("+20 Movement Speed", "+15 Movement Speed", false)]
    [InlineData("-20s Cooldown", "+20s Cooldown", false)]
    [InlineData("20% Damage", "20 Damage", false)]
    public void SemanticNormalization_DoesNotChangeValueSignOrUnits(string a, string b, bool same)
        => Assert.Equal(same, CurrentValveTalentResolver.NormalizeLabel(a) == CurrentValveTalentResolver.NormalizeLabel(b));

    [Fact]
    public async Task UsefulItemsOnlyGuide_DoesNotRequireAbilityPlanning()
    {
        var source = InstalledGuideMappingTests.Build();
        source.SkillOrder.Clear();
        source.TalentChoices.Clear();
        source.ItemGroups.RemoveRange(1, 4);
        var mapped = await InstalledGuideMappingTests.Resolver().ResolveAsync(
            InstalledGuideMappingTests.Account(), InstalledGuideMappingTests.Subscription(), source, default);
        Assert.True(mapped.Succeeded, mapped.Error);
        var serializer = new ValveGuideSerializer();
        var parsed = serializer.Parse(serializer.Serialize(mapped.Build!, 1, DateTimeOffset.UtcNow).Text);
        Assert.Empty(parsed.SkillOrder);
        Assert.Empty(parsed.TalentChoices);
        Assert.Equal(5, parsed.ItemGroups.Single().ItemIds.Count);
    }

    [Theory]
    [InlineData(AppLanguage.English, "Source/patch mismatch")]
    [InlineData(AppLanguage.Russian, "Источник не соответствует патчу")]
    public void PatchMismatch_HasCompactLocalizedStatus(AppLanguage language, string expected)
        => Assert.Equal(expected, GuideStatusPresentation.Label(new UiTextService { Language = language }, true,
            GuideSubscriptionStatus.SourcePatchIncompatible));

    private static (NormalizedHeroGuideBuild, DotaGuideMappingResult) Map(int hero, GuideRole role)
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "guide-blockers-index.json")));
        var catalogs = JsonSerializer.Deserialize<Dictionary<string, InstalledDotaGuideCatalog>>(
            File.ReadAllText(Path.Combine(fixtures, "guide-blockers-catalogs.json")))!;
        var snapshot = catalogs[hero.ToString()];
        // JSON does not preserve the production localization dictionary's comparer.
        var catalog = new InstalledDotaGuideCatalog { Hero = snapshot.Hero, Items = snapshot.Items,
            Labels = new Dictionary<string, string>(snapshot.Labels, StringComparer.OrdinalIgnoreCase),
            TalentDefinitions = snapshot.TalentDefinitions, Evidence = snapshot.Evidence };
        var source = D2ptLiveGuideParser.ParseIndex(index.RootElement, hero, role);
        var record = new GuideSubscriptionRecord { HeroId = hero, HeroInternalName = catalog.Hero.Key, Role = role };
        return (source, DotaGuideMappingResolver.ResolveInstalled(InstalledGuideMappingTests.Account(), record,
            source, default, (_, _, _) => catalog));
    }
}
