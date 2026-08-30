namespace MetaGrid.Core.Models;

public sealed class UpdateHistoryEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required UpdateStatus Status { get; init; }
    public required string GridHash { get; init; }
    public required string Source { get; init; }
    public string? SourceStrategy { get; init; }
    public required string AccountId { get; init; }
    public string? AccountDisplayName { get; init; }
    public string Operation { get; init; } = "Update check";
    public required string Message { get; init; }
    public string? PreviousHash { get; init; }
    public string? NewHash { get; init; }
    public string? AvailableHash { get; init; }
    public string? InstalledHash { get; init; }
    public string? BaseSourceHash { get; init; }
    public string? EffectiveGridHash { get; init; }
    public string? PersonalizationStatus { get; init; }
    public string? PersonalizationMessage { get; init; }
    public string? PersonalizationAccountId { get; init; }
    public bool PersonalizationUsedCache { get; init; }
    public string? BackupFilePath { get; init; }
    public string? BackupOperationId { get; init; }
    public string? BackupHash { get; init; }
    public string? TargetPath { get; init; }
    public int ChangedHeroCount { get; init; }
    public UpdateTriggerKind Trigger { get; init; } = UpdateTriggerKind.ManualCheck;
}

public sealed class UpdateRunResult
{
    public required UpdateStatus Status { get; init; }
    public required string Message { get; init; }
    public required string GridHash { get; init; }
    public int ChangedHeroCount { get; init; }
    public ProviderStatus ProviderStatus { get; init; }
    public GridOriginKind? OriginKind { get; init; }
    public string? SourceName { get; init; }
    public string? SourceStrategy { get; init; }
    public DateTimeOffset? RetrievedAt { get; init; }
    public string? ProviderMessage { get; init; }
    public string? AvailableHash { get; init; }
    public string? InstalledHash { get; init; }
    public string? BaseSourceHash { get; init; }
    public string? EffectiveGridHash { get; init; }
    public string? PersonalizationStatus { get; init; }
    public string? PersonalizationMessage { get; init; }
    public string? PersonalizationAccountId { get; init; }
    public string? PersonalizationAccountDisplayName { get; init; }
    public bool PersonalizationUsedCache { get; init; }
    public int PersonalHeroCount { get; init; }
    public string? OperationId { get; init; }
    public string? TargetPath { get; init; }
    public BackupEntry? BackupEntry { get; init; }
    public InstallGridSnapshot? ConfirmedInstallSnapshot { get; init; }
    public IReadOnlyList<UpdateHistoryEntry> HistoryEntries { get; init; } = [];
    public UpdateTriggerKind Trigger { get; init; } = UpdateTriggerKind.ManualCheck;
}
