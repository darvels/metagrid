using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class PersonalizedGridComposer(IDotaGridService dotaGridService) : IPersonalizedGridComposer
{
    private const string PersonalRowName = "MY BEST HEROES";
    private const string AllHeroesCategoryName = "All Heroes";
    private const double FallbackGap = 20d;

    public HeroGridSnapshot Compose(HeroGridSnapshot baseSnapshot, PersonalizationResolution personalization)
    {
        if (personalization.Selection is null || personalization.Selection.SelectedHeroes.Count == 0)
        {
            return baseSnapshot;
        }

        var targetLayoutIndex = baseSnapshot.Layouts
            .Select((layout, index) => (layout, index))
            .FirstOrDefault(x => x.layout.Name.Contains("All Roles", StringComparison.OrdinalIgnoreCase)).index;

        if (targetLayoutIndex < 0 || targetLayoutIndex >= baseSnapshot.Layouts.Count)
        {
            return baseSnapshot;
        }

        var targetLayout = baseSnapshot.Layouts[targetLayoutIndex];
        var leftColumnCategories = targetLayout.Categories
            .OrderBy(category => category.YPosition)
            .ThenBy(category => category.XPosition)
            .Where(category => category.XPosition == targetLayout.Categories.Min(item => item.XPosition))
            .ToArray();

        if (leftColumnCategories.Length == 0)
        {
            return baseSnapshot;
        }

        var topCategory = leftColumnCategories[0];
        var gap = DetermineVerticalGap(leftColumnCategories, topCategory.Height);
        var shift = topCategory.Height + gap;
        var personalizedTopY = topCategory.YPosition;

        var personalCategory = new HeroGridCategory
        {
            Name = PersonalRowName,
            HeroIds = personalization.Selection.SelectedHeroes.Select(hero => hero.HeroId).ToArray(),
            XPosition = topCategory.XPosition,
            YPosition = topCategory.YPosition,
            Width = topCategory.Width,
            Height = topCategory.Height
        };

        var shiftedCategories = targetLayout.Categories
            .Select(category => CreateAdjustedCategory(category, topCategory.XPosition, personalizedTopY, shift))
            .ToList();
        shiftedCategories.Insert(0, personalCategory);

        var effectiveLayouts = baseSnapshot.Layouts
            .Select((layout, index) => index == targetLayoutIndex
                ? new HeroGridLayout
                {
                    Name = layout.Name,
                    Categories = shiftedCategories
                }
                : new HeroGridLayout
                {
                    Name = layout.Name,
                    Categories = layout.Categories
                        .Select(category => new HeroGridCategory
                        {
                            Name = category.Name,
                            HeroIds = category.HeroIds.ToArray(),
                            XPosition = category.XPosition,
                            YPosition = category.YPosition,
                            Width = category.Width,
                            Height = category.Height
                        })
                        .ToArray()
                })
            .ToArray();

        var draft = new HeroGridSnapshot
        {
            SourceName = baseSnapshot.SourceName,
            ProviderName = baseSnapshot.ProviderName,
            SourceStrategy = baseSnapshot.SourceStrategy,
            Preset = baseSnapshot.Preset,
            PatchLabel = baseSnapshot.PatchLabel,
            CapturedAt = baseSnapshot.CapturedAt,
            Hash = string.Empty,
            Layouts = effectiveLayouts,
            RankBracket = baseSnapshot.RankBracket,
            PeriodStart = baseSnapshot.PeriodStart,
            PeriodEnd = baseSnapshot.PeriodEnd,
            MinimumSampleMatches = baseSnapshot.MinimumSampleMatches,
            UsesDerivedPositions = baseSnapshot.UsesDerivedPositions,
            PositionDefinition = baseSnapshot.PositionDefinition,
            RoleResults = baseSnapshot.RoleResults,
            ProviderStatus = baseSnapshot.ProviderStatus,
            OriginKind = baseSnapshot.OriginKind,
            SourceDetails = BuildSourceDetails(baseSnapshot.SourceDetails, personalization),
            RawSha256 = baseSnapshot.RawSha256,
            OriginalFilename = baseSnapshot.OriginalFilename,
            ConfigCount = baseSnapshot.ConfigCount,
            PayloadSizeBytes = baseSnapshot.PayloadSizeBytes,
            RuntimeVersion = baseSnapshot.RuntimeVersion,
            FinalUrl = baseSnapshot.FinalUrl,
            Title = baseSnapshot.Title,
            RawPayloadBytes = baseSnapshot.RawPayloadBytes,
            ParsedGrid = baseSnapshot.ParsedGrid
        };

        var effectiveHash = dotaGridService.ComputeSnapshotHash(draft);
        return new HeroGridSnapshot
        {
            SourceName = draft.SourceName,
            ProviderName = draft.ProviderName,
            SourceStrategy = draft.SourceStrategy,
            Preset = draft.Preset,
            PatchLabel = draft.PatchLabel,
            CapturedAt = draft.CapturedAt,
            Hash = effectiveHash,
            Layouts = draft.Layouts,
            RankBracket = draft.RankBracket,
            PeriodStart = draft.PeriodStart,
            PeriodEnd = draft.PeriodEnd,
            MinimumSampleMatches = draft.MinimumSampleMatches,
            UsesDerivedPositions = draft.UsesDerivedPositions,
            PositionDefinition = draft.PositionDefinition,
            RoleResults = draft.RoleResults,
            ProviderStatus = draft.ProviderStatus,
            OriginKind = draft.OriginKind,
            SourceDetails = draft.SourceDetails,
            RawSha256 = draft.RawSha256,
            OriginalFilename = draft.OriginalFilename,
            ConfigCount = draft.ConfigCount,
            PayloadSizeBytes = draft.PayloadSizeBytes,
            RuntimeVersion = draft.RuntimeVersion,
            FinalUrl = draft.FinalUrl,
            Title = draft.Title,
            RawPayloadBytes = draft.RawPayloadBytes,
            ParsedGrid = draft.ParsedGrid
        };
    }

    private static double DetermineVerticalGap(IReadOnlyList<HeroGridCategory> leftColumnCategories, double height)
    {
        for (var index = 1; index < leftColumnCategories.Count; index++)
        {
            var delta = leftColumnCategories[index].YPosition - leftColumnCategories[index - 1].YPosition;
            if (delta > height)
            {
                return delta - height;
            }
        }

        return FallbackGap;
    }

    private static HeroGridCategory CreateAdjustedCategory(HeroGridCategory category, double leftColumnX, double personalizedTopY, double shift)
    {
        var adjustedY = category.YPosition;
        if (Math.Abs(category.XPosition - leftColumnX) < 0.001d)
        {
            adjustedY += shift;
        }
        else if (string.Equals(category.Name, AllHeroesCategoryName, StringComparison.OrdinalIgnoreCase))
        {
            adjustedY = personalizedTopY;
        }

        return new HeroGridCategory
        {
            Name = category.Name,
            HeroIds = category.HeroIds.ToArray(),
            XPosition = category.XPosition,
            YPosition = adjustedY,
            Width = category.Width,
            Height = category.Height
        };
    }

    private static string BuildSourceDetails(string? baseDetails, PersonalizationResolution personalization)
    {
        var suffix = personalization.UsedCache
            ? "Personalized with cached OpenDota MY BEST HEROES."
            : "Personalized with current OpenDota MY BEST HEROES.";

        return string.IsNullOrWhiteSpace(baseDetails)
            ? suffix
            : $"{baseDetails} {suffix}";
    }
}
