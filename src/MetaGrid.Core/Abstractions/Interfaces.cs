using MetaGrid.Core.Models;

namespace MetaGrid.Core.Abstractions;

public interface ISettingsService
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}

public interface IHistoryService
{
    Task<IReadOnlyList<UpdateHistoryEntry>> LoadAsync(CancellationToken cancellationToken);
    Task AppendAsync(IEnumerable<UpdateHistoryEntry> entries, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}

public interface ILoggingService
{
    Task LogAsync(LogLevelKind level, string message, object? data = null, CancellationToken cancellationToken = default);
    string GetLogsDirectory();
}

public interface ISteamLocatorService
{
    Task<IReadOnlyList<SteamInstallation>> LocateAsync(AppSettings settings, CancellationToken cancellationToken);
}

public interface ISteamAccountService
{
    Task<IReadOnlyList<SteamAccount>> DetectAccountsAsync(AppSettings settings, CancellationToken cancellationToken);
}

public interface IHeroCatalogService
{
    Task<IReadOnlyDictionary<string, HeroDefinition>> LoadByNameAsync(CancellationToken cancellationToken);
}

public interface IHeroGridProvider
{
    Task<HeroGridSnapshot> FetchAsync(HeroGridPreset preset, CancellationToken cancellationToken);
}

public interface ID2ptOfficialGridRetrievalService
{
    Task<D2ptOfficialGridRetrievalResult> RetrieveOfficialHighWinrateAsync(bool forceRefresh, CancellationToken cancellationToken);
}

public interface IDotaGridService
{
    Task<DotaHeroGridFile> ReadAsync(string configPath, CancellationToken cancellationToken);
    Task<string?> ReadInstalledMetaGridHashAsync(string configPath, CancellationToken cancellationToken);
    string? ReadInstalledMetaGridHash(DotaHeroGridFile file);
    DotaHeroGridFile ApplySnapshot(DotaHeroGridFile existing, HeroGridSnapshot snapshot);
    string NormalizeSnapshot(HeroGridSnapshot snapshot);
    CanonicalHeroGridSnapshot CanonicalizeSnapshot(HeroGridSnapshot snapshot);
    CanonicalHeroGridSnapshot? ExtractManagedGrid(DotaHeroGridFile file);
    string NormalizeCanonical(CanonicalHeroGridSnapshot snapshot);
    string ComputeSnapshotHash(HeroGridSnapshot snapshot);
    string ComputeCanonicalHash(CanonicalHeroGridSnapshot snapshot);
    string SummarizeDifference(CanonicalHeroGridSnapshot expected, CanonicalHeroGridSnapshot actual);
    string Serialize(DotaHeroGridFile file);
    bool TryValidate(string json, out string error);
}

public interface IBackupService
{
    Task<BackupEntry?> BackupAsync(string sourceFilePath, int retentionCount, BackupMetadata? metadata, CancellationToken cancellationToken);
    Task<IReadOnlyList<BackupEntry>> GetBackupsAsync(CancellationToken cancellationToken, string? accountId = null);
    Task<bool> RestoreLatestAsync(string destinationFilePath, CancellationToken cancellationToken);
    Task<bool> RestoreAsync(BackupEntry backup, string destinationFilePath, CancellationToken cancellationToken);
    string GetBackupsDirectory();
}

public interface IGridComparisonService
{
    int CountChangedHeroes(HeroGridSnapshot current, HeroGridSnapshot previous);
}

public interface IUpdateService
{
    Task<UpdateRunResult> CheckForUpdatesAsync(IReadOnlyList<SteamAccount> accounts, AppSettings settings, bool forceWrite, CancellationToken cancellationToken);
    Task<UpdateRunResult> InstallGridAsync(IReadOnlyList<SteamAccount> accounts, AppSettings settings, InstallGridSnapshot installSnapshot, CancellationToken cancellationToken);
    Task<UpdateRunResult> RunAutomaticUpdateCycleAsync(IReadOnlyList<SteamAccount> accounts, AppSettings settings, CancellationToken cancellationToken);
}

public interface INotificationService
{
    void ShowInfo(string title, string message);
    void ShowSuccess(string title, string message);
    void ShowError(string title, string message);
}

public interface IStartupService
{
    void ApplyLaunchAtStartup(bool enabled);
}

public interface IAppPaths
{
    string RootDirectory { get; }
    string SettingsFilePath { get; }
    string HistoryFilePath { get; }
    string LogsDirectory { get; }
    string BackupsDirectory { get; }
    string CacheDirectory { get; }
    string CachedGridSnapshotPath { get; }
    string CachedGridPayloadPath { get; }
    string D2ptCacheDirectory { get; }
    string D2ptTempDirectory { get; }
    string D2ptWebView2ProfileDirectory { get; }
}

public interface IAppClock
{
    DateTimeOffset Now { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
