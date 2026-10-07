namespace MetaGrid.Core.Models;

public enum GuideRole
{
    Carry,
    Mid,
    Offlane,
    Support,
    HardSupport
}

public enum GuideSubscriptionStatus
{
    Disabled,
    Unsupported,
    Pending,
    Installing,
    Installed,
    UpToDate,
    UpdateAvailable,
    SteamUnavailable,
    SourceUnavailable,
    MappingFailed,
    Conflict,
    Error,
    Removing,
    RemovalPending,
    RemovalFailed,
    NoBuild,
    SourceIncomplete,
    SourcePatchIncompatible
}

public enum GuideFailureKind
{
    Other,
    SourceIncomplete,
    SourcePatchIncompatible,
    TalentMapping
}

public sealed class NormalizedGuideItemGroup
{
    public string CategoryKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> ItemIds { get; set; } = [];
}

public sealed class NormalizedGuideTalentChoice
{
    public int Level { get; set; }
    public string TalentId { get; set; } = string.Empty;
    public string SelectedLabel { get; set; } = string.Empty;
    public string? AlternativeLabel { get; set; }
}

public sealed record GuideSkillCandidate(string Ability, double PickRate);

public sealed record GuideStartingItem(string ItemId, int Quantity, string? Slot = null,
    int? Charges = null, int? SecondaryCharges = null);
public sealed record GuideStartingInventory(int MatchCount, double? WinRate, int SourceOrder,
    string SourceIdentity, IReadOnlyList<GuideStartingItem> Items);

public sealed record GuideTalentCandidate(int SourceLevel, string TalentId, string Label, double PickRate,
    double? WinRate, int? Matches, int? Wins);
public sealed record GuideTalentRowEvidence(int Level, IReadOnlyList<GuideTalentCandidate> Candidates,
    string SelectedByPickRate, string LevelEvidence);

public sealed class NormalizedHeroGuideBuild
{
    public const int CurrentStartingItemsVersion = 4;
    public int StartingItemsVersion { get; set; }
    public List<GuideStartingInventory> StartingInventories { get; set; } = [];
    public int? SelectedStartingInventoryOrder { get; set; }
    public List<GuideTalentCandidate> RawTalentCandidates { get; set; } = [];
    public List<GuideTalentRowEvidence> TalentEvidence { get; set; } = [];
    public List<string> AbsentOptionalSections { get; set; } = [];
    public List<List<GuideSkillCandidate>> SkillCandidates { get; set; } = [];
    public string SourceClassification { get; set; } = "Unavailable";
    public int SchemaVersion { get; set; } = 1;
    public int HeroId { get; set; }
    public string HeroInternalName { get; set; } = string.Empty;
    public GuideRole Role { get; set; }
    public string Source { get; set; } = string.Empty;
    public string SourceBuildId { get; set; } = string.Empty;
    public string SourceTitle { get; set; } = string.Empty;
    public string GameplayVersion { get; set; } = string.Empty;
    public double WinRate { get; set; }
    public int Matches { get; set; }
    public List<NormalizedGuideItemGroup> ItemGroups { get; set; } = [];
    public List<string> SkillOrder { get; set; } = [];
    public List<NormalizedGuideTalentChoice> TalentChoices { get; set; } = [];
    public DateTimeOffset RetrievedAtUtc { get; set; }
    public string CanonicalSourceHash { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
}

public sealed class ResolvedGuideTalentChoice
{
    public int Level { get; set; }
    public string TalentId { get; set; } = string.Empty;
    public string SelectedLabel { get; set; } = string.Empty;
}

public sealed class ResolvedHeroGuideBuild
{
    public List<string>? ExpectedStartingItems { get; init; }
    public required NormalizedHeroGuideBuild SourceBuild { get; init; }
    public string HeroToken { get; init; } = string.Empty;
    public string RoleToken { get; init; } = string.Empty;
    public string GuideTitle { get; init; } = string.Empty;
    public string Overview { get; init; } = string.Empty;
    public string OriginalCreatorIdHex { get; init; } = string.Empty;
    public IReadOnlyList<NormalizedGuideItemGroup> ItemGroups { get; init; } = [];
    public IReadOnlyList<string> SkillOrder { get; init; } = [];
    public IReadOnlyList<ResolvedGuideTalentChoice> TalentChoices { get; init; } = [];
    public string EffectiveGuideHash { get; init; } = string.Empty;
}

public sealed class DotaGuideMappingResult
{
    public GuideFailureKind FailureKind { get; init; }
    public bool Succeeded { get; init; }
    public ResolvedHeroGuideBuild? Build { get; init; }
    public string? Error { get; init; }

    public static DotaGuideMappingResult Success(ResolvedHeroGuideBuild build) => new()
    {
        Succeeded = true,
        Build = build
    };

    public static DotaGuideMappingResult Failure(string error, GuideFailureKind kind = GuideFailureKind.Other) => new()
    {
        FailureKind = kind,
        Succeeded = false,
        Error = error
    };
}

public sealed class GuideSerializationResult
{
    public required byte[] Bytes { get; init; }
    public required string Text { get; init; }
    public required string EffectiveGuideHash { get; init; }
}

public sealed class ParsedValveGuideDocument
{
    public string HeroToken { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string RoleToken { get; init; } = string.Empty;
    public string GameplayVersion { get; init; } = string.Empty;
    public string Overview { get; init; } = string.Empty;
    public int GuideRevision { get; init; }
    public string OriginalCreatorIdHex { get; init; } = string.Empty;
    public IReadOnlyList<NormalizedGuideItemGroup> ItemGroups { get; init; } = [];
    public IReadOnlyList<string> SkillOrder { get; init; } = [];
    public IReadOnlyList<ResolvedGuideTalentChoice> TalentChoices { get; init; } = [];
}

public sealed class RemoteGuideFileEntry
{
    public string Name { get; init; } = string.Empty;
    public int Size { get; init; }
    public bool Exists { get; init; }
    public bool Persisted { get; init; }
    public long Timestamp { get; init; }
}

public sealed class RemoteGuideWriteResult
{
    public bool Succeeded { get; init; }
    public string RemoteFile { get; init; } = string.Empty;
    public int BytesWritten { get; init; }
    public bool ExistsAfterWrite { get; init; }
    public bool PersistedAfterWrite { get; init; }
    public string? ReadBackHash { get; init; }
    public string? Error { get; init; }
}

public sealed class RemoteGuideRecoveryResult
{
    public bool Found { get; init; }
    public bool Ambiguous { get; init; }
    public string? RemoteFile { get; init; }
    public string? Error { get; init; }
}

public sealed class GuideSubscriptionSyncResult
{
    public int GuideRevision { get; init; }
    public GuideSubscriptionStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? CanonicalSourceHash { get; init; }
    public string? EffectiveGuideHash { get; init; }
    public string? RemoteFile { get; init; }
    public string? ProviderName { get; init; }
    public string? SourceStrategy { get; init; }
    public string? BuildTitle { get; init; }
    public string? PatchLabel { get; init; }
    public DateTimeOffset? RetrievedAtUtc { get; init; }
}

public sealed class GuideSubscriptionRecord
{
    public bool RemovalRequested { get; set; }
    public string? InstalledAccountId { get; set; }
    public bool NeedsRemoval => RemovalRequested || Status is GuideSubscriptionStatus.Removing or GuideSubscriptionStatus.RemovalPending or GuideSubscriptionStatus.RemovalFailed;
    public int HeroId { get; set; }
    public string HeroInternalName { get; set; } = string.Empty;
    public string HeroDisplayName { get; set; } = string.Empty;
    public GuideRole Role { get; set; }
    public bool IsEnabled { get; set; }
    public string? RemoteFile { get; set; }
    public string? InstalledHash { get; set; }
    public string? AvailableHash { get; set; }
    public string? ProviderName { get; set; }
    public string? SourceStrategy { get; set; }
    public string? BuildTitle { get; set; }
    public string? PatchLabel { get; set; }
    public string? CanonicalSourceHash { get; set; }
    public string? EffectiveGuideHash { get; set; }
    public int GuideRevision { get; set; }
    public string? LastError { get; set; }
    public string? StatusMessage { get; set; }
    public GuideSubscriptionStatus Status { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTimeOffset? LastRetrievedAt { get; set; }
    public DateTimeOffset? LastInstalledAt { get; set; }
    public DateTimeOffset? LastUpdatedAt { get; set; }

    public string StableKey => CreateStableKey(HeroId, Role);

    public bool ApplyVerifiedInstallation(GuideSubscriptionSyncResult result)
    {
        if (result.Status is not (GuideSubscriptionStatus.Installed or GuideSubscriptionStatus.UpToDate)) return false;
        if (string.IsNullOrWhiteSpace(result.RemoteFile) || string.IsNullOrWhiteSpace(result.EffectiveGuideHash)
            || string.IsNullOrWhiteSpace(result.CanonicalSourceHash)) return false;
        RemoteFile = result.RemoteFile;
        CanonicalSourceHash = result.CanonicalSourceHash;
        EffectiveGuideHash = InstalledHash = result.EffectiveGuideHash;
        GuideRevision = result.GuideRevision;
        LastInstalledAt ??= DateTimeOffset.UtcNow;
        if (result.Status == GuideSubscriptionStatus.Installed) LastUpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public bool ApplyVerifiedRemoval(GuideRemovalResult result)
    {
        if (!result.VerifiedAbsent) return false;
        IsEnabled = false;
        RemovalRequested = false;
        RemoteFile = InstalledHash = EffectiveGuideHash = InstalledAccountId = null;
        GuideRevision = 0;
        LastInstalledAt = LastUpdatedAt = null;
        LastError = null;
        Status = GuideSubscriptionStatus.Disabled;
        StatusMessage = "Auto Guide is disabled and its owned guide is absent.";
        return true;
    }

    public GuideSubscriptionRecord CreateCopy() => new()
    {
        RemovalRequested = RemovalRequested,
        InstalledAccountId = InstalledAccountId,
        HeroId = HeroId,
        HeroInternalName = HeroInternalName,
        HeroDisplayName = HeroDisplayName,
        Role = Role,
        IsEnabled = IsEnabled,
        RemoteFile = RemoteFile,
        InstalledHash = InstalledHash,
        AvailableHash = AvailableHash,
        ProviderName = ProviderName,
        SourceStrategy = SourceStrategy,
        BuildTitle = BuildTitle,
        PatchLabel = PatchLabel,
        CanonicalSourceHash = CanonicalSourceHash,
        EffectiveGuideHash = EffectiveGuideHash,
        GuideRevision = GuideRevision,
        LastError = LastError,
        StatusMessage = StatusMessage,
        Status = Status,
        LastCheckedAt = LastCheckedAt,
        LastRetrievedAt = LastRetrievedAt,
        LastInstalledAt = LastInstalledAt,
        LastUpdatedAt = LastUpdatedAt
    };

    public static string CreateStableKey(int heroId, GuideRole role)
        => $"{heroId}:{role}";
}

public sealed record RemoteGuideDeleteResult(bool Succeeded, bool ExistsAfterDelete, bool PersistedAfterDelete, string? Error = null);
public sealed record GuideRemovalResult(bool VerifiedAbsent, string? RemoteFile, GuideSubscriptionStatus Status, string Message);

public static class GuideSubscriptionCollection
{
    public static List<GuideSubscriptionRecord> Normalize(IEnumerable<GuideSubscriptionRecord>? records)
    {
        if (records is null)
        {
            return [];
        }

        var deduped = new Dictionary<string, GuideSubscriptionRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            if (record is null || record.HeroId <= 0)
            {
                continue;
            }

            var normalized = record.CreateCopy();
            normalized.HeroInternalName = normalized.HeroInternalName?.Trim() ?? string.Empty;
            normalized.HeroDisplayName = normalized.HeroDisplayName?.Trim() ?? string.Empty;
            normalized.RemoteFile = NormalizeOptional(normalized.RemoteFile);
            normalized.InstalledHash = NormalizeOptional(normalized.InstalledHash);
            normalized.AvailableHash = NormalizeOptional(normalized.AvailableHash);
            normalized.ProviderName = NormalizeOptional(normalized.ProviderName);
            normalized.SourceStrategy = NormalizeOptional(normalized.SourceStrategy);
            normalized.BuildTitle = NormalizeOptional(normalized.BuildTitle);
            normalized.PatchLabel = NormalizeOptional(normalized.PatchLabel);
            normalized.CanonicalSourceHash = NormalizeOptional(normalized.CanonicalSourceHash);
            normalized.EffectiveGuideHash = NormalizeOptional(normalized.EffectiveGuideHash);
            normalized.LastError = NormalizeOptional(normalized.LastError);
            normalized.StatusMessage = NormalizeOptional(normalized.StatusMessage);
            if (!normalized.IsEnabled && normalized.Status == GuideSubscriptionStatus.Disabled)
            {
                normalized.StatusMessage = normalized.StatusMessage ?? "Auto Guide is disabled.";
            }

            deduped[normalized.StableKey] = normalized;
        }

        return deduped.Values
            .OrderBy(record => record.HeroDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.HeroId)
            .ThenBy(record => record.Role)
            .ToList();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
