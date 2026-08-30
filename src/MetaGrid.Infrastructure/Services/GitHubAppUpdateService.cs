using System.Net;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class GitHubAppUpdateService(
    IGitHubReleaseClient releaseClient,
    IAppUpdateDownloader downloader,
    IUpdaterLauncher updaterLauncher,
    IAppRuntimeInfo runtimeInfo,
    IAppClock appClock,
    ILoggingService loggingService) : IAppUpdateService
{
    public async Task<AppUpdateInfo> CheckForUpdatesAsync(AppSettings settings, bool manual, IProgress<AppUpdateProgress>? progress, CancellationToken cancellationToken)
    {
        var currentVersion = runtimeInfo.GetCurrentVersion();
        progress?.Report(new AppUpdateProgress(AppUpdateProgressPhase.Checking, "Checking GitHub Releases for MetaGrid updates."));

        try
        {
            var releases = await releaseClient.GetReleasesAsync(cancellationToken);
            var latest = SelectLatestStableRelease(releases, out var packageAsset, out var shaAsset);
            var checkedAt = appClock.Now;

            if (latest is null || packageAsset is null || shaAsset is null)
            {
                return new AppUpdateInfo(currentVersion, null, AppUpdateCheckState.InvalidRelease, "No valid stable MetaGrid release package was found.", checkedAt, false, false);
            }

            var compare = AppVersionParser.Compare(latest.TagName, currentVersion);
            if (compare <= 0)
            {
                return new AppUpdateInfo(currentVersion, NormalizeVersion(latest.TagName), AppUpdateCheckState.LatestInstalled, "You already have the latest MetaGrid release installed.", checkedAt, false, false, latest, packageAsset, shaAsset);
            }

            var availableVersion = NormalizeVersion(latest.TagName);
            var isDeferred = !manual
                && string.Equals(settings.DeferredAppUpdateVersion, availableVersion, StringComparison.OrdinalIgnoreCase)
                && settings.DeferredAppUpdateUntil is { } deferredUntil
                && deferredUntil > checkedAt;

            return new AppUpdateInfo(
                currentVersion,
                availableVersion,
                isDeferred ? AppUpdateCheckState.Deferred : AppUpdateCheckState.UpdateAvailable,
                isDeferred
                    ? $"MetaGrid {availableVersion} is available, but you chose to be reminded later."
                    : $"MetaGrid {availableVersion} is available to install.",
                checkedAt,
                !isDeferred,
                isDeferred,
                latest,
                packageAsset,
                shaAsset);
        }
        catch (GitHubReleaseClientException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            await loggingService.LogAsync(LogLevelKind.Warning, "GitHub Releases rate-limited MetaGrid.", new { ex.StatusCode, ex.Body }, cancellationToken);
            return new AppUpdateInfo(currentVersion, null, AppUpdateCheckState.RateLimited, "GitHub temporarily rate-limited MetaGrid update checks.", appClock.Now, false, false);
        }
        catch (GitHubReleaseClientException ex)
        {
            await loggingService.LogAsync(LogLevelKind.Warning, "GitHub Releases request failed for MetaGrid.", new { ex.StatusCode, ex.Body }, cancellationToken);
            return new AppUpdateInfo(currentVersion, null, AppUpdateCheckState.Unavailable, $"GitHub Releases request failed with {(int)ex.StatusCode}.", appClock.Now, false, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await loggingService.LogAsync(LogLevelKind.Error, "MetaGrid app update check failed.", new { ex.Message }, cancellationToken);
            return new AppUpdateInfo(currentVersion, null, AppUpdateCheckState.Failed, ex.Message, appClock.Now, false, false);
        }
    }

    public async Task<AppUpdateLaunchResult> PrepareAndLaunchUpdateAsync(AppSettings settings, AppUpdateInfo updateInfo, IProgress<AppUpdateProgress>? progress, CancellationToken cancellationToken)
    {
        if (!updateInfo.IsUpdateAvailable || updateInfo.Release is null || updateInfo.PackageAsset is null || updateInfo.Sha256Asset is null)
        {
            return new AppUpdateLaunchResult(AppUpdateInstallState.None, "No MetaGrid app update is ready to install.", false, updateInfo.AvailableVersion);
        }

        if (!runtimeInfo.CanInstallUpdate(out var reason))
        {
            return new AppUpdateLaunchResult(AppUpdateInstallState.NotWritable, reason, false, updateInfo.AvailableVersion);
        }

        try
        {
            var prepared = await downloader.DownloadAndPrepareAsync(updateInfo, cancellationToken, progress);
            var installDirectory = runtimeInfo.GetInstallDirectory();
            prepared = prepared with
            {
                InstallDirectory = installDirectory,
                RelaunchExecutableName = runtimeInfo.GetExecutableName(),
                ParentProcessId = runtimeInfo.GetProcessId()
            };

            progress?.Report(new AppUpdateProgress(AppUpdateProgressPhase.LaunchingUpdater, $"Launching MetaGrid {prepared.ReleaseVersion} updater."));
            return await updaterLauncher.LaunchAsync(prepared, runtimeInfo.GetCurrentVersion(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await loggingService.LogAsync(LogLevelKind.Error, "MetaGrid failed to prepare the application update.", new
            {
                updateInfo.AvailableVersion,
                ex.Message
            }, cancellationToken);
            return new AppUpdateLaunchResult(AppUpdateInstallState.Failed, ex.Message, false, updateInfo.AvailableVersion);
        }
    }

    private static AppReleaseMetadata? SelectLatestStableRelease(IReadOnlyList<AppReleaseMetadata> releases, out AppReleaseAsset? packageAsset, out AppReleaseAsset? shaAsset)
    {
        packageAsset = null;
        shaAsset = null;

        foreach (var candidate in releases
                     .Where(release => !release.Draft && !release.Prerelease)
                     .Select(release => new
                     {
                         Release = release,
                         Parsed = AppVersionParser.TryParse(release.TagName, out var parsedVersion, out _)
                             ? parsedVersion
                             : null
                     })
                     .Where(x => x.Parsed is not null)
                     .OrderByDescending(x => x.Parsed))
        {
            var release = candidate.Release;
            packageAsset = release.Assets.FirstOrDefault(asset => asset.Name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase));
            shaAsset = release.Assets.FirstOrDefault(asset => asset.Name.EndsWith("-win-x64.zip.sha256", StringComparison.OrdinalIgnoreCase));
            if (packageAsset is not null && shaAsset is not null)
            {
                return release;
            }
        }

        return null;
    }

    private static string NormalizeVersion(string rawVersion)
        => AppVersionParser.TryParse(rawVersion, out _, out var normalized)
            ? normalized
            : rawVersion.Trim();
}
