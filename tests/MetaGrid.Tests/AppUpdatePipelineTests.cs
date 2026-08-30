using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;
using MetaGrid.Tests.TestSupport;

namespace MetaGrid.Tests;

public sealed class AppUpdatePipelineTests
{
    [Fact]
    public void AppVersionParser_StripsLeadingV_AndComparesSemanticVersions()
    {
        var parsed = AppVersionParser.TryParse("v0.2.0", out var version, out var normalized);

        Assert.True(parsed);
        Assert.Equal(new Version(0, 2, 0), version);
        Assert.Equal("0.2.0", normalized);
        Assert.True(AppVersionParser.Compare("v0.2.0", "0.1.9") > 0);
    }

    [Fact]
    public async Task GitHubAppUpdateService_ReturnsUpdateAvailable_ForNewerStableRelease()
    {
        var service = new GitHubAppUpdateService(
            new FakeReleaseClient([
                new AppReleaseMetadata(
                    "v0.2.0",
                    "MetaGrid 0.2.0",
                    false,
                    false,
                    DateTimeOffset.UtcNow,
                    [
                        new AppReleaseAsset("MetaGrid-v0.2.0-win-x64.zip", "https://example.test/metagrid.zip", 128),
                        new AppReleaseAsset("MetaGrid-v0.2.0-win-x64.zip.sha256", "https://example.test/metagrid.zip.sha256", 64)
                    ]),
                new AppReleaseMetadata(
                    "v0.3.0-beta.1",
                    "MetaGrid Beta",
                    false,
                    true,
                    DateTimeOffset.UtcNow,
                    [])
            ]),
            new FakeDownloader(),
            new FakeUpdaterLauncher(),
            new FakeRuntimeInfo("0.1.0"),
            new FakeClock(DateTimeOffset.Parse("2026-08-29T12:00:00Z")),
            new FakeLoggingService());

        var result = await service.CheckForUpdatesAsync(new AppSettings(), manual: true, progress: null, CancellationToken.None);

        Assert.Equal(AppUpdateCheckState.UpdateAvailable, result.State);
        Assert.Equal("0.2.0", result.AvailableVersion);
        Assert.True(result.IsUpdateAvailable);
        Assert.NotNull(result.PackageAsset);
        Assert.NotNull(result.Sha256Asset);
    }

    [Fact]
    public async Task AppUpdateDownloader_DownloadsVerifiesAndExtractsReleasePackage()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        Directory.CreateDirectory(paths.AppUpdateSessionDirectory);

        var zipBytes = CreateZipArchive([
            ("MetaGrid-v0.2.0-win-x64/MetaGrid.exe", Encoding.UTF8.GetBytes("app")),
            ("MetaGrid-v0.2.0-win-x64/MetaGrid.Updater.exe", Encoding.UTF8.GetBytes("updater"))
        ]);
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(zipBytes));
        var httpClient = new HttpClient(new FakeHttpMessageHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["https://example.test/metagrid.zip"] = CreateBinaryResponse(zipBytes),
            ["https://example.test/metagrid.zip.sha256"] = CreateTextResponse($"{sha}  MetaGrid-v0.2.0-win-x64.zip")
        }));

        var downloader = new AppUpdateDownloader(httpClient, paths);
        var updateInfo = new AppUpdateInfo(
            "0.1.0",
            "0.2.0",
            AppUpdateCheckState.UpdateAvailable,
            "Update ready",
            DateTimeOffset.UtcNow,
            true,
            false,
            new AppReleaseMetadata(
                "v0.2.0",
                "MetaGrid 0.2.0",
                false,
                false,
                DateTimeOffset.UtcNow,
                [
                    new AppReleaseAsset("MetaGrid-v0.2.0-win-x64.zip", "https://example.test/metagrid.zip", zipBytes.Length),
                    new AppReleaseAsset("MetaGrid-v0.2.0-win-x64.zip.sha256", "https://example.test/metagrid.zip.sha256", 80)
                ]),
            new AppReleaseAsset("MetaGrid-v0.2.0-win-x64.zip", "https://example.test/metagrid.zip", zipBytes.Length),
            new AppReleaseAsset("MetaGrid-v0.2.0-win-x64.zip.sha256", "https://example.test/metagrid.zip.sha256", 80));

        var package = await downloader.DownloadAndPrepareAsync(updateInfo, CancellationToken.None);

        Assert.True(File.Exists(package.DownloadedArchivePath));
        Assert.True(File.Exists(package.Sha256FilePath));
        Assert.True(Directory.Exists(package.PackageRootDirectory));
        Assert.True(File.Exists(Path.Combine(package.PackageRootDirectory, "MetaGrid.exe")));
        Assert.True(File.Exists(package.UpdaterExecutablePath));
        Assert.Equal(sha, package.ExpectedSha256);
    }

    [Fact]
    public async Task AppUpdateDownloader_RejectsZipTraversal()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        Directory.CreateDirectory(paths.AppUpdateSessionDirectory);

        var zipBytes = CreateZipArchive([
            ("../escape.txt", Encoding.UTF8.GetBytes("bad")),
            ("MetaGrid-v0.2.0-win-x64/MetaGrid.exe", Encoding.UTF8.GetBytes("app")),
            ("MetaGrid-v0.2.0-win-x64/MetaGrid.Updater.exe", Encoding.UTF8.GetBytes("updater"))
        ]);
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(zipBytes));
        var httpClient = new HttpClient(new FakeHttpMessageHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["https://example.test/metagrid.zip"] = CreateBinaryResponse(zipBytes),
            ["https://example.test/metagrid.zip.sha256"] = CreateTextResponse(sha)
        }));

        var downloader = new AppUpdateDownloader(httpClient, paths);
        var updateInfo = new AppUpdateInfo(
            "0.1.0",
            "0.2.0",
            AppUpdateCheckState.UpdateAvailable,
            "Update ready",
            DateTimeOffset.UtcNow,
            true,
            false,
            new AppReleaseMetadata("v0.2.0", "MetaGrid 0.2.0", false, false, DateTimeOffset.UtcNow, []),
            new AppReleaseAsset("MetaGrid-v0.2.0-win-x64.zip", "https://example.test/metagrid.zip", zipBytes.Length),
            new AppReleaseAsset("MetaGrid-v0.2.0-win-x64.zip.sha256", "https://example.test/metagrid.zip.sha256", 64));

        await Assert.ThrowsAsync<InvalidOperationException>(() => downloader.DownloadAndPrepareAsync(updateInfo, CancellationToken.None));
    }

    [Fact]
    public async Task AppUpdateInstallerEngine_ReplacesInstallDirectory_WithoutRelaunchInTests()
    {
        using var temp = new TemporaryDirectory();
        var installDirectory = Path.Combine(temp.Path, "install");
        var packageDirectory = Path.Combine(temp.Path, "package");
        var backupDirectory = Path.Combine(temp.Path, "backup");

        Directory.CreateDirectory(installDirectory);
        Directory.CreateDirectory(packageDirectory);
        await File.WriteAllTextAsync(Path.Combine(installDirectory, "old.txt"), "old");
        await File.WriteAllTextAsync(Path.Combine(packageDirectory, "MetaGrid.exe"), "new exe");
        await File.WriteAllTextAsync(Path.Combine(packageDirectory, "new.txt"), "new");

        var engine = new AppUpdateInstallerEngine();
        var result = await engine.ExecuteAsync(new AppUpdateInstallRequest(
            installDirectory,
            packageDirectory,
            "MetaGrid.exe",
            0,
            backupDirectory,
            "0.1.0",
            "0.2.0",
            true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(installDirectory, "MetaGrid.exe")));
        Assert.True(File.Exists(Path.Combine(installDirectory, "new.txt")));
        Assert.False(File.Exists(Path.Combine(installDirectory, "old.txt")));
        Assert.True(File.Exists(Path.Combine(backupDirectory, "old.txt")));
    }

    [Fact]
    public async Task AppUpdateInstallerEngine_RollsBackOldInstall_WhenFailureOccursAfterDelete()
    {
        using var temp = new TemporaryDirectory();
        var installDirectory = Path.Combine(temp.Path, "install");
        var packageDirectory = Path.Combine(temp.Path, "package");
        var backupDirectory = Path.Combine(temp.Path, "backup");

        Directory.CreateDirectory(installDirectory);
        Directory.CreateDirectory(packageDirectory);
        await File.WriteAllTextAsync(Path.Combine(installDirectory, "old.txt"), "old");
        await File.WriteAllTextAsync(Path.Combine(packageDirectory, "MetaGrid.exe"), "new exe");
        await File.WriteAllTextAsync(Path.Combine(packageDirectory, "new.txt"), "new");

        var engine = new AppUpdateInstallerEngine();
        var result = await engine.ExecuteAsync(new AppUpdateInstallRequest(
            installDirectory,
            packageDirectory,
            "MetaGrid.exe",
            0,
            backupDirectory,
            "0.1.0",
            "0.2.0",
            true,
            Path.Combine(temp.Path, "updater.log"),
            "after-delete"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(File.Exists(Path.Combine(installDirectory, "old.txt")));
        Assert.False(File.Exists(Path.Combine(installDirectory, "new.txt")));
        Assert.True(File.Exists(Path.Combine(backupDirectory, "old.txt")));
    }

    [Theory]
    [InlineData("C:\\")]
    [InlineData("C:\\Windows")]
    [InlineData("C:\\Program Files")]
    public async Task AppUpdateInstallerEngine_RejectsUnsafeInstallPaths(string installDirectory)
    {
        using var temp = new TemporaryDirectory();
        var packageDirectory = Path.Combine(temp.Path, "package");
        var backupDirectory = Path.Combine(temp.Path, "backup");
        Directory.CreateDirectory(packageDirectory);
        await File.WriteAllTextAsync(Path.Combine(packageDirectory, "MetaGrid.exe"), "new exe");

        var engine = new AppUpdateInstallerEngine();
        var result = await engine.ExecuteAsync(new AppUpdateInstallRequest(
            installDirectory,
            packageDirectory,
            "MetaGrid.exe",
            0,
            backupDirectory,
            "0.1.0",
            "0.2.0",
            true), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("refused", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitHubAppUpdateService_ReturnsNotWritable_WhenInstallPreflightFails()
    {
        var service = new GitHubAppUpdateService(
            new FakeReleaseClient([
                new AppReleaseMetadata(
                    "v0.2.0",
                    "MetaGrid 0.2.0",
                    false,
                    false,
                    DateTimeOffset.UtcNow,
                    [
                        new AppReleaseAsset("MetaGrid-v0.2.0-win-x64.zip", "https://example.test/metagrid.zip", 128),
                        new AppReleaseAsset("MetaGrid-v0.2.0-win-x64.zip.sha256", "https://example.test/metagrid.zip.sha256", 64)
                    ])
            ]),
            new FakeDownloader(),
            new FakeUpdaterLauncher(),
            new FakeRuntimeInfo("0.1.0", canInstallUpdate: false, failureReason: "Access denied to install directory."),
            new FakeClock(DateTimeOffset.Parse("2026-08-30T12:00:00Z")),
            new FakeLoggingService());

        var updateInfo = await service.CheckForUpdatesAsync(new AppSettings(), manual: true, progress: null, CancellationToken.None);
        var launchResult = await service.PrepareAndLaunchUpdateAsync(new AppSettings(), updateInfo, progress: null, CancellationToken.None);

        Assert.Equal(AppUpdateInstallState.NotWritable, launchResult.State);
        Assert.Contains("Access denied", launchResult.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(launchResult.ShouldExitApplication);
    }

    private static byte[] CreateZipArchive(IReadOnlyList<(string Path, byte[] Contents)> entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                var zipEntry = archive.CreateEntry(entry.Path);
                using var entryStream = zipEntry.Open();
                entryStream.Write(entry.Contents, 0, entry.Contents.Length);
            }
        }

        return memory.ToArray();
    }

    private static HttpResponseMessage CreateBinaryResponse(byte[] payload)
        => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        };

    private static HttpResponseMessage CreateTextResponse(string payload)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };

    private sealed class FakeReleaseClient(IReadOnlyList<AppReleaseMetadata> releases) : IGitHubReleaseClient
    {
        public Task<IReadOnlyList<AppReleaseMetadata>> GetReleasesAsync(CancellationToken cancellationToken)
            => Task.FromResult(releases);
    }

    private sealed class FakeDownloader : IAppUpdateDownloader
    {
        public Task<PreparedAppUpdatePackage> DownloadAndPrepareAsync(AppUpdateInfo updateInfo, CancellationToken cancellationToken, IProgress<AppUpdateProgress>? progress = null)
            => Task.FromResult(new PreparedAppUpdatePackage(
                updateInfo.AvailableVersion ?? "0.0.0",
                "session",
                "archive.zip",
                "archive.zip.sha256",
                "HASH",
                "extract",
                "package",
                "install",
                "MetaGrid.exe",
                "runner\\MetaGrid.Updater.exe",
                "backup",
                1234,
                "updater.log"));
    }

    private sealed class FakeUpdaterLauncher : IUpdaterLauncher
    {
        public Task<AppUpdateLaunchResult> LaunchAsync(PreparedAppUpdatePackage package, string currentVersion, CancellationToken cancellationToken)
            => Task.FromResult(new AppUpdateLaunchResult(AppUpdateInstallState.Started, "Started", true, package.ReleaseVersion));
    }

    private sealed class FakeRuntimeInfo(string version, bool canInstallUpdate = true, string failureReason = "") : IAppRuntimeInfo
    {
        public string GetCurrentVersion() => version;
        public string GetInstallDirectory() => "C:\\MetaGrid";
        public string GetExecutablePath() => "C:\\MetaGrid\\MetaGrid.exe";
        public string GetExecutableName() => "MetaGrid.exe";
        public string GetUpdaterExecutablePath() => "C:\\MetaGrid\\MetaGrid.Updater.exe";
        public int GetProcessId() => 4242;
        public bool CanInstallUpdate(out string reason)
        {
            reason = canInstallUpdate ? string.Empty : failureReason;
            return canInstallUpdate;
        }
    }

    private sealed class FakeClock(DateTimeOffset now) : IAppClock
    {
        public DateTimeOffset Now => now;
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeLoggingService : ILoggingService
    {
        public Task LogAsync(LogLevelKind level, string message, object? data = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public string GetLogsDirectory() => string.Empty;
    }

    private sealed class FakeHttpMessageHandler(IReadOnlyDictionary<string, HttpResponseMessage> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is null || !responses.TryGetValue(request.RequestUri.ToString(), out var response))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(CloneResponse(response));
        }

        private static HttpResponseMessage CloneResponse(HttpResponseMessage response)
        {
            var clone = new HttpResponseMessage(response.StatusCode);
            if (response.Content is not null)
            {
                var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                clone.Content = new ByteArrayContent(bytes);
                foreach (var header in response.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return clone;
        }
    }
}
