using System.Text.RegularExpressions;

namespace MetaGrid.Core.Models;

public enum AppUpdateCheckState
{
    Unknown,
    Checking,
    LatestInstalled,
    UpdateAvailable,
    Deferred,
    Failed,
    Unavailable,
    InvalidRelease,
    RateLimited
}

public enum AppUpdateProgressPhase
{
    Checking,
    Downloading,
    Verifying,
    Extracting,
    LaunchingUpdater,
    Completed,
    Failed
}

public enum AppUpdateInstallState
{
    None,
    ReadyToInstall,
    Started,
    Failed,
    VerificationFailed,
    UnsafeInstallPath,
    NotWritable,
    MissingUpdater
}

public sealed record AppReleaseAsset(
    string Name,
    string DownloadUrl,
    long Size,
    string? Digest = null);

public sealed record AppReleaseMetadata(
    string TagName,
    string Name,
    bool Draft,
    bool Prerelease,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<AppReleaseAsset> Assets,
    string? HtmlUrl = null);

public sealed record AppUpdateInfo(
    string CurrentVersion,
    string? AvailableVersion,
    AppUpdateCheckState State,
    string Message,
    DateTimeOffset CheckedAt,
    bool IsUpdateAvailable,
    bool IsDeferred,
    AppReleaseMetadata? Release = null,
    AppReleaseAsset? PackageAsset = null,
    AppReleaseAsset? Sha256Asset = null);

public sealed record AppUpdateProgress(
    AppUpdateProgressPhase Phase,
    string Message,
    int? Percent = null);

public sealed record PreparedAppUpdatePackage(
    string ReleaseVersion,
    string SessionDirectory,
    string DownloadedArchivePath,
    string Sha256FilePath,
    string ExpectedSha256,
    string ExtractionDirectory,
    string PackageRootDirectory,
    string InstallDirectory,
    string RelaunchExecutableName,
    string UpdaterExecutablePath,
    string BackupDirectory,
    int ParentProcessId,
    string? UpdaterLogPath = null);

public sealed record AppUpdateLaunchResult(
    AppUpdateInstallState State,
    string Message,
    bool ShouldExitApplication,
    string? ReleaseVersion = null,
    int? UpdaterProcessId = null);

public sealed record AppUpdateInstallRequest(
    string InstallDirectory,
    string PackageRootDirectory,
    string RelaunchExecutableName,
    int ParentProcessId,
    string BackupDirectory,
    string CurrentVersion,
    string TargetVersion,
    bool SkipRelaunch = false,
    string? LogPath = null,
    string? SimulatedFailureStage = null);

public sealed record AppUpdateInstallExecutionResult(
    bool Success,
    string Message,
    string? RelaunchExecutablePath = null);

public static class AppVersionParser
{
    private static readonly Regex LeadingVRegex = new(@"^[vV](?<version>\d+\.\d+\.\d+)$", RegexOptions.Compiled);

    public static bool TryParse(string? rawVersion, out Version? version, out string normalizedVersion)
    {
        version = null;
        normalizedVersion = string.Empty;

        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return false;
        }

        var candidate = rawVersion.Trim();
        var match = LeadingVRegex.Match(candidate);
        if (match.Success)
        {
            candidate = match.Groups["version"].Value;
        }

        if (!Version.TryParse(candidate, out var parsed))
        {
            return false;
        }

        if (parsed.Build < 0)
        {
            parsed = new Version(parsed.Major, parsed.Minor, 0);
        }

        version = parsed;
        normalizedVersion = $"{parsed.Major}.{parsed.Minor}.{parsed.Build}";
        return true;
    }

    public static int Compare(string? left, string? right)
    {
        var hasLeft = TryParse(left, out var leftVersion, out _);
        var hasRight = TryParse(right, out var rightVersion, out _);

        return (hasLeft, hasRight) switch
        {
            (true, true) => leftVersion!.CompareTo(rightVersion),
            (true, false) => 1,
            (false, true) => -1,
            _ => 0
        };
    }
}
