using System.Text.Json;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;

namespace MetaGrid.Tests;

public sealed class TalentPickRateTests
{
    [Theory]
    [InlineData("\"79.5%\"", .795)]
    [InlineData("\"90.2%\"", .902)]
    [InlineData("\"100%\"", 1)]
    [InlineData("\"0%\"", 0)]
    [InlineData("0.795", .795)]
    [InlineData("\"79,5%\"", .795)]
    public void Percentages_AreNumeric(string json, double expected)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(expected, D2ptLiveGuideParser.ParsePickRate(document.RootElement));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"unknown\"")]
    [InlineData("1.2")]
    public void MissingOrInvalidRates_AreExplicitSourceFailure(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(GuideFailureKind.SourceIncomplete,
            Assert.Throws<GuideSourceDataException>(() => D2ptLiveGuideParser.ParsePickRate(document.RootElement)).Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualBountyHunterTree_IsIndependentOfSourceRowOrder(bool descending)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture, "guide-blockers-index.json")));
        var build = D2ptLiveGuideParser.ParseIndex(index.RootElement, 62, GuideRole.Support);
        var snapshots = JsonSerializer.Deserialize<Dictionary<string, InstalledDotaGuideCatalog>>(
            File.ReadAllText(Path.Combine(fixture, "guide-blockers-catalogs.json")))!;
        var old = snapshots["62"];
        var data = new InstalledDotaGuideCatalog { Hero = old.Hero, Items = old.Items, TalentDefinitions = old.TalentDefinitions,
            Labels = new Dictionary<string, string>(old.Labels, StringComparer.OrdinalIgnoreCase), Evidence = old.Evidence };
        build.RawTalentCandidates = descending ? build.RawTalentCandidates.OrderByDescending(c => c.SourceLevel).ToList()
            : build.RawTalentCandidates.OrderBy(c => (c.SourceLevel + 13) % 17).ToList();
        D2ptCurrentTalentTree.Reconstruct(build, data);
        Assert.Equal("special_bonus_unique_bounty_hunter_2", build.TalentChoices.Single(c => c.Level == 20).TalentId);
        Assert.Equal("special_bonus_unique_bounty_hunter_8", build.TalentChoices.Single(c => c.Level == 25).TalentId);
        Assert.All(build.TalentEvidence, row => Assert.Equal(row.Candidates.Max(c => c.PickRate),
            row.Candidates.Single(c => c.TalentId == row.SelectedByPickRate).PickRate));
    }

    [Fact]
    public void Tie_IsOrdinalInternalId_NotWinRateOrInputOrder()
    {
        var build = InstalledGuideMappingTests.Build();
        var data = InstalledGuideMappingTests.Catalog();
        var hero = new ValveDataNode(data.Hero.Key, null, data.Hero.Children.Select(n =>
            n.Key == "Ability11" ? new ValveDataNode(n.Key, "special_bonus_unique_wisp_test", []) : n).ToArray());
        var labels = new Dictionary<string, string>(data.Labels, StringComparer.OrdinalIgnoreCase)
        { ["DOTA_Tooltip_ability_special_bonus_unique_wisp_test"] = "Other" };
        data = new() { Hero = hero, Items = data.Items, Labels = labels, Evidence = data.Evidence };
        build.RawTalentCandidates = [new(10, "special_bonus_unique_wisp_test", "Other", .5, 1, 10, 10),
            new(10, InstalledGuideMappingTests.Talents[0], "Selected", .5, 0, 10, 0)];
        build.TalentChoices = [new() { Level = 10, TalentId = InstalledGuideMappingTests.Talents[0], SelectedLabel = "Selected" }];
        D2ptCurrentTalentTree.Reconstruct(build, data);
        Assert.Equal(InstalledGuideMappingTests.Talents[0], build.TalentChoices.Single().TalentId);
    }
}
