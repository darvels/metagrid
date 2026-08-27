namespace MetaGrid.Core.Models;

public sealed class BackupEntry
{
    public required string FilePath { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? AccountId { get; init; }
    public string? AccountDisplayName { get; init; }
    public string? OriginalPath { get; init; }
    public string? PreWriteHash { get; init; }
    public string? BackupHash { get; init; }
    public string? OperationId { get; init; }
    public long FileSizeBytes { get; init; }
    public string DisplayName => string.IsNullOrWhiteSpace(AccountId)
        ? $"{CreatedAt.LocalDateTime:g}"
        : $"{CreatedAt.LocalDateTime:g} - {AccountId}";
    public string FileSizeText => FileSizeBytes switch
    {
        >= 1024 * 1024 => $"{FileSizeBytes / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{FileSizeBytes / 1024d:0.0} KB",
        _ => $"{FileSizeBytes} B"
    };
}

public sealed class BackupMetadata
{
    public string? AccountId { get; init; }
    public string? AccountDisplayName { get; init; }
    public string? OriginalPath { get; init; }
    public string? PreWriteHash { get; init; }
    public string? BackupHash { get; init; }
    public string? OperationId { get; init; }
}
