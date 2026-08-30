using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class AppUpdateDownloader(HttpClient httpClient, IAppPaths appPaths) : IAppUpdateDownloader
{
    private static readonly Regex ShaRegex = new(@"(?<hash>[a-fA-F0-9]{64})", RegexOptions.Compiled);

    public async Task<PreparedAppUpdatePackage> DownloadAndPrepareAsync(AppUpdateInfo updateInfo, CancellationToken cancellationToken, IProgress<AppUpdateProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(updateInfo);
        var releaseVersion = updateInfo.AvailableVersion ?? throw new InvalidOperationException("No available app version was provided.");
        var packageAsset = updateInfo.PackageAsset ?? throw new InvalidOperationException("No package asset was provided.");
        var shaAsset = updateInfo.Sha256Asset ?? throw new InvalidOperationException("No SHA256 asset was provided.");

        var sessionDirectory = Path.Combine(appPaths.AppUpdateSessionDirectory, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{releaseVersion.Replace('.', '_')}-{Guid.NewGuid():N}");
        var downloadDirectory = Path.Combine(sessionDirectory, "download");
        var extractionDirectory = Path.Combine(sessionDirectory, "extracted");
        var runnerDirectory = Path.Combine(sessionDirectory, "runner");
        var backupDirectory = Path.Combine(sessionDirectory, "backup");
        Directory.CreateDirectory(downloadDirectory);
        Directory.CreateDirectory(extractionDirectory);
        Directory.CreateDirectory(runnerDirectory);
        Directory.CreateDirectory(backupDirectory);

        var archivePath = Path.Combine(downloadDirectory, packageAsset.Name);
        var shaPath = Path.Combine(downloadDirectory, shaAsset.Name);

        progress?.Report(new AppUpdateProgress(AppUpdateProgressPhase.Downloading, $"Downloading MetaGrid {releaseVersion}."));
        await DownloadFileAsync(packageAsset.DownloadUrl, archivePath, cancellationToken);
        await DownloadFileAsync(shaAsset.DownloadUrl, shaPath, cancellationToken);

        progress?.Report(new AppUpdateProgress(AppUpdateProgressPhase.Verifying, $"Verifying MetaGrid {releaseVersion} package."));
        var expectedSha = await ReadExpectedShaAsync(shaPath, cancellationToken);
        var actualSha = ComputeSha256(archivePath);
        if (!string.Equals(expectedSha, actualSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Downloaded MetaGrid update hash mismatch. Expected {expectedSha}, got {actualSha}.");
        }

        progress?.Report(new AppUpdateProgress(
            AppUpdateProgressPhase.Verifying,
            $"Verified MetaGrid {releaseVersion} SHA256 {actualSha[..12]}."));

        progress?.Report(new AppUpdateProgress(AppUpdateProgressPhase.Extracting, $"Extracting MetaGrid {releaseVersion} package."));
        ExtractArchiveSafely(archivePath, extractionDirectory);
        var packageRoot = ResolvePackageRootDirectory(extractionDirectory, "MetaGrid.exe");
        var updaterPath = Path.Combine(packageRoot, "MetaGrid.Updater.exe");
        if (!File.Exists(updaterPath))
        {
            throw new InvalidOperationException("The downloaded MetaGrid release did not contain MetaGrid.Updater.exe.");
        }

        var runnerUpdaterPath = Path.Combine(runnerDirectory, "MetaGrid.Updater.exe");
        File.Copy(updaterPath, runnerUpdaterPath, overwrite: true);

        return new PreparedAppUpdatePackage(
            releaseVersion,
            sessionDirectory,
            archivePath,
            shaPath,
            expectedSha,
            extractionDirectory,
            packageRoot,
            string.Empty,
            "MetaGrid.exe",
            runnerUpdaterPath,
            backupDirectory,
            Environment.ProcessId,
            Path.Combine(sessionDirectory, "updater.log"));
    }

    private async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("MetaGrid/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await responseStream.CopyToAsync(fileStream, cancellationToken);
    }

    private static async Task<string> ReadExpectedShaAsync(string shaPath, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(shaPath, cancellationToken);
        var match = ShaRegex.Match(text);
        if (!match.Success)
        {
            throw new InvalidOperationException("The downloaded SHA256 file did not contain a valid hash.");
        }

        return match.Groups["hash"].Value.ToUpperInvariant();
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    private static void ExtractArchiveSafely(string archivePath, string extractionDirectory)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var extractionRoot = Path.GetFullPath(extractionDirectory);

        foreach (var entry in archive.Entries)
        {
            var targetPath = Path.GetFullPath(Path.Combine(extractionRoot, entry.FullName));
            if (!targetPath.StartsWith(extractionRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsafe path detected in release archive: {entry.FullName}");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    private static string ResolvePackageRootDirectory(string extractionDirectory, string expectedExecutableName)
    {
        var directExecutable = Path.Combine(extractionDirectory, expectedExecutableName);
        if (File.Exists(directExecutable))
        {
            return extractionDirectory;
        }

        var candidateDirectories = Directory.GetDirectories(extractionDirectory);
        if (candidateDirectories.Length == 1)
        {
            var candidateExecutable = Path.Combine(candidateDirectories[0], expectedExecutableName);
            if (File.Exists(candidateExecutable))
            {
                return candidateDirectories[0];
            }
        }

        throw new InvalidOperationException($"The extracted MetaGrid update package did not contain {expectedExecutableName}.");
    }
}
