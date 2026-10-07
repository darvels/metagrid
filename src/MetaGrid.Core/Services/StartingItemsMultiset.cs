namespace MetaGrid.Core.Services;

public sealed record StartingItemsDifference(Dictionary<string, int> Expected, Dictionary<string, int> Actual,
    Dictionary<string, int> Missing, Dictionary<string, int> Extra, Dictionary<string, string> WrongQuantities)
{
    public bool IsMatch => Missing.Count == 0 && Extra.Count == 0;
}

public static class StartingItemsMultiset
{
    public static Dictionary<string, int> Count(IEnumerable<string> items)
        => items.GroupBy(i => i, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    public static StartingItemsDifference Compare(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var left = Count(expected);
        var right = Count(actual);
        var missing = new Dictionary<string, int>(StringComparer.Ordinal);
        var extra = new Dictionary<string, int>(StringComparer.Ordinal);
        var quantities = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in left.Keys.Union(right.Keys, StringComparer.Ordinal))
        {
            var e = left.GetValueOrDefault(id);
            var a = right.GetValueOrDefault(id);
            if (e > a) missing[id] = e - a;
            if (a > e) extra[id] = a - e;
            if (e > 0 && a > 0 && e != a) quantities[id] = $"expected={e}; actual={a}";
        }
        return new(left, right, missing, extra, quantities);
    }
}
