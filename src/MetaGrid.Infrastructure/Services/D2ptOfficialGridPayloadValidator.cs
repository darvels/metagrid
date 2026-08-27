using System.Text.Json;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Utilities;

namespace MetaGrid.Infrastructure.Services;

public static class D2ptOfficialGridPayloadValidator
{
    private static readonly string[] ExpectedLayoutMarkers =
    [
        "All Roles",
        "Carry",
        "Mid",
        "Offlane",
        "Support",
        "Hard Support"
    ];

    public static D2ptOfficialGridValidationResult Validate(string rawJson, string? suggestedFilename = null)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return D2ptOfficialGridValidationResult.Failure("DownloadedPayloadInvalid", "The official D2PT download was empty.");
        }

        if (rawJson.Length < 128 || rawJson.Length > 2_000_000)
        {
            return D2ptOfficialGridValidationResult.Failure("DownloadedPayloadInvalid", $"The official D2PT download size was outside the expected range: {rawJson.Length} characters.");
        }

        if (rawJson.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
            rawJson.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
            rawJson.Contains("Enable JavaScript and cookies to continue", StringComparison.OrdinalIgnoreCase))
        {
            return D2ptOfficialGridValidationResult.Failure("DownloadedPayloadInvalid", "The downloaded content was HTML or a Cloudflare challenge page instead of the official grid JSON.");
        }

        if (!string.IsNullOrWhiteSpace(suggestedFilename) &&
            !suggestedFilename.Contains("high_winrate", StringComparison.OrdinalIgnoreCase))
        {
            return D2ptOfficialGridValidationResult.Failure("UnexpectedFilename", $"The official download filename did not look like the High Winrate grid: {suggestedFilename}");
        }

        DotaHeroGridFile? file;
        try
        {
            file = JsonSerializer.Deserialize<DotaHeroGridFile>(rawJson, JsonDefaults.StrictIndented);
        }
        catch (JsonException ex)
        {
            return D2ptOfficialGridValidationResult.Failure("DownloadedPayloadInvalid", $"The official D2PT payload was not valid JSON: {ex.Message}");
        }

        if (file?.Configs is null)
        {
            return D2ptOfficialGridValidationResult.Failure("GridSchemaInvalid", "The official D2PT payload did not contain a configs collection.");
        }

        if (file.Version != 3)
        {
            return D2ptOfficialGridValidationResult.Failure("GridSchemaInvalid", $"The official D2PT payload used unsupported version {file.Version}. Expected version 3.");
        }

        if (file.Configs.Count < 6 || file.Configs.Count > 12)
        {
            return D2ptOfficialGridValidationResult.Failure("GridSchemaInvalid", $"The official D2PT payload contained an implausible number of configs: {file.Configs.Count}.");
        }

        var missingLayouts = ExpectedLayoutMarkers
            .Where(marker => !file.Configs.Any(config => config.ConfigName.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (missingLayouts.Length > 0)
        {
            return D2ptOfficialGridValidationResult.Failure("GridSchemaInvalid", $"The official D2PT payload was missing expected layouts: {string.Join(", ", missingLayouts)}.");
        }

        var patchLabel = InferPatchLabel(file);
        var snapshot = ToSnapshot(file, patchLabel, rawJson.Length, suggestedFilename);
        var semanticHash = new DotaGridService().ComputeSnapshotHash(snapshot);
        var rawSha256 = HashUtilities.Sha256(rawJson);
        var heroCount = snapshot.Layouts
            .SelectMany(layout => layout.Categories)
            .SelectMany(category => category.HeroIds)
            .Distinct()
            .Count();

        if (heroCount < 40)
        {
            return D2ptOfficialGridValidationResult.Failure("GridSchemaInvalid", $"The official D2PT payload hero count was implausibly low: {heroCount}.");
        }

        foreach (var layout in file.Configs)
        {
            if (string.IsNullOrWhiteSpace(layout.ConfigName) || layout.Categories.Count == 0)
            {
                return D2ptOfficialGridValidationResult.Failure("GridSchemaInvalid", $"The layout '{layout.ConfigName}' did not contain any categories.");
            }

            foreach (var category in layout.Categories)
            {
                if (string.IsNullOrWhiteSpace(category.CategoryName))
                {
                    return D2ptOfficialGridValidationResult.Failure("GridSchemaInvalid", $"A layout in '{layout.ConfigName}' contained a blank category name.");
                }

                if (category.HeroIds.Count == 0)
                {
                    return D2ptOfficialGridValidationResult.Failure("GridSchemaInvalid", $"The category '{category.CategoryName}' in '{layout.ConfigName}' did not contain any hero IDs.");
                }

                if (category.HeroIds.Any(heroId => heroId <= 0))
                {
                    return D2ptOfficialGridValidationResult.Failure("GridSchemaInvalid", $"The category '{category.CategoryName}' in '{layout.ConfigName}' contained invalid hero IDs.");
                }
            }
        }

        return D2ptOfficialGridValidationResult.Success(file, snapshot, rawSha256, semanticHash, patchLabel, heroCount);
    }

    public static string InferPatchLabel(DotaHeroGridFile file)
    {
        var name = file.Configs.FirstOrDefault()?.ConfigName ?? string.Empty;
        var match = System.Text.RegularExpressions.Regex.Match(name, @"\b\d+\.\d+[a-z]?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Value : "Unknown Patch";
    }

    public static HeroGridSnapshot ToSnapshot(DotaHeroGridFile file, string patchLabel, int rawLength, string? suggestedFilename)
        => new()
        {
            SourceName = "Dota2ProTracker",
            ProviderName = "Dota2ProTracker",
            SourceStrategy = "OfficialDownloadPayload",
            Preset = HeroGridPreset.HighWinrate,
            PatchLabel = patchLabel,
            CapturedAt = DateTimeOffset.UtcNow,
            Hash = string.Empty,
            Layouts = file.Configs
                .Select(config => new HeroGridLayout
                {
                    Name = config.ConfigName.Trim(),
                    Categories = config.Categories
                        .Select(category => new HeroGridCategory
                        {
                            Name = category.CategoryName.Trim(),
                            HeroIds = category.HeroIds.Distinct().ToArray(),
                            XPosition = category.XPosition,
                            YPosition = category.YPosition,
                            Width = category.Width,
                            Height = category.Height
                        })
                        .ToArray()
                })
                .ToArray(),
            ProviderStatus = ProviderStatus.Online,
            OriginKind = GridOriginKind.NativeD2pt,
            SourceDetails = "Official D2PT High Winrate download payload",
            OriginalFilename = suggestedFilename,
            ConfigCount = file.Configs.Count,
            PayloadSizeBytes = rawLength,
            ParsedGrid = file
        };
}

public sealed record D2ptOfficialGridValidationResult(
    bool IsValid,
    string Classification,
    string Message,
    DotaHeroGridFile? GridFile,
    HeroGridSnapshot? Snapshot,
    string? RawSha256,
    string? SemanticHash,
    string? PatchLabel,
    int HeroCount)
{
    public static D2ptOfficialGridValidationResult Failure(string classification, string message)
        => new(false, classification, message, null, null, null, null, null, 0);

    public static D2ptOfficialGridValidationResult Success(
        DotaHeroGridFile gridFile,
        HeroGridSnapshot snapshot,
        string rawSha256,
        string semanticHash,
        string patchLabel,
        int heroCount)
        => new(true, "OfficialPayloadValid", "The official D2PT High Winrate payload validated successfully.", gridFile, snapshot, rawSha256, semanticHash, patchLabel, heroCount);
}
