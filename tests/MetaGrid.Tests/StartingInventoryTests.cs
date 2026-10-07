using System.Text.Json;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;

namespace MetaGrid.Tests;

public sealed class StartingInventoryTests
{
    private static NormalizedHeroGuideBuild Build() => new()
    {
        HeroId = 62, HeroInternalName = "npc_dota_hero_bounty_hunter", Role = GuideRole.Support,
        ItemGroups = [new() { CategoryKey = "#DOTA_Item_Build_Starting_Items", ItemIds = ["incorrect"] },
            new() { CategoryKey = "#DOTA_Item_Build_Core_Items", ItemIds = ["item_tranquil_boots"] }],
        SkillOrder = ["bounty_hunter_wind_walk"],
        TalentChoices = [new() { Level = 25, TalentId = "special_bonus_unique_bounty_hunter_8" }]
    };

    private static string Html(object[] options) => "<script>__sveltekit_test={data:" + JsonSerializer.Serialize(new object[]
    {
        new { data = new { itemsMapping = new Dictionary<string, object>
        { ["29"] = new { name = "item_boots" }, ["44"] = new { name = "item_tango" },
            ["1123"] = new { name = "item_blood_grenade" }, ["16"] = new { name = "item_branches" },
            ["218"] = new { name = "item_ward_dispenser" }, ["43"] = new { name = "item_ward_sentry" } } } },
        new { data = new { heroId = 62, buildData = new[] { new { hero_id = 62, position = "pos 4", build_id = 0,
            build_data = new { starting_inventory_options = options } } } } }
    }) + "};</script>";

    private static object Variant(int count, double rate, params int[] ids) => new object[]
    {
        ids.Select((id, index) => new { item_id = id, slot = "item" + index, charges = 0, secondary_charges = 0 }).ToArray(),
        new { count, win_rate = rate }
    };

    [Fact]
    public void BountyHunter5618Winner_IsOneInventory_NotUnionOrWinRate()
    {
        var build = Build();
        D2ptLiveGuideParser.ApplyExactStartingInventories(build, Html([
            Variant(5618, .562, 29, 218, 1123), Variant(1112, .567, 29, 43, 1123), Variant(476, .555, 29, 218, 1123)]));
        Assert.Equal(new[] { "item_boots", "item_ward_dispenser", "item_blood_grenade" }, build.ItemGroups[0].ItemIds);
        Assert.Equal(3, build.StartingInventories.Count);
        Assert.Equal(5618, D2ptLiveGuideParser.SelectStartingInventory(build.StartingInventories)!.MatchCount);
        Assert.Equal(0, build.SelectedStartingInventoryOrder);
    }

    [Fact]
    public void DuplicateSlotsAndOrder_ArePreserved()
    {
        var build = Build();
        D2ptLiveGuideParser.ApplyExactStartingInventories(build, Html([Variant(10, .4, 16, 29, 16)]));
        Assert.Equal(new[] { "item_branches", "item_boots", "item_branches" }, build.ItemGroups[0].ItemIds);
        Assert.Equal(3, build.StartingInventories[0].Items.Count);
    }

    [Fact]
    public void EqualMatchCount_UsesSourceOrder_NotWinRate()
    {
        var build = Build();
        D2ptLiveGuideParser.ApplyExactStartingInventories(build, Html([Variant(10, .2, 29), Variant(10, .9, 44)]));
        Assert.Equal("item_boots", Assert.Single(build.ItemGroups[0].ItemIds));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidMatchCount_FailsClosed(int count)
        => Assert.Throws<GuideSourceDataException>(() => D2ptLiveGuideParser.ApplyExactStartingInventories(Build(), Html([Variant(count, .5, 29)])));

    [Fact]
    public void MissingMatchCount_FailsClosed()
        => Assert.Throws<GuideSourceDataException>(() => D2ptLiveGuideParser.ApplyExactStartingInventories(Build(), Html([
            new object[] { new[] { new { item_id = 29, slot = "item0" } }, new { win_rate = .5 } }])));

    [Fact]
    public void OtherGroupsSkillsAndSharedVision_AreUnchanged()
    {
        var build = Build();
        var other = JsonSerializer.Serialize(build.ItemGroups.Skip(1));
        D2ptLiveGuideParser.ApplyExactStartingInventories(build, Html([Variant(10, .5, 29)]));
        Assert.Equal(other, JsonSerializer.Serialize(build.ItemGroups.Skip(1)));
        Assert.Equal("bounty_hunter_wind_walk", Assert.Single(build.SkillOrder));
        Assert.Equal("special_bonus_unique_bounty_hunter_8", Assert.Single(build.TalentChoices).TalentId);
    }

    [Fact]
    public void CorrectedItems_ChangeCanonicalAndEffectiveHashes()
    {
        var build = Build();
        var serializer = new ValveGuideSerializer();
        var resolved = new ResolvedHeroGuideBuild { SourceBuild = build, ItemGroups = build.ItemGroups };
        var oldSource = serializer.ComputeCanonicalSourceHash(build);
        var oldEffective = serializer.ComputeEffectiveGuideHash(resolved);
        D2ptLiveGuideParser.ApplyExactStartingInventories(build, Html([Variant(10, .5, 29)]));
        Assert.NotEqual(oldSource, serializer.ComputeCanonicalSourceHash(build));
        Assert.NotEqual(oldEffective, serializer.ComputeEffectiveGuideHash(resolved));
        Assert.Equal(NormalizedHeroGuideBuild.CurrentStartingItemsVersion, build.StartingItemsVersion);
        Assert.Equal(0, Build().StartingItemsVersion);
    }

    [Fact]
    public void QuantityExpansion_PreservesOrderedCopies()
        => Assert.Equal(new[] { "item_branches", "item_branches", "item_tango" },
            D2ptLiveGuideParser.ExpandStartingInventory(new(10, .5, 0, "test", [new("item_branches", 2), new("item_tango", 1)])));

    [Fact]
    public void LiteralDate_IsDecodedWithoutExecutingJavascript()
    {
        var html = Html([Variant(10, .5, 29)]).Replace("\"heroId\":62", "\"date\":new Date(1789500634000),\"heroId\":62");
        D2ptLiveGuideParser.ApplyExactStartingInventories(Build(), html);
    }

    [Fact]
    public void ExplicitEmptyInventory_IsNotMalformed_AndDoesNotMergeIntoWinner()
    {
        var build = Build();
        D2ptLiveGuideParser.ApplyExactStartingInventories(build, Html([Variant(10, .5, 29), Variant(5, .9)]));
        Assert.Equal(2, build.StartingInventories.Count);
        Assert.Equal("item_boots", Assert.Single(build.ItemGroups[0].ItemIds));
    }

    [Fact]
    public void OldOrInconsistentCache_CannotPassCurrentSemantics()
    {
        var build = Build();
        Assert.False(D2ptLiveGuideParser.HasCurrentStartingInventory(build));
        D2ptLiveGuideParser.ApplyExactStartingInventories(build, Html([Variant(10, .5, 29)]));
        Assert.True(D2ptLiveGuideParser.HasCurrentStartingInventory(build));
        build.ItemGroups[0].ItemIds.Add("item_tango");
        Assert.False(D2ptLiveGuideParser.HasCurrentStartingInventory(build));
    }
}
