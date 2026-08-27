using System.Text.Json;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Utilities;

namespace MetaGrid.Infrastructure.Services;

public sealed class DotaGridService : IDotaGridService
{
    private const string MetaGridPrefix = "MetaGrid -";
    private static readonly string[] LegacyManagedPrefixes =
    [
        "MetaGrid - D2PT",
        MetaGridPrefix
    ];

    public async Task<DotaHeroGridFile> ReadAsync(string configPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(configPath))
        {
            return new DotaHeroGridFile();
        }

        await using var stream = File.OpenRead(configPath);
        return await JsonSerializer.DeserializeAsync<DotaHeroGridFile>(stream, JsonDefaults.StrictIndented, cancellationToken)
            ?? new DotaHeroGridFile();
    }

    public async Task<string?> ReadInstalledMetaGridHashAsync(string configPath, CancellationToken cancellationToken)
    {
        var file = await ReadAsync(configPath, cancellationToken);
        return ReadInstalledMetaGridHash(file);
    }

    public string? ReadInstalledMetaGridHash(DotaHeroGridFile file)
        => ExtractManagedGrid(file) is { } canonical ? ComputeCanonicalHash(canonical) : null;

    public DotaHeroGridFile ApplySnapshot(DotaHeroGridFile existing, HeroGridSnapshot snapshot)
    {
        var retained = existing.Configs
            .Where(x => !IsManagedLayoutName(x.ConfigName))
            .ToList();

        retained.AddRange(CreateLayouts(snapshot));
        existing.Configs = retained;
        existing.Version = Math.Max(existing.Version, 3);
        return existing;
    }

    public string NormalizeSnapshot(HeroGridSnapshot snapshot)
        => NormalizeCanonical(CanonicalizeSnapshot(snapshot));

    public CanonicalHeroGridSnapshot CanonicalizeSnapshot(HeroGridSnapshot snapshot)
        => new()
        {
            Version = snapshot.ParsedGrid?.Version ?? 3,
            Layouts = snapshot.Layouts
                .Select(layout => new CanonicalHeroGridLayout
                {
                    Name = layout.Name.Trim(),
                    Categories = layout.Categories
                        .Select((category, index) => ToCanonicalCategory(category, layout.Categories.Count, index))
                        .ToList()
                })
                .ToList()
        };

    public CanonicalHeroGridSnapshot? ExtractManagedGrid(DotaHeroGridFile file)
    {
        var layouts = file.Configs
            .Where(x => IsManagedLayoutName(x.ConfigName))
            .Select(layout => new CanonicalHeroGridLayout
            {
                Name = NormalizeManagedLayoutName(layout.ConfigName),
                Categories = layout.Categories
                    .Select(category => new CanonicalHeroGridCategory
                    {
                        Name = category.CategoryName.Trim(),
                        HeroIds = DistinctPreservingOrder(category.HeroIds),
                        XPosition = category.XPosition,
                        YPosition = category.YPosition,
                        Width = category.Width,
                        Height = category.Height
                    })
                    .ToList()
            })
            .ToList();

        return layouts.Count == 0
            ? null
            : new CanonicalHeroGridSnapshot
            {
                Version = file.Version,
                Layouts = layouts
            };
    }

    public string NormalizeCanonical(CanonicalHeroGridSnapshot snapshot)
        => $"v{snapshot.Version}|{string.Join("|", snapshot.Layouts.Select(layout =>
            $"{layout.Name}:{string.Join(';', layout.Categories.Select(category => $"{category.Name}@{FormatDouble(category.XPosition)},{FormatDouble(category.YPosition)},{FormatDouble(category.Width)},{FormatDouble(category.Height)}={string.Join(',', category.HeroIds)}"))}"))}";

    public string ComputeSnapshotHash(HeroGridSnapshot snapshot)
        => ComputeCanonicalHash(CanonicalizeSnapshot(snapshot));

    public string ComputeCanonicalHash(CanonicalHeroGridSnapshot snapshot)
        => HashUtilities.Sha256(NormalizeCanonical(snapshot));

    public string SummarizeDifference(CanonicalHeroGridSnapshot expected, CanonicalHeroGridSnapshot actual)
    {
        var differences = new List<string>();
        var layoutCount = Math.Max(expected.Layouts.Count, actual.Layouts.Count);
        for (var layoutIndex = 0; layoutIndex < layoutCount; layoutIndex++)
        {
            if (layoutIndex >= expected.Layouts.Count)
            {
                differences.Add($"Additional layout at position {layoutIndex + 1}: '{actual.Layouts[layoutIndex].Name}'.");
                continue;
            }

            if (layoutIndex >= actual.Layouts.Count)
            {
                differences.Add($"Missing layout at position {layoutIndex + 1}: '{expected.Layouts[layoutIndex].Name}'.");
                continue;
            }

            var expectedLayout = expected.Layouts[layoutIndex];
            var actualLayout = actual.Layouts[layoutIndex];
            if (!string.Equals(expectedLayout.Name, actualLayout.Name, StringComparison.Ordinal))
            {
                differences.Add($"Layout order/name changed at position {layoutIndex + 1}: expected '{expectedLayout.Name}', actual '{actualLayout.Name}'.");
                continue;
            }

            var categoryCount = Math.Max(expectedLayout.Categories.Count, actualLayout.Categories.Count);
            for (var categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                if (categoryIndex >= expectedLayout.Categories.Count)
                {
                    differences.Add($"Additional category in layout '{actualLayout.Name}' at position {categoryIndex + 1}: '{actualLayout.Categories[categoryIndex].Name}'.");
                    continue;
                }

                if (categoryIndex >= actualLayout.Categories.Count)
                {
                    differences.Add($"Missing category in layout '{expectedLayout.Name}' at position {categoryIndex + 1}: '{expectedLayout.Categories[categoryIndex].Name}'.");
                    continue;
                }

                var expectedCategory = expectedLayout.Categories[categoryIndex];
                var actualCategory = actualLayout.Categories[categoryIndex];
                if (!string.Equals(expectedCategory.Name, actualCategory.Name, StringComparison.Ordinal))
                {
                    differences.Add($"Category order/name changed in layout '{expectedLayout.Name}' at position {categoryIndex + 1}: expected '{expectedCategory.Name}', actual '{actualCategory.Name}'.");
                    continue;
                }

                if (!expectedCategory.HeroIds.SequenceEqual(actualCategory.HeroIds))
                {
                    differences.Add($"Hero ordering/content changed in layout '{expectedLayout.Name}' category '{expectedCategory.Name}': expected [{string.Join(", ", expectedCategory.HeroIds)}], actual [{string.Join(", ", actualCategory.HeroIds)}].");
                    continue;
                }

                if (!AreEqual(expectedCategory.XPosition, actualCategory.XPosition)
                    || !AreEqual(expectedCategory.YPosition, actualCategory.YPosition)
                    || !AreEqual(expectedCategory.Width, actualCategory.Width)
                    || !AreEqual(expectedCategory.Height, actualCategory.Height))
                {
                    differences.Add($"Geometry changed in layout '{expectedLayout.Name}' category '{expectedCategory.Name}': expected [{FormatDouble(expectedCategory.XPosition)}, {FormatDouble(expectedCategory.YPosition)}, {FormatDouble(expectedCategory.Width)}, {FormatDouble(expectedCategory.Height)}], actual [{FormatDouble(actualCategory.XPosition)}, {FormatDouble(actualCategory.YPosition)}, {FormatDouble(actualCategory.Width)}, {FormatDouble(actualCategory.Height)}].");
                }
            }
        }

        return differences.Count == 0 ? "No semantic differences." : string.Join(" | ", differences);
    }

    public string Serialize(DotaHeroGridFile file) => JsonSerializer.Serialize(file, JsonDefaults.StrictIndented);

    public bool TryValidate(string json, out string error)
    {
        try
        {
            JsonDocument.Parse(json);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static IEnumerable<DotaHeroGridLayout> CreateLayouts(HeroGridSnapshot snapshot)
    {
        foreach (var layout in snapshot.Layouts)
        {
            yield return new DotaHeroGridLayout
            {
                ConfigName = $"MetaGrid - {layout.Name}",
                Categories = CreateCategories(layout).ToList()
            };
        }
    }

    private static IEnumerable<DotaHeroGridCategory> CreateCategories(HeroGridLayout layout)
    {
        const double totalWidth = 1193.478271;
        const double columnWidth = 530.434814;
        const double rowHeight = 220;
        const double gapX = 96;
        const double gapY = 26;

        var index = 0;
        foreach (var category in layout.Categories)
        {
            var column = index % 2;
            var row = index / 2;
            var hasExplicitGeometry = category.Width > 0 && category.Height > 0;
            yield return new DotaHeroGridCategory
            {
                CategoryName = category.Name,
                XPosition = hasExplicitGeometry ? category.XPosition : (column == 0 ? 0 : columnWidth + gapX),
                YPosition = hasExplicitGeometry ? category.YPosition : row * (rowHeight + gapY),
                Width = hasExplicitGeometry ? category.Width : (layout.Categories.Count == 1 ? totalWidth : columnWidth),
                Height = hasExplicitGeometry ? category.Height : rowHeight,
                HeroIds = category.HeroIds.Distinct().ToList()
            };
            index++;
        }
    }

    private static string NormalizeManagedLayoutName(string configName)
    {
        foreach (var prefix in LegacyManagedPrefixes.OrderByDescending(x => x.Length))
        {
            var prefixWithSpacer = prefix.EndsWith("-", StringComparison.Ordinal) ? $"{prefix} " : $"{prefix} - ";
            if (configName.StartsWith(prefixWithSpacer, StringComparison.OrdinalIgnoreCase))
            {
                return configName[prefixWithSpacer.Length..].Trim();
            }
        }

        return configName.Trim();
    }

    private static bool IsManagedLayoutName(string configName)
        => LegacyManagedPrefixes.Any(prefix => configName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<int> DistinctPreservingOrder(IEnumerable<int> heroIds)
    {
        var seen = new HashSet<int>();
        var ordered = new List<int>();
        foreach (var heroId in heroIds)
        {
            if (seen.Add(heroId))
            {
                ordered.Add(heroId);
            }
        }

        return ordered;
    }

    private static CanonicalHeroGridCategory ToCanonicalCategory(HeroGridCategory category, int categoryCount, int index)
    {
        var geometry = ResolveGeometry(category, categoryCount, index);
        return new CanonicalHeroGridCategory
        {
            Name = category.Name.Trim(),
            HeroIds = DistinctPreservingOrder(category.HeroIds),
            XPosition = geometry.XPosition,
            YPosition = geometry.YPosition,
            Width = geometry.Width,
            Height = geometry.Height
        };
    }

    private static (double XPosition, double YPosition, double Width, double Height) ResolveGeometry(HeroGridCategory category, int categoryCount, int index)
    {
        const double totalWidth = 1193.478271;
        const double columnWidth = 530.434814;
        const double rowHeight = 220;
        const double gapX = 96;
        const double gapY = 26;

        if (category.Width > 0 && category.Height > 0)
        {
            return (category.XPosition, category.YPosition, category.Width, category.Height);
        }

        var column = index % 2;
        var row = index / 2;
        return (
            column == 0 ? 0 : columnWidth + gapX,
            row * (rowHeight + gapY),
            categoryCount == 1 ? totalWidth : columnWidth,
            rowHeight);
    }

    private static bool AreEqual(double left, double right)
        => Math.Abs(left - right) < 0.0001d;

    private static string FormatDouble(double value)
        => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
