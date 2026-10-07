using MetaGrid.Core.Models;
using MetaGrid.Core.Services;
using MetaGrid.Infrastructure.Services;

namespace MetaGrid.Tests;

public sealed class StartingMultisetTests
{
    [Theory]
    [InlineData(1, 2, true, 2)]
    [InlineData(3, 3, true, 1)]
    [InlineData(3, 6, true, 2)]
    [InlineData(1, 5, false, 1)]
    public void Charges_RespectValvePurchaseBundle(int initial, int charges, bool stackable, int expected)
    {
        var definition = new ValveDataNode("fixture", null,
            [new("ItemStackable", stackable ? "1" : "0", []), new("ItemInitialCharges", initial.ToString(), [])]);
        Assert.Equal(expected, StartingItemsIntegrity.PurchasesPerSlot(new("fixture", 1, Charges: charges), definition));
    }

    [Fact]
    public void PartialChargeBundle_IsExplicitBlocker()
    {
        var definition = new ValveDataNode("item_tango", null,
            [new("ItemStackable", "1", []), new("ItemInitialCharges", "3", [])]);
        Assert.Throws<InvalidDataException>(() => StartingItemsIntegrity.PurchasesPerSlot(new("item_tango", 1, Charges: 2), definition));
        Assert.Equal(4, StartingItemsIntegrity.PurchasesPerSlot(new("item_tango", 2, Charges: 6), definition));
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(6, 2)]
    public async Task ProductionMapper_ExpandsPurchases_AndSerializerPreservesThem(int charges, int purchases)
    {
        var catalog = InstalledGuideMappingTests.Catalog();
        ((Dictionary<string, ValveDataNode>)catalog.Items).Add("item_tango", new("item_tango", null,
            [new("ItemStackable", "1", []), new("ItemInitialCharges", "3", [])]));
        var build = InstalledGuideMappingTests.Build();
        build.StartingItemsVersion = NormalizedHeroGuideBuild.CurrentStartingItemsVersion;
        build.StartingInventories = [new(10, .5, 0, "fixture", [new("item_tango", 1, Charges: charges)])];
        build.SelectedStartingInventoryOrder = 0;
        build.ItemGroups[0].ItemIds = ["item_tango"];
        var result = await new DotaGuideMappingResolver((_, _, _) => catalog).ResolveAsync(
            InstalledGuideMappingTests.Account(), InstalledGuideMappingTests.Subscription(), build, default);
        Assert.True(result.Succeeded, result.Error);
        var serializer = new ValveGuideSerializer();
        var parsed = serializer.Parse(serializer.Serialize(result.Build!, 1, DateTimeOffset.UtcNow).Text);
        Assert.Equal(purchases, StartingItemsIntegrity.GroupItems(parsed.ItemGroups).Count);
        Assert.All(StartingItemsIntegrity.GroupItems(parsed.ItemGroups), id => Assert.Equal("item_tango", id));
    }

    private static ResolvedHeroGuideBuild Guide(string[] expected, string[]? actual = null) => new()
    {
        SourceBuild = new() { GameplayVersion = "7.41f" }, HeroToken = "life_stealer", GuideTitle = "Deterministic test",
        ExpectedStartingItems = expected.ToList(),
        ItemGroups = [new() { CategoryKey = StartingItemsIntegrity.Category, ItemIds = (actual ?? expected).ToList() }]
    };

    [Fact]
    public void Lifestealer_FaerieFire_SurvivesActualSerializer()
    {
        string[] expected = ["item_quelling_blade", "item_gauntlets", "item_gauntlets", "item_gauntlets", "item_faerie_fire"];
        var serializer = new ValveGuideSerializer();
        var actual = StartingItemsIntegrity.GroupItems(serializer.Parse(serializer.Serialize(Guide(expected), 1, DateTimeOffset.UtcNow).Text).ItemGroups);
        Assert.True(StartingItemsMultiset.Compare(expected, actual).IsMatch);
        Assert.Equal(1, StartingItemsMultiset.Count(actual)["item_faerie_fire"]);
    }

    [Fact]
    public void Kez_ExplicitQuantities_AreNotDeduplicated()
    {
        string[] expected = ["item_quelling_blade", "item_branches", "item_branches", "item_magic_stick",
            "item_tango", "item_tango", "item_tango", "item_faerie_fire"];
        var serializer = new ValveGuideSerializer();
        var actual = StartingItemsIntegrity.GroupItems(serializer.Parse(serializer.Serialize(Guide(expected), 1, DateTimeOffset.UtcNow).Text).ItemGroups);
        Assert.True(StartingItemsMultiset.Compare(expected, actual).IsMatch);
        Assert.Equal(3, StartingItemsMultiset.Count(actual)["item_tango"]);
        Assert.Equal(2, StartingItemsMultiset.Count(actual)["item_branches"]);
    }

    [Fact]
    public void BountyHunter_ExactMultiset_RoundTrips()
    {
        string[] expected = ["item_boots", "item_ward_dispenser", "item_blood_grenade"];
        var serializer = new ValveGuideSerializer();
        Assert.True(StartingItemsMultiset.Compare(expected,
            StartingItemsIntegrity.GroupItems(serializer.Parse(serializer.Serialize(Guide(expected), 1, DateTimeOffset.UtcNow).Text).ItemGroups)).IsMatch);
    }

    [Fact]
    public void MissingFaerieFire_IsFailure_NotReady()
    {
        var difference = StartingItemsMultiset.Compare(["item_quelling_blade", "item_faerie_fire"], ["item_quelling_blade"]);
        Assert.False(difference.IsMatch);
        Assert.Equal(1, difference.Missing["item_faerie_fire"]);
        Assert.Throws<InvalidDataException>(() => new ValveGuideSerializer().Serialize(
            Guide(["item_quelling_blade", "item_faerie_fire"], ["item_quelling_blade"]), 1, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ExtraItem_IsFailure()
    {
        var difference = StartingItemsMultiset.Compare(["item_boots"], ["item_boots", "item_branches"]);
        Assert.False(difference.IsMatch);
        Assert.Equal(1, difference.Extra["item_branches"]);
    }

    [Fact]
    public void SameDistinctIds_WrongQuantities_IsFailure()
    {
        var difference = StartingItemsMultiset.Compare(["item_gauntlets", "item_gauntlets", "item_gauntlets"], ["item_gauntlets", "item_gauntlets"]);
        Assert.False(difference.IsMatch);
        Assert.Equal(1, difference.Missing["item_gauntlets"]);
        Assert.Equal("expected=3; actual=2", difference.WrongQuantities["item_gauntlets"]);
    }

    [Fact]
    public void Ordering_IsSeparateFromMultisetEquality()
        => Assert.True(StartingItemsMultiset.Compare(["item_boots", "item_branches"], ["item_branches", "item_boots"]).IsMatch);

    [Fact]
    public void UnresolvedSource_IsExplicitMappingBlocker()
    {
        var build = new NormalizedHeroGuideBuild { StartingItemsVersion = NormalizedHeroGuideBuild.CurrentStartingItemsVersion,
            StartingInventories = [new(10, .5, 0, "fixture", [new("unresolved_source", 1)])], SelectedStartingInventoryOrder = 0,
            ItemGroups = [new() { CategoryKey = StartingItemsIntegrity.Category, ItemIds = ["unresolved_source"] }] };
        var error = Assert.Throws<InvalidDataException>(() => StartingItemsIntegrity.ResolveExpected(build,
            _ => throw new InvalidDataException("unknown source item")));
        Assert.Contains("Starting Items mapping blocker", error.Message);
    }

    [Fact]
    public void LostNormalizedItem_IsBlockedBeforeResolution()
    {
        var build = new NormalizedHeroGuideBuild { StartingItemsVersion = NormalizedHeroGuideBuild.CurrentStartingItemsVersion,
            StartingInventories = [new(10, .5, 0, "fixture", [new("item_quelling_blade", 1), new("item_faerie_fire", 1)])], SelectedStartingInventoryOrder = 0,
            ItemGroups = [new() { CategoryKey = StartingItemsIntegrity.Category, ItemIds = ["item_quelling_blade"] }] };
        Assert.Throws<InvalidDataException>(() => StartingItemsIntegrity.ResolveExpected(build, i => i));
    }

    [Fact]
    public async Task MissingNormalizedCopy_CannotBecomeReadyInProductionResolver()
    {
        var build = InstalledGuideMappingTests.Build();
        build.StartingItemsVersion = NormalizedHeroGuideBuild.CurrentStartingItemsVersion;
        build.StartingInventories = [new(10, .5, 0, "fixture", [new("item_branches", 5)])];
        build.SelectedStartingInventoryOrder = 0;
        build.ItemGroups[0].ItemIds.RemoveAt(0);
        var result = await InstalledGuideMappingTests.Resolver().ResolveAsync(InstalledGuideMappingTests.Account(),
            InstalledGuideMappingTests.Subscription(), build, default);
        Assert.False(result.Succeeded);
        Assert.Null(result.Build);
        Assert.Contains("Starting Items mapping blocker", result.Error);
    }

    [Fact]
    public async Task UnresolvedStartingItem_CannotBecomeReadyInProductionResolver()
    {
        var build = InstalledGuideMappingTests.Build();
        build.StartingItemsVersion = NormalizedHeroGuideBuild.CurrentStartingItemsVersion;
        build.StartingInventories = [new(10, .5, 0, "fixture", [new("item_unmapped_fixture", 1)])];
        build.SelectedStartingInventoryOrder = 0;
        build.ItemGroups[0].ItemIds = ["item_unmapped_fixture"];
        var result = await InstalledGuideMappingTests.Resolver().ResolveAsync(InstalledGuideMappingTests.Account(),
            InstalledGuideMappingTests.Subscription(), build, default);
        Assert.False(result.Succeeded);
        Assert.Contains("Starting Items mapping blocker", result.Error);
    }
}
