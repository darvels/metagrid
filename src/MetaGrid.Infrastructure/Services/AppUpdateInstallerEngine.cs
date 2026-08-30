using System.Diagnostics;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class AppUpdateInstallerEngine
{
    public async Task<AppUpdateInstallExecutionResult> ExecuteAsync(AppUpdateInstallRequest request, CancellationToken cancellationToken)
    {
        var log = new UpdaterLog(request.LogPath);
        log.Write($"Starting MetaGrid updater. Current={request.CurrentVersion}; Target={request.TargetVersion}; ParentPid={request.ParentProcessId}");
        try
        {
            var installDirectory = EnsureExistingDirectory(request.InstallDirectory, "install");
            EnsureInstallPathIsSafe(installDirectory);
            log.Write($"Validated install directory: {installDirectory}");

            var packageRootDirectory = EnsureExistingDirectory(request.PackageRootDirectory, "package");
            log.Write($"Validated package directory: {packageRootDirectory}");

            var backupDirectory = EnsureSafeBackupDirectory(request.BackupDirectory);
            log.Write($"Validated backup directory: {backupDirectory}");

            var installRoot = Path.GetFullPath(installDirectory);
            var packageRoot = Path.GetFullPath(packageRootDirectory);
            if (packageRoot.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
            {
                log.Write("Rejected package directory because it is inside the live install directory.");
                return new AppUpdateInstallExecutionResult(false, "Refusing to install from a package directory inside the live install directory.");
            }

            var relaunchPath = Path.Combine(packageRoot, request.RelaunchExecutableName);
            if (!File.Exists(relaunchPath))
            {
                log.Write($"Rejected package because {request.RelaunchExecutableName} was missing.");
                return new AppUpdateInstallExecutionResult(false, $"The update package does not contain {request.RelaunchExecutableName}.");
            }

            log.Write("Waiting for parent MetaGrid process to exit before replacement.");
            var waitResult = await WaitForProcessExitAsync(request.ParentProcessId, TimeSpan.FromSeconds(90), cancellationToken);
            if (!waitResult)
            {
                log.Write("Parent process did not exit before timeout.");
                return new AppUpdateInstallExecutionResult(false, "MetaGrid did not exit in time for the updater to replace the installation.");
            }

            Directory.CreateDirectory(backupDirectory);
            log.Write("Creating backup mirror of the old installation.");
            MirrorDirectory(installRoot, backupDirectory, cancellationToken);
            log.Write("Backup mirror completed.");

            if (string.Equals(request.SimulatedFailureStage, "after-backup", StringComparison.OrdinalIgnoreCase))
            {
                log.Write("Simulated failure triggered after backup.");
                throw new InvalidOperationException("Simulated updater failure after backup.");
            }

            try
            {
                log.Write("Deleting old installation contents.");
                DeleteDirectoryContents(installRoot, cancellationToken);

                if (string.Equals(request.SimulatedFailureStage, "after-delete", StringComparison.OrdinalIgnoreCase))
                {
                    log.Write("Simulated failure triggered after delete and before copy.");
                    throw new InvalidOperationException("Simulated updater failure after delete.");
                }

                log.Write("Copying new package into installation directory.");
                CopyDirectoryContents(packageRoot, installRoot, cancellationToken);
                log.Write("Replacement copy completed.");
            }
            catch
            {
                log.Write("Replacement failed. Starting rollback from backup mirror.");
                DeleteDirectoryContents(installRoot, cancellationToken);
                CopyDirectoryContents(backupDirectory, installRoot, cancellationToken);
                log.Write("Rollback completed.");
                throw;
            }

            var relaunchedExecutablePath = Path.Combine(installRoot, request.RelaunchExecutableName);
            if (!request.SkipRelaunch && File.Exists(relaunchedExecutablePath))
            {
                log.Write($"Launching updated MetaGrid from {relaunchedExecutablePath}.");
                Process.Start(new ProcessStartInfo
                {
                    FileName = relaunchedExecutablePath,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = installRoot
                });
            }
            else if (request.SkipRelaunch)
            {
                log.Write("Relaunch skipped by test request.");
            }

            log.Write("MetaGrid updater completed successfully.");
            return new AppUpdateInstallExecutionResult(true, $"MetaGrid updated from {request.CurrentVersion} to {request.TargetVersion}.", relaunchedExecutablePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Write($"MetaGrid updater failed: {ex.Message}");
            return new AppUpdateInstallExecutionResult(false, ex.Message);
        }
    }

    private static string EnsureExistingDirectory(string path, string kind)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"MetaGrid updater received an empty {kind} directory path.");
        }

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"MetaGrid updater could not find the {kind} directory: {fullPath}");
        }

        var root = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrWhiteSpace(root)
            && string.Equals(root.TrimEnd(Path.DirectorySeparatorChar), fullPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"MetaGrid updater refused to use a drive root as the {kind} directory: {fullPath}");
        }

        return fullPath;
    }

    private static void EnsureInstallPathIsSafe(string installDirectory)
    {
        var fullPath = Path.GetFullPath(installDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar) ?? string.Empty,
            Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd(Path.DirectorySeparatorChar),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).TrimEnd(Path.DirectorySeparatorChar),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).TrimEnd(Path.DirectorySeparatorChar),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(Path.DirectorySeparatorChar)
        };

        if (blocked.Contains(fullPath))
        {
            throw new InvalidOperationException($"MetaGrid updater refused unsafe install directory: {fullPath}");
        }
    }

    private static string EnsureSafeBackupDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("MetaGrid updater received an empty backup directory path.");
        }

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    private static async Task<bool> WaitForProcessExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (processId <= 0)
        {
            return true;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            var started = Stopwatch.StartNew();
            while (!process.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (started.Elapsed >= timeout)
                {
                    return false;
                }

                await Task.Delay(250, cancellationToken);
                process.Refresh();
            }

            return true;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static void MirrorDirectory(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
    {
        DeleteDirectoryContents(destinationDirectory, cancellationToken);
        CopyDirectoryContents(sourceDirectory, destinationDirectory, cancellationToken);
    }

    private static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    private static void DeleteDirectoryContents(string directory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(file);
        }

        foreach (var childDirectory in Directory.GetDirectories(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Delete(childDirectory, recursive: true);
        }
    }
}

internal sealed class UpdaterLog
{
    private readonly string? _path;

    public UpdaterLog(string? path)
    {
        _path = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        if (_path is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        }
    }

    public void Write(string message)
    {
        if (_path is null)
        {
            return;
        }

        File.AppendAllText(_path, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}{Environment.NewLine}");
    }
}
