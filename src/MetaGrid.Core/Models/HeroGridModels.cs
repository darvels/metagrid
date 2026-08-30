using System.Text.Json.Serialization;
using System.Text.Json;

namespace MetaGrid.Core.Models;

public sealed class HeroDefinition
{
    public int Id { get; init; }
    public required string LocalizedName { get; init; }
    public required string InternalName { get; init; }
    public required string Slug { get; init; }
}

public sealed class HeroGridCategory
{
    public required string Name { get; init; }
    public required IReadOnlyList<int> HeroIds { get; init; }
    public double XPosition { get; init; }
    public double YPosition { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

public sealed class HeroGridLayout
{
    public required string Name { get; init; }
    public required IReadOnlyList<HeroGridCategory> Categories { get; init; }
}

public sealed class HeroGridSnapshot
{
    public required string SourceName { get; init; }
    public string ProviderName { get; init; } = string.Empty;
    public string SourceStrategy { get; init; } = string.Empty;
    public required HeroGridPreset Preset { get; init; }
    public required string PatchLabel { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required string Hash { get; init; }
    public required IReadOnlyList<HeroGridLayout> Layouts { get; init; }
    public string RankBracket { get; init; } = string.Empty;
    public DateTimeOffset? PeriodStart { get; init; }
    public DateTimeOffset? PeriodEnd { get; init; }
    public int MinimumSampleMatches { get; init; }
    public bool UsesDerivedPositions { get; init; }
    public string? PositionDefinition { get; init; }
    public IReadOnlyList<RoleGridResult> RoleResults { get; init; } = [];
    public ProviderStatus ProviderStatus { get; init; } = ProviderStatus.Online;
    public GridOriginKind OriginKind { get; init; } = GridOriginKind.NativeD2pt;
    public string? SourceDetails { get; init; }
    public string? RawSha256 { get; init; }
    public string? OriginalFilename { get; init; }
    public int? ConfigCount { get; init; }
    public long? PayloadSizeBytes { get; init; }
    public string? RuntimeVersion { get; init; }
    public string? FinalUrl { get; init; }
    public string? Title { get; init; }
    public byte[]? RawPayloadBytes { get; init; }
    public DotaHeroGridFile? ParsedGrid { get; init; }
}

public sealed class RoleGridResult
{
    public required int Position { get; init; }
    public required string RoleName { get; init; }
    public required IReadOnlyList<HeroRoleStat> Heroes { get; init; }
}

public sealed class HeroRoleStat
{
    public required int HeroId { get; init; }
    public required string HeroName { get; init; }
    public required int Position { get; init; }
    public required string RoleName { get; init; }
    public required int Wins { get; init; }
    public required int Matches { get; init; }
    public required double WinRate { get; init; }
    public required string RankBracket { get; init; }
    public required DateTimeOffset PeriodStart { get; init; }
    public required DateTimeOffset PeriodEnd { get; init; }
    public required string Provider { get; init; }
    public double? PickRate { get; init; }
    public DateTimeOffset? SourceTimestamp { get; init; }
    public string? ProviderHeroIdentifier { get; init; }
}

// Canonical semantic representation for MetaGrid-managed layouts.
// It preserves only fields that affect the effective installed grid meaning:
// layout order, layout names, category order, category names, and ordered hero IDs.
// Dota-specific coordinates are intentionally excluded because MetaGrid derives them
// deterministically from this semantic structure during installation.
public sealed class CanonicalHeroGridSnapshot
{
    public int Version { get; init; }
    public required IReadOnlyList<CanonicalHeroGridLayout> Layouts { get; init; }
}

public sealed class CanonicalHeroGridLayout
{
    public required string Name { get; init; }
    public required IReadOnlyList<CanonicalHeroGridCategory> Categories { get; init; }
}

public sealed class CanonicalHeroGridCategory
{
    public required string Name { get; init; }
    public required IReadOnlyList<int> HeroIds { get; init; }
    public double XPosition { get; init; }
    public double YPosition { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

public sealed class InstalledMetaGridInspectionResult
{
    public InstalledMetaGridState State { get; init; } = InstalledMetaGridState.Unknown;
    public string? InstalledHash { get; init; }
    public string? Error { get; init; }
    public int ManagedLayoutCount { get; init; }
    public int ManagedCategoryCount { get; init; }
    public bool HasAllRoleLayout { get; init; }
    public bool HasRoleSpecificLayouts { get; init; }
}

public sealed class NativeGridStructureValidationResult
{
    public bool IsValid { get; init; }
    public string? Error { get; init; }
}

public sealed class InstallGridSnapshot
{
    public required HeroGridSnapshot Snapshot { get; init; }
    public required string AccountId { get; init; }
    public required string AccountDisplayName { get; init; }
    public required string TargetPath { get; init; }
    public required DateTimeOffset PreparedAt { get; init; }
    public string? BaseSourceHash { get; init; }
    public string? EffectiveGridHash { get; init; }
    public string? PersonalizationStatus { get; init; }
    public string? PersonalizationMessage { get; init; }
    public string? PersonalizationAccountId { get; init; }
    public bool PersonalizationUsedCache { get; init; }
    public int HeroCount { get; init; }
    public int GroupCount { get; init; }
    public bool HasExistingGridFile { get; init; }
}

public sealed class D2ptOfficialGridRetrievalResult
{
    public required bool Succeeded { get; init; }
    public ProviderStatus ProviderStatus { get; init; } = ProviderStatus.Unknown;
    public string Classification { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? RawJson { get; init; }
    public string? SuggestedFilename { get; init; }
    public string? MimeType { get; init; }
    public string? RuntimeVersion { get; init; }
    public string? FinalUrl { get; init; }
    public string? Title { get; init; }
    public bool PageLoaded { get; init; }
    public bool CloudflareChallenge { get; init; }
    public bool HighWinrateButtonFound { get; init; }
    public bool OfficialDownloadTriggered { get; init; }
    public bool DownloadCompleted { get; init; }
    public long InitMs { get; init; }
    public long NavigationMs { get; init; }
    public long DownloadMs { get; init; }
    public long ElapsedMs { get; init; }
    public string? TemporaryFilePath { get; init; }
    public static D2ptOfficialGridRetrievalResult Failure(ProviderStatus providerStatus, string classification, string message)
        => new()
        {
            Succeeded = false,
            ProviderStatus = providerStatus,
            Classification = classification,
            Message = message
        };
}

public sealed class DotaHeroGridFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 3;

    [JsonPropertyName("configs")]
    public List<DotaHeroGridLayout> Configs { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class DotaHeroGridLayout
{
    [JsonPropertyName("config_name")]
    public required string ConfigName { get; set; }

    [JsonPropertyName("categories")]
    public List<DotaHeroGridCategory> Categories { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class DotaHeroGridCategory
{
    [JsonPropertyName("category_name")]
    public required string CategoryName { get; set; }

    [JsonPropertyName("x_position")]
    public double XPosition { get; set; }

    [JsonPropertyName("y_position")]
    public double YPosition { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonPropertyName("hero_ids")]
    public List<int> HeroIds { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
