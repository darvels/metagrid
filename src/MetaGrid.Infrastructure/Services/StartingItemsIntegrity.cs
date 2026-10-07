using System.Text.Json;
using MetaGrid.Core.Models;
using MetaGrid.Core.Services;

namespace MetaGrid.Infrastructure.Services;

public static class StartingItemsIntegrity
{
    public const string Category = "#DOTA_Item_Build_Starting_Items";

    public static List<string> GroupItems(IEnumerable<NormalizedGuideItemGroup> groups)
    {
        var starting = groups.Where(g => g.CategoryKey == Category).ToArray();
        if (starting.Length != 1) throw new InvalidDataException("Starting Items mapping blocker: expected exactly one starting group.");
        return starting[0].ItemIds;
    }

    public static void RequireMatch(IEnumerable<string> expected, IEnumerable<string> actual, string stage)
    {
        var difference = StartingItemsMultiset.Compare(expected, actual);
        if (!difference.IsMatch)
            throw new InvalidDataException("Starting Items mapping blocker at " + stage + ": " + JsonSerializer.Serialize(difference));
    }

    public static List<string> ResolveExpected(NormalizedHeroGuideBuild build, Func<string, string> resolveItem)
    {
        if (!D2ptLiveGuideParser.HasCurrentStartingInventory(build))
            throw new InvalidDataException("Starting Items mapping blocker: selected source evidence differs from normalized items or semantics are stale.");
        var selected = D2ptLiveGuideParser.SelectStartingInventory(build.StartingInventories);
        var source = selected is null ? [] : D2ptLiveGuideParser.ExpandStartingInventory(selected);
        RequireMatch(source, GroupItems(build.ItemGroups), "normalization");
        try { return source.Select(resolveItem).ToList(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException("Starting Items mapping blocker: unresolved selected source item. " + ex.Message, ex);
        }
    }

    public static int PurchasesPerSlot(GuideStartingItem item, ValveDataNode definition)
    {
        if (item.Charges is not > 0 || definition.Get("ItemStackable") != "1"
            || !int.TryParse(definition.Get("ItemInitialCharges"), out var initial) || initial <= 0) return item.Quantity;
        if (item.Charges.Value % initial != 0 || item.Charges.Value / initial > 32)
            throw new InvalidDataException("Starting Items mapping blocker: partial/unrepresentable stack for " + item.ItemId);
        return checked(item.Quantity * (item.Charges.Value / initial));
    }

    public static List<string> ResolveExpected(NormalizedHeroGuideBuild build, InstalledDotaGuideCatalog catalog)
    {
        _ = ResolveExpected(build, catalog.ResolveItem);
        var selected = D2ptLiveGuideParser.SelectStartingInventory(build.StartingInventories);
        return selected is null ? [] : selected.Items.SelectMany(item =>
        {
            var id = catalog.ResolveItem(item.ItemId);
            return Enumerable.Repeat(id, PurchasesPerSlot(item, catalog.Items[id]));
        }).ToList();
    }
}
