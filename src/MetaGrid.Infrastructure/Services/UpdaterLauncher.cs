using System.Diagnostics;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class UpdaterLauncher(IAppRuntimeInfo runtimeInfo) : IUpdaterLauncher
{
    public Task<AppUpdateLaunchResult> LaunchAsync(PreparedAppUpdatePackage package, string currentVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var installDirectory = runtimeInfo.GetInstallDirectory();
        if (!Directory.Exists(installDirectory))
        {
            return Task.FromResult(new AppUpdateLaunchResult(
                AppUpdateInstallState.UnsafeInstallPath,
                $"MetaGrid install directory was not found: {installDirectory}",
                false,
                package.ReleaseVersion));
        }

        if (!File.Exists(package.UpdaterExecutablePath))
        {
            return Task.FromResult(new AppUpdateLaunchResult(
                AppUpdateInstallState.MissingUpdater,
                "MetaGrid updater helper is missing from the prepared update session.",
                false,
                package.ReleaseVersion));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = package.UpdaterExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(package.UpdaterExecutablePath)!,
            Arguments = BuildArguments(
                installDirectory,
                package.PackageRootDirectory,
                package.RelaunchExecutableName,
                package.BackupDirectory,
                currentVersion,
                package.ReleaseVersion,
                package.ParentProcessId,
                package.UpdaterLogPath)
        };

        var process = Process.Start(startInfo);

        return Task.FromResult(new AppUpdateLaunchResult(
            AppUpdateInstallState.Started,
            $"MetaGrid {package.ReleaseVersion} updater started.",
            true,
            package.ReleaseVersion,
            process?.Id));
    }

    private static string BuildArguments(string installDirectory, string packageRootDirectory, string relaunchExecutableName, string backupDirectory, string currentVersion, string targetVersion, int parentProcessId, string? updaterLogPath)
        => string.Join(" ",
            $"--parent-pid {parentProcessId}",
            $"--install-dir \"{installDirectory}\"",
            $"--package-dir \"{packageRootDirectory}\"",
            $"--exe-name \"{relaunchExecutableName}\"",
            $"--backup-dir \"{backupDirectory}\"",
            $"--current-version \"{currentVersion}\"",
            $"--target-version \"{targetVersion}\"",
            string.IsNullOrWhiteSpace(updaterLogPath) ? string.Empty : $"--log-path \"{updaterLogPath}\"");
}
