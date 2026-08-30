using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;

return await MetaGridSelfUpdateE2E.RunAsync(args);

internal static class MetaGridSelfUpdateE2E
{
    private const string OldVersion = "0.1.0";
    private const string NewVersion = "0.1.1";
    private const string PackageName = "MetaGrid-v0.1.1-win-x64";

    public static async Task<int> RunAsync(string[] args)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        var cancellationToken = cts.Token;
        var repoRoot = ResolveRepoRoot();
        var tempRoot = Path.Combine(Path.GetTempPath(), "MetaGrid-SelfUpdate-E2E", DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
        var result = new E2EResult(tempRoot, OldVersion, NewVersion);

        Directory.CreateDirectory(tempRoot);

        try
        {
            Console.WriteLine($"[MetaGrid.SelfUpdateE2E] Root: {tempRoot}");

            var buildRoot = Path.Combine(tempRoot, "builds");
            var oldBuildRoot = Path.Combine(buildRoot, "old-pristine");
            var newBuildRoot = Path.Combine(buildRoot, PackageName);
            Directory.CreateDirectory(buildRoot);

            await PublishAppPairAsync(repoRoot, oldBuildRoot, OldVersion, "0.1.0-e2e-old", cancellationToken);
            await PublishAppPairAsync(repoRoot, newBuildRoot, NewVersion, "0.1.1-e2e-new", cancellationToken);

            await File.WriteAllTextAsync(Path.Combine(oldBuildRoot, "old-version-sentinel.txt"), "OLD_BUILD_SENTINEL", cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(newBuildRoot, "new-version-sentinel.txt"), "NEW_BUILD_SENTINEL", cancellationToken);

            var downloadsRoot = Path.Combine(tempRoot, "downloads");
            Directory.CreateDirectory(downloadsRoot);
            var zipPath = Path.Combine(downloadsRoot, $"{PackageName}.zip");
            var shaPath = $"{zipPath}.sha256";
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            ZipFile.CreateFromDirectory(newBuildRoot, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
            var packageHash = ComputeSha256(zipPath);
            await File.WriteAllTextAsync(shaPath, $"{packageHash}  {Path.GetFileName(zipPath)}", cancellationToken);

            result.PackageAudit = await AuditPackageAsync(zipPath, shaPath, cancellationToken);

            var positiveScenario = await PrepareScenarioAsync(tempRoot, "positive", oldBuildRoot, cancellationToken);
            var positiveRelease = CreateReleaseMetadata(positiveScenario.ZipPath, positiveScenario.ShaPath);
            using (var positiveServer = new LocalReleaseServer(positiveScenario.DownloadMap))
            {
                result.Positive = await RunPositiveScenarioAsync(positiveScenario, positiveRelease, positiveServer, cancellationToken);
            }

            var shaScenario = await PrepareScenarioAsync(tempRoot, "sha-negative", oldBuildRoot, cancellationToken);
            await CorruptFileByteAsync(shaScenario.ZipPath, cancellationToken);
            var shaNegativeRelease = CreateReleaseMetadata(shaScenario.ZipPath, positiveScenario.ShaPath);
            using (var shaServer = new LocalReleaseServer(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                   {
                       ["package"] = shaScenario.ZipPath,
                       ["sha"] = positiveScenario.ShaPath
                   }))
            {
                result.NegativeSha = await RunShaNegativeScenarioAsync(shaScenario, shaNegativeRelease, shaServer, cancellationToken);
            }

            result.Rollback = await RunRollbackScenarioAsync(tempRoot, oldBuildRoot, newBuildRoot, cancellationToken);
            result.Security = await RunSecurityChecksAsync(tempRoot, newBuildRoot, cancellationToken);

            var reportPath = Path.Combine(tempRoot, "report.json");
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            Console.WriteLine($"[MetaGrid.SelfUpdateE2E] Report: {reportPath}");
            Console.WriteLine("[MetaGrid.SelfUpdateE2E] Completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[MetaGrid.SelfUpdateE2E] FAILED");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task PublishAppPairAsync(string repoRoot, string outputRoot, string version, string informationalVersion, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputRoot);
        await PublishProjectAsync(
            Path.Combine(repoRoot, "src", "MetaGrid.UI", "MetaGrid.UI.csproj"),
            outputRoot,
            version,
            informationalVersion,
            cancellationToken);

        await PublishProjectAsync(
            Path.Combine(repoRoot, "src", "MetaGrid.Updater", "MetaGrid.Updater.csproj"),
            outputRoot,
            version,
            informationalVersion,
            cancellationToken);
    }

    private static async Task PublishProjectAsync(string projectPath, string outputRoot, string version, string informationalVersion, CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "publish",
            projectPath,
            "-c", "Release",
            "-r", "win-x64",
            "--self-contained", "true",
            "-m:1",
            "-o", outputRoot,
            "/p:PublishSingleFile=true",
            "/p:DebugSymbols=false",
            "/p:DebugType=None",
            $"/p:Version={version}",
            $"/p:FileVersion={version}.0",
            $"/p:AssemblyVersion={version}.0",
            $"/p:InformationalVersion={informationalVersion}"
        };

        await RunProcessAsync("dotnet", args, ResolveRepoRoot(), cancellationToken);
    }

    private static async Task<ScenarioRoot> PrepareScenarioAsync(string tempRoot, string name, string pristineOldBuild, CancellationToken cancellationToken)
    {
        var scenarioRoot = Path.Combine(tempRoot, "scenarios", name);
        var installRoot = Path.Combine(scenarioRoot, "old-install");
        var packageRoot = Path.Combine(scenarioRoot, PackageName);
        var appDataRoot = Path.Combine(scenarioRoot, "appdata-root");
        var persistentRoot = Path.Combine(scenarioRoot, "persistent-data");
        var downloadsRoot = Path.Combine(scenarioRoot, "downloads");
        Directory.CreateDirectory(scenarioRoot);
        Directory.CreateDirectory(downloadsRoot);

        CopyDirectory(pristineOldBuild, installRoot);
        CopyDirectory(Path.Combine(tempRoot, "builds", PackageName), packageRoot);

        var zipPath = Path.Combine(downloadsRoot, $"{PackageName}.zip");
        var shaPath = $"{zipPath}.sha256";
        ZipFile.CreateFromDirectory(packageRoot, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
        var sha = ComputeSha256(zipPath);
        await File.WriteAllTextAsync(shaPath, $"{sha}  {Path.GetFileName(zipPath)}", cancellationToken);

        await CreateAppSettingsAsync(appDataRoot, cancellationToken);
        var sentinelMap = await CreatePersistentSentinelsAsync(persistentRoot, cancellationToken);
        return new ScenarioRoot(name, scenarioRoot, installRoot, packageRoot, appDataRoot, persistentRoot, zipPath, shaPath, sentinelMap);
    }

    private static async Task CreateAppSettingsAsync(string appDataRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(appDataRoot);
        Directory.CreateDirectory(Path.Combine(appDataRoot, "Settings"));

        var settings = new AppSettings
        {
            OnboardingCompleted = true,
            AutomaticallyCheckAppUpdates = false,
            AutoUpdateEnabled = false,
            AutomaticallyDetectSteam = false,
            MinimizeToTray = false,
            CloseToTray = false,
            StartMinimized = false
        };

        var settingsPath = Path.Combine(appDataRoot, "Settings", "settings.json");
        await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        }), cancellationToken);
    }

    private static async Task<Dictionary<string, PersistentSentinel>> CreatePersistentSentinelsAsync(string persistentRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(persistentRoot);
        Directory.CreateDirectory(Path.Combine(persistentRoot, "Backups"));
        Directory.CreateDirectory(Path.Combine(persistentRoot, "Personalization"));

        var sentinelPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["settings"] = Path.Combine(persistentRoot, "settings.json"),
            ["cache"] = Path.Combine(persistentRoot, "cache.json"),
            ["backup"] = Path.Combine(persistentRoot, "Backups", "sample.backup"),
            ["personalization"] = Path.Combine(persistentRoot, "Personalization", "sample.cache")
        };

        await File.WriteAllTextAsync(sentinelPaths["settings"], "SELF_UPDATE_E2E_SETTINGS_SENTINEL", cancellationToken);
        await File.WriteAllTextAsync(sentinelPaths["cache"], "SELF_UPDATE_E2E_CACHE_SENTINEL", cancellationToken);
        await File.WriteAllTextAsync(sentinelPaths["backup"], "SELF_UPDATE_E2E_BACKUP_SENTINEL", cancellationToken);
        await File.WriteAllTextAsync(sentinelPaths["personalization"], "SELF_UPDATE_E2E_PERSONALIZATION_SENTINEL", cancellationToken);

        return sentinelPaths.ToDictionary(
            pair => pair.Key,
            pair => new PersistentSentinel(pair.Value, ComputeSha256(pair.Value)),
            StringComparer.OrdinalIgnoreCase);
    }

    private static AppReleaseMetadata CreateReleaseMetadata(string zipPath, string shaPath)
        => new(
            $"v{NewVersion}",
            $"MetaGrid {NewVersion}",
            false,
            false,
            DateTimeOffset.Now,
            [
                new AppReleaseAsset(Path.GetFileName(zipPath), "http://localhost/package", new FileInfo(zipPath).Length),
                new AppReleaseAsset(Path.GetFileName(shaPath), "http://localhost/sha", new FileInfo(shaPath).Length)
            ]);

    private static async Task<PositiveScenarioResult> RunPositiveScenarioAsync(ScenarioRoot scenario, AppReleaseMetadata release, LocalReleaseServer server, CancellationToken cancellationToken)
    {
        using var oldProcess = LaunchMetaGridProcess(scenario.InstallRoot, scenario.AppDataRoot);
        await WaitForWindowAsync(oldProcess, cancellationToken);

        var oldExePath = Path.Combine(scenario.InstallRoot, "MetaGrid.exe");
        var oldBinaryHash = ComputeSha256(oldExePath);
        var progress = new List<string>();
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var appPaths = new HarnessAppPaths(Path.Combine(scenario.Root, "appdata"));
        var runtimeInfo = new HarnessRuntimeInfo(scenario.InstallRoot, oldProcess.Id, OldVersion);
        var logging = new HarnessLoggingService(Path.Combine(scenario.Root, "logs"));
        var service = new GitHubAppUpdateService(
            new HarnessReleaseClient(RewriteReleaseUrls(release, server.BaseUri)),
            new AppUpdateDownloader(httpClient, appPaths),
            new UpdaterLauncher(runtimeInfo),
            runtimeInfo,
            new AppClock(),
            logging);

        var settings = new AppSettings();
        var updateInfo = await service.CheckForUpdatesAsync(settings, manual: true, new Progress<AppUpdateProgress>(p => progress.Add(p.Message)), cancellationToken);
        var launchResult = await service.PrepareAndLaunchUpdateAsync(settings, updateInfo, new Progress<AppUpdateProgress>(p => progress.Add(p.Message)), cancellationToken);
        if (launchResult.UpdaterProcessId is null)
        {
            throw new InvalidOperationException($"Updater process was not launched. State={launchResult.State}; Message={launchResult.Message}");
        }

        await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
        oldProcess.Refresh();
        var oldAliveBeforeClose = !oldProcess.HasExited;

        await CloseOrKillProcessAsync(oldProcess, TimeSpan.FromSeconds(10), cancellationToken);

        using var updaterProcess = Process.GetProcessById(launchResult.UpdaterProcessId.Value);
        await WaitForExitAsync(updaterProcess, TimeSpan.FromSeconds(60), cancellationToken);

        var newProcess = await WaitForNewMetaGridProcessAsync(oldProcess.Id, oldExePath, cancellationToken);
        try
        {
            await WaitForWindowAsync(newProcess, cancellationToken);
            var newBinaryHash = ComputeSha256(oldExePath);
            var sentinelsUnchanged = VerifySentinelsUnchanged(scenario.PersistentSentinels);
            return new PositiveScenarioResult(
                oldProcess.Id,
                launchResult.UpdaterProcessId.Value,
                newProcess.Id,
                oldAliveBeforeClose,
                progress,
                oldBinaryHash,
                newBinaryHash,
                File.Exists(Path.Combine(scenario.InstallRoot, "new-version-sentinel.txt")),
                !File.Exists(Path.Combine(scenario.InstallRoot, "old-version-sentinel.txt")),
                GetFileVersion(oldExePath),
                newProcess.MainWindowTitle,
                newProcess.Responding,
                sentinelsUnchanged);
        }
        finally
        {
            await TerminateProcessAsync(newProcess);
        }
    }

    private static async Task<ShaNegativeScenarioResult> RunShaNegativeScenarioAsync(ScenarioRoot scenario, AppReleaseMetadata release, LocalReleaseServer server, CancellationToken cancellationToken)
    {
        using var oldProcess = LaunchMetaGridProcess(scenario.InstallRoot, scenario.AppDataRoot);
        await WaitForWindowAsync(oldProcess, cancellationToken);

        var oldExePath = Path.Combine(scenario.InstallRoot, "MetaGrid.exe");
        var oldBinaryHash = ComputeSha256(oldExePath);
        var progress = new List<string>();
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var appPaths = new HarnessAppPaths(Path.Combine(scenario.Root, "appdata"));
        var runtimeInfo = new HarnessRuntimeInfo(scenario.InstallRoot, oldProcess.Id, OldVersion);
        var logging = new HarnessLoggingService(Path.Combine(scenario.Root, "logs"));
        var service = new GitHubAppUpdateService(
            new HarnessReleaseClient(RewriteReleaseUrls(release, server.BaseUri)),
            new AppUpdateDownloader(httpClient, appPaths),
            new UpdaterLauncher(runtimeInfo),
            runtimeInfo,
            new AppClock(),
            logging);

        var updateInfo = await service.CheckForUpdatesAsync(new AppSettings(), manual: true, new Progress<AppUpdateProgress>(p => progress.Add(p.Message)), cancellationToken);
        var launchResult = await service.PrepareAndLaunchUpdateAsync(new AppSettings(), updateInfo, new Progress<AppUpdateProgress>(p => progress.Add(p.Message)), cancellationToken);

        oldProcess.Refresh();
        var oldStillRunning = !oldProcess.HasExited;
        var installUnchanged = oldBinaryHash == ComputeSha256(oldExePath)
                               && File.Exists(Path.Combine(scenario.InstallRoot, "old-version-sentinel.txt"))
                               && !File.Exists(Path.Combine(scenario.InstallRoot, "new-version-sentinel.txt"));

        await TerminateProcessAsync(oldProcess);
        using var relaunch = LaunchMetaGridProcess(scenario.InstallRoot, scenario.AppDataRoot);
        await WaitForWindowAsync(relaunch, cancellationToken);
        var relaunchedSuccessfully = relaunch.Responding && string.Equals(relaunch.MainWindowTitle, "MetaGrid", StringComparison.Ordinal);
        await TerminateProcessAsync(relaunch);

        return new ShaNegativeScenarioResult(
            launchResult.State.ToString(),
            launchResult.Message,
            launchResult.UpdaterProcessId is not null,
            oldStillRunning,
            installUnchanged,
            relaunchedSuccessfully,
            progress);
    }

    private static async Task<RollbackScenarioResult> RunRollbackScenarioAsync(string tempRoot, string pristineOldBuild, string pristineNewBuild, CancellationToken cancellationToken)
    {
        var scenarioRoot = Path.Combine(tempRoot, "scenarios", "rollback");
        var installRoot = Path.Combine(scenarioRoot, "old-install");
        var packageRoot = Path.Combine(scenarioRoot, PackageName);
        var appDataRoot = Path.Combine(scenarioRoot, "appdata-root");
        var persistentRoot = Path.Combine(scenarioRoot, "persistent-data");
        var backupRoot = Path.Combine(scenarioRoot, "backup");
        var logPath = Path.Combine(scenarioRoot, "logs", "updater.log");
        Directory.CreateDirectory(scenarioRoot);
        CopyDirectory(pristineOldBuild, installRoot);
        CopyDirectory(pristineNewBuild, packageRoot);
        await CreateAppSettingsAsync(appDataRoot, cancellationToken);
        await CreatePersistentSentinelsAsync(persistentRoot, cancellationToken);

        using var oldProcess = LaunchMetaGridProcess(installRoot, appDataRoot);
        await WaitForWindowAsync(oldProcess, cancellationToken);
        var updaterExe = Path.Combine(packageRoot, "MetaGrid.Updater.exe");
        var updater = StartUpdaterProcess(
            updaterExe,
            installRoot,
            packageRoot,
            backupRoot,
            oldProcess.Id,
            OldVersion,
            NewVersion,
            logPath,
            "after-delete");

        await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
        oldProcess.Refresh();
        var oldAliveBeforeClose = !oldProcess.HasExited;
        await CloseOrKillProcessAsync(oldProcess, TimeSpan.FromSeconds(10), cancellationToken);
        await WaitForExitAsync(updater, TimeSpan.FromSeconds(60), cancellationToken);

        var oldRestored = File.Exists(Path.Combine(installRoot, "old-version-sentinel.txt"))
                          && !File.Exists(Path.Combine(installRoot, "new-version-sentinel.txt"));
        using var relaunched = LaunchMetaGridProcess(installRoot, appDataRoot);
        await WaitForWindowAsync(relaunched, cancellationToken);
        var relaunchedSuccessfully = relaunched.Responding && string.Equals(relaunched.MainWindowTitle, "MetaGrid", StringComparison.Ordinal);
        await TerminateProcessAsync(relaunched);

        return new RollbackScenarioResult(
            updater.ExitCode,
            oldAliveBeforeClose,
            oldRestored,
            relaunchedSuccessfully,
            File.Exists(logPath) ? await File.ReadAllTextAsync(logPath, cancellationToken) : string.Empty);
    }

    private static async Task<SecurityScenarioResult> RunSecurityChecksAsync(string tempRoot, string pristineNewBuild, CancellationToken cancellationToken)
    {
        var scenarioRoot = Path.Combine(tempRoot, "scenarios", "security");
        var packageRoot = Path.Combine(scenarioRoot, PackageName);
        var backupRoot = Path.Combine(scenarioRoot, "backup");
        Directory.CreateDirectory(scenarioRoot);
        CopyDirectory(pristineNewBuild, packageRoot);

        var updaterExe = Path.Combine(packageRoot, "MetaGrid.Updater.exe");
        var rejections = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var installPath in new[] { "C:\\", "C:\\Windows", "C:\\Program Files", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) })
        {
            var logPath = Path.Combine(scenarioRoot, "logs", SanitizeFileName(installPath) + ".log");
            using var updater = StartUpdaterProcess(updaterExe, installPath, packageRoot, backupRoot, 0, OldVersion, NewVersion, logPath, null);
            await WaitForExitAsync(updater, TimeSpan.FromSeconds(20), cancellationToken);
            rejections[installPath] = updater.ExitCode;
        }

        var traversalRoot = Path.Combine(scenarioRoot, "traversal");
        Directory.CreateDirectory(traversalRoot);
        var maliciousZip = Path.Combine(traversalRoot, "malicious.zip");
        var maliciousSha = maliciousZip + ".sha256";
        CreateZipArchive(maliciousZip,
        [
            ("../outside.txt", Encoding.UTF8.GetBytes("escape")),
            ($"{PackageName}/MetaGrid.exe", Encoding.UTF8.GetBytes("app")),
            ($"{PackageName}/MetaGrid.Updater.exe", Encoding.UTF8.GetBytes("updater"))
        ]);
        await File.WriteAllTextAsync(maliciousSha, $"{ComputeSha256(maliciousZip)}  {Path.GetFileName(maliciousZip)}", cancellationToken);

        using var traversalServer = new LocalReleaseServer(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["package"] = maliciousZip,
            ["sha"] = maliciousSha
        });

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var downloader = new AppUpdateDownloader(httpClient, new HarnessAppPaths(Path.Combine(traversalRoot, "appdata")));
        var release = RewriteReleaseUrls(CreateReleaseMetadata(maliciousZip, maliciousSha), traversalServer.BaseUri);
        var updateInfo = new AppUpdateInfo(
            OldVersion,
            NewVersion,
            AppUpdateCheckState.UpdateAvailable,
            "Test traversal",
            DateTimeOffset.Now,
            true,
            false,
            release,
            release.Assets[0],
            release.Assets[1]);

        var traversalRejected = false;
        try
        {
            await downloader.DownloadAndPrepareAsync(updateInfo, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Unsafe path", StringComparison.OrdinalIgnoreCase))
        {
            traversalRejected = true;
        }

        var outsidePath = Path.GetFullPath(Path.Combine(Path.Combine(traversalRoot, "appdata"), "..", "outside.txt"));
        return new SecurityScenarioResult(rejections, traversalRejected, File.Exists(outsidePath));
    }

    private static AppReleaseMetadata RewriteReleaseUrls(AppReleaseMetadata release, Uri baseUri)
        => release with
        {
            Assets =
            [
                release.Assets[0] with { DownloadUrl = new Uri(baseUri, "package").ToString() },
                release.Assets[1] with { DownloadUrl = new Uri(baseUri, "sha").ToString() }
            ]
        };

    private static Process LaunchMetaGridProcess(string installRoot, string persistentRoot)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(installRoot, "MetaGrid.exe"),
            WorkingDirectory = installRoot,
            UseShellExecute = false
        };
        startInfo.Environment["METAGRID_APPDATA_ROOT"] = persistentRoot;
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start MetaGrid.exe");
    }

    private static Process StartUpdaterProcess(string updaterExe, string installRoot, string packageRoot, string backupRoot, int parentPid, string currentVersion, string targetVersion, string logPath, string? simulateFailure)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = updaterExe,
            WorkingDirectory = Path.GetDirectoryName(updaterExe)!,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(parentPid.ToString());
        startInfo.ArgumentList.Add("--install-dir");
        startInfo.ArgumentList.Add(installRoot);
        startInfo.ArgumentList.Add("--package-dir");
        startInfo.ArgumentList.Add(packageRoot);
        startInfo.ArgumentList.Add("--exe-name");
        startInfo.ArgumentList.Add("MetaGrid.exe");
        startInfo.ArgumentList.Add("--backup-dir");
        startInfo.ArgumentList.Add(backupRoot);
        startInfo.ArgumentList.Add("--current-version");
        startInfo.ArgumentList.Add(currentVersion);
        startInfo.ArgumentList.Add("--target-version");
        startInfo.ArgumentList.Add(targetVersion);
        startInfo.ArgumentList.Add("--skip-relaunch");
        startInfo.ArgumentList.Add("true");
        startInfo.ArgumentList.Add("--log-path");
        startInfo.ArgumentList.Add(logPath);
        if (!string.IsNullOrWhiteSpace(simulateFailure))
        {
            startInfo.ArgumentList.Add("--simulate-failure");
            startInfo.ArgumentList.Add(simulateFailure);
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start MetaGrid.Updater.exe");
    }

    private static async Task WaitForWindowAsync(Process process, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        while (started.Elapsed < TimeSpan.FromSeconds(30))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new InvalidOperationException($"Process {process.Id} exited before showing a window.");
            }

            process.Refresh();
            if (!string.IsNullOrWhiteSpace(process.MainWindowTitle))
            {
                return;
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException($"Process {process.Id} did not create a visible window within 30 seconds.");
    }

    private static async Task<Process> WaitForNewMetaGridProcessAsync(int oldProcessId, string executablePath, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        while (started.Elapsed < TimeSpan.FromSeconds(45))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var process in Process.GetProcessesByName("MetaGrid"))
            {
                try
                {
                    if (process.Id == oldProcessId || process.HasExited)
                    {
                        process.Dispose();
                        continue;
                    }

                    var candidatePath = process.MainModule?.FileName;
                    if (string.Equals(candidatePath, executablePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return process;
                    }
                }
                catch
                {
                    process.Dispose();
                }
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException("Updated MetaGrid process was not detected.");
    }

    private static async Task WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var completed = await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds), cancellationToken);
        if (!completed)
        {
            throw new TimeoutException($"Process {process.Id} did not exit within {timeout}.");
        }
    }

    private static async Task CloseOrKillProcessAsync(Process process, TimeSpan gracefulTimeout, CancellationToken cancellationToken)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.CloseMainWindow();
        }
        catch
        {
            // Fall back to kill below.
        }

        var exitedGracefully = await Task.Run(() => process.WaitForExit((int)gracefulTimeout.TotalMilliseconds), cancellationToken);
        if (exitedGracefully)
        {
            return;
        }

        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(cancellationToken);
    }

    private static async Task TerminateProcessAsync(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.CloseMainWindow();
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch
        {
            // Best effort cleanup for isolated temp processes.
        }
    }

    private static string ResolveRepoRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static Task<ReleasePackageAudit> AuditPackageAsync(string zipPath, string shaPath, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entries = archive.Entries.Select(entry => entry.FullName).ToArray();
        var hasMetaGridExe = entries.Any(entry => entry.EndsWith("/MetaGrid.exe", StringComparison.OrdinalIgnoreCase));
        var hasUpdaterExe = entries.Any(entry => entry.EndsWith("/MetaGrid.Updater.exe", StringComparison.OrdinalIgnoreCase));
        var hasSourceLeak = entries.Any(entry => entry.Contains("/obj/", StringComparison.OrdinalIgnoreCase) || entry.Contains("/src/", StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(new ReleasePackageAudit(
            Path.GetFileName(zipPath),
            Path.GetFileName(shaPath),
            hasMetaGridExe,
            hasUpdaterExe,
            !hasSourceLeak,
            new FileInfo(zipPath).Length,
            ComputeSha256(zipPath),
            entries.Take(20).ToArray()));
    }

    private static void CreateZipArchive(string zipPath, IReadOnlyList<(string Path, byte[] Contents)> entries)
    {
        using var stream = File.Create(zipPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            var zipEntry = archive.CreateEntry(entry.Path);
            using var entryStream = zipEntry.Open();
            entryStream.Write(entry.Contents, 0, entry.Contents.Length);
        }
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string GetFileVersion(string filePath)
        => FileVersionInfo.GetVersionInfo(filePath).ProductVersion ?? string.Empty;

    private static Dictionary<string, bool> VerifySentinelsUnchanged(IReadOnlyDictionary<string, PersistentSentinel> sentinels)
        => sentinels.ToDictionary(
            pair => pair.Key,
            pair => File.Exists(pair.Value.Path) && string.Equals(ComputeSha256(pair.Value.Path), pair.Value.ExpectedSha256, StringComparison.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        if (Directory.Exists(destinationDirectory))
        {
            Directory.Delete(destinationDirectory, recursive: true);
        }

        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    private static async Task CorruptFileByteAsync(string filePath, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);
    }

    private static string SanitizeFileName(string path)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(path.Length);
        foreach (var character in path)
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
        }

        return builder.ToString();
    }

    private static async Task RunProcessAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
        var stdOut = process.StandardOutput.ReadToEndAsync();
        var stdErr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);
        var output = await stdOut;
        var error = await stdErr;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }
    }
}

internal sealed record ScenarioRoot(
    string Name,
    string Root,
    string InstallRoot,
    string PackageRoot,
    string AppDataRoot,
    string PersistentRoot,
    string ZipPath,
    string ShaPath,
    IReadOnlyDictionary<string, PersistentSentinel> PersistentSentinels)
{
    public IReadOnlyDictionary<string, string> DownloadMap => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["package"] = ZipPath,
        ["sha"] = ShaPath
    };
}

internal sealed record PersistentSentinel(string Path, string ExpectedSha256);

internal sealed record ReleasePackageAudit(
    string ZipFilename,
    string ShaFilename,
    bool HasMetaGridExe,
    bool HasUpdaterExe,
    bool CleanLayout,
    long PackageSize,
    string PackageHash,
    IReadOnlyList<string> SampleEntries);

internal sealed record PositiveScenarioResult(
    int OldPid,
    int UpdaterPid,
    int NewPid,
    bool OldAliveBeforeClose,
    IReadOnlyList<string> ProgressMessages,
    string OldBinaryHash,
    string NewBinaryHash,
    bool NewSentinelExists,
    bool OldSentinelRemoved,
    string NewVersionReported,
    string MainWindowTitle,
    bool Responding,
    IReadOnlyDictionary<string, bool> PersistentSentinelsPreserved);

internal sealed record ShaNegativeScenarioResult(
    string LaunchState,
    string Message,
    bool UpdaterLaunched,
    bool OldProcessStillRunning,
    bool InstallUnchanged,
    bool OldAppRelaunched,
    IReadOnlyList<string> ProgressMessages);

internal sealed record RollbackScenarioResult(
    int UpdaterExitCode,
    bool OldAliveBeforeClose,
    bool OldInstallationRestored,
    bool OldAppRelaunched,
    string UpdaterLog);

internal sealed record SecurityScenarioResult(
    IReadOnlyDictionary<string, int> UnsafePathExitCodes,
    bool TraversalRejected,
    bool OutsideFileCreated);

internal sealed class E2EResult(string isolatedRoot, string oldVersion, string newVersion)
{
    public string IsolatedRoot { get; init; } = isolatedRoot;
    public string OldVersion { get; init; } = oldVersion;
    public string NewVersion { get; init; } = newVersion;
    public ReleasePackageAudit? PackageAudit { get; set; }
    public PositiveScenarioResult? Positive { get; set; }
    public ShaNegativeScenarioResult? NegativeSha { get; set; }
    public RollbackScenarioResult? Rollback { get; set; }
    public SecurityScenarioResult? Security { get; set; }
}

internal sealed class HarnessReleaseClient(AppReleaseMetadata release) : IGitHubReleaseClient
{
    public Task<IReadOnlyList<AppReleaseMetadata>> GetReleasesAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<AppReleaseMetadata>>([release]);
}

internal sealed class HarnessRuntimeInfo(string installDirectory, int processId, string currentVersion) : IAppRuntimeInfo
{
    public string GetCurrentVersion() => currentVersion;
    public string GetInstallDirectory() => installDirectory;
    public string GetExecutablePath() => Path.Combine(installDirectory, "MetaGrid.exe");
    public string GetExecutableName() => "MetaGrid.exe";
    public string GetUpdaterExecutablePath() => Path.Combine(installDirectory, "MetaGrid.Updater.exe");
    public int GetProcessId() => processId;

    public bool CanInstallUpdate(out string reason)
    {
        try
        {
            var probePath = Path.Combine(installDirectory, $".probe-{Guid.NewGuid():N}");
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }
}

internal sealed class HarnessLoggingService : ILoggingService
{
    private readonly string _logsDirectory;

    public HarnessLoggingService(string logsDirectory)
    {
        _logsDirectory = logsDirectory;
        Directory.CreateDirectory(_logsDirectory);
    }

    public async Task LogAsync(LogLevelKind level, string message, object? data = null, CancellationToken cancellationToken = default)
    {
        var payload = data is null ? string.Empty : JsonSerializer.Serialize(data);
        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {level} {message} {payload}{Environment.NewLine}";
        await File.AppendAllTextAsync(Path.Combine(_logsDirectory, "metagrid-e2e.log"), line, cancellationToken);
    }

    public string GetLogsDirectory() => _logsDirectory;
}

internal sealed class HarnessAppPaths : IAppPaths
{
    public HarnessAppPaths(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        SettingsFilePath = Path.Combine(rootDirectory, "Settings", "settings.json");
        HistoryFilePath = Path.Combine(rootDirectory, "History", "update-history.json");
        LogsDirectory = Path.Combine(rootDirectory, "Logs");
        BackupsDirectory = Path.Combine(rootDirectory, "Backups");
        CacheDirectory = Path.Combine(rootDirectory, "Cache");
        CachedGridSnapshotPath = Path.Combine(CacheDirectory, "grid-snapshot.json");
        CachedGridPayloadPath = Path.Combine(CacheDirectory, "grid-payload.json");
        PersonalizationCacheDirectory = Path.Combine(CacheDirectory, "Personalization");
        D2ptCacheDirectory = Path.Combine(CacheDirectory, "D2PT");
        D2ptTempDirectory = Path.Combine(CacheDirectory, "D2PT", "Temp");
        D2ptWebView2ProfileDirectory = Path.Combine(CacheDirectory, "D2PT", "WebView2");
        AppUpdateDirectory = Path.Combine(rootDirectory, "AppUpdate");
        AppUpdateSessionDirectory = Path.Combine(AppUpdateDirectory, "Sessions");

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(HistoryFilePath)!);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(PersonalizationCacheDirectory);
        Directory.CreateDirectory(D2ptCacheDirectory);
        Directory.CreateDirectory(D2ptTempDirectory);
        Directory.CreateDirectory(D2ptWebView2ProfileDirectory);
        Directory.CreateDirectory(AppUpdateSessionDirectory);
    }

    public string RootDirectory { get; }
    public string SettingsFilePath { get; }
    public string HistoryFilePath { get; }
    public string LogsDirectory { get; }
    public string BackupsDirectory { get; }
    public string CacheDirectory { get; }
    public string CachedGridSnapshotPath { get; }
    public string CachedGridPayloadPath { get; }
    public string PersonalizationCacheDirectory { get; }
    public string D2ptCacheDirectory { get; }
    public string D2ptTempDirectory { get; }
    public string D2ptWebView2ProfileDirectory { get; }
    public string AppUpdateDirectory { get; }
    public string AppUpdateSessionDirectory { get; }
}

internal sealed class LocalReleaseServer : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly HttpListener _listener = new();
    private readonly Task _serverTask;
    private readonly IReadOnlyDictionary<string, string> _filesByRoute;

    public LocalReleaseServer(IReadOnlyDictionary<string, string> filesByRoute)
    {
        _filesByRoute = filesByRoute;
        var port = GetEphemeralPort();
        BaseUri = new Uri($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add(BaseUri.ToString());
        _listener.Start();
        _serverTask = Task.Run(() => ServeAsync(_cts.Token));
    }

    public Uri BaseUri { get; }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch
        {
            // Ignore cleanup errors.
        }

        try
        {
            _serverTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Ignore cleanup errors.
        }
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                break;
            }

            var route = context.Request.Url?.AbsolutePath.Trim('/').ToLowerInvariant();
            if (route is null || !_filesByRoute.TryGetValue(route, out var filePath) || !File.Exists(filePath))
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            context.Response.StatusCode = 200;
            context.Response.ContentLength64 = bytes.Length;
            context.Response.ContentType = route.EndsWith("sha", StringComparison.Ordinal) ? "text/plain" : "application/octet-stream";
            await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
            context.Response.Close();
        }
    }

    private static int GetEphemeralPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
