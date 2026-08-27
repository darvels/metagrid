using System.Security.Cryptography;
using System.Text.Json;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Utilities;

namespace MetaGrid.Infrastructure.Services;

public sealed class BackupService(IAppPaths appPaths) : IBackupService
{
    public string GetBackupsDirectory() => appPaths.BackupsDirectory;

    public async Task<BackupEntry?> BackupAsync(string sourceFilePath, int retentionCount, BackupMetadata? metadata, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourceFilePath))
        {
            return null;
        }

        Directory.CreateDirectory(appPaths.BackupsDirectory);
        var accountId = metadata?.AccountId ?? TryResolveAccountId(sourceFilePath);
        var prefix = string.IsNullOrWhiteSpace(accountId) ? "global" : accountId;
        var fileName = $"{prefix}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_hero_grid_config.json";
        var backupPath = Path.Combine(appPaths.BackupsDirectory, fileName);

        await using (var source = File.OpenRead(sourceFilePath))
        await using (var target = File.Create(backupPath))
        {
            await source.CopyToAsync(target, cancellationToken);
            await target.FlushAsync(cancellationToken);
        }

        var sourceBytes = await File.ReadAllBytesAsync(sourceFilePath, cancellationToken);
        var backupBytes = await File.ReadAllBytesAsync(backupPath, cancellationToken);
        if (!sourceBytes.AsSpan().SequenceEqual(backupBytes))
        {
            throw new InvalidOperationException("Backup verification failed because the preserved bytes do not match the original file.");
        }

        var backupHash = Convert.ToHexString(SHA256.HashData(backupBytes));
        var fullMetadata = new BackupMetadata
        {
            AccountId = accountId,
            AccountDisplayName = metadata?.AccountDisplayName,
            OriginalPath = metadata?.OriginalPath ?? sourceFilePath,
            PreWriteHash = metadata?.PreWriteHash,
            BackupHash = backupHash,
            OperationId = metadata?.OperationId
        };
        await WriteMetadataAsync(backupPath, fullMetadata, cancellationToken);

        var backups = await GetBackupsAsync(cancellationToken, accountId);
        foreach (var oldBackup in backups.Skip(retentionCount))
        {
            File.Delete(oldBackup.FilePath);
            var metadataPath = GetMetadataPath(oldBackup.FilePath);
            if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
            }
        }

        return new BackupEntry
        {
            FilePath = backupPath,
            CreatedAt = DateTimeOffset.Now,
            AccountId = accountId,
            AccountDisplayName = fullMetadata.AccountDisplayName,
            OriginalPath = fullMetadata.OriginalPath,
            PreWriteHash = fullMetadata.PreWriteHash,
            BackupHash = fullMetadata.BackupHash,
            OperationId = fullMetadata.OperationId,
            FileSizeBytes = new FileInfo(backupPath).Length
        };
    }

    public Task<IReadOnlyList<BackupEntry>> GetBackupsAsync(CancellationToken cancellationToken, string? accountId = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(appPaths.BackupsDirectory);
        var items = Directory.EnumerateFiles(appPaths.BackupsDirectory, "*_hero_grid_config.json", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                var metadata = ReadMetadata(path);
                return new BackupEntry
                {
                    FilePath = path,
                    CreatedAt = File.GetCreationTimeUtc(path),
                    AccountId = metadata?.AccountId ?? TryResolveAccountId(path),
                    AccountDisplayName = metadata?.AccountDisplayName,
                    OriginalPath = metadata?.OriginalPath,
                    PreWriteHash = metadata?.PreWriteHash,
                    BackupHash = metadata?.BackupHash,
                    OperationId = metadata?.OperationId,
                    FileSizeBytes = new FileInfo(path).Length
                };
            })
            .Where(entry => string.IsNullOrWhiteSpace(accountId) || string.Equals(entry.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<BackupEntry>>(items);
    }

    public async Task<bool> RestoreLatestAsync(string destinationFilePath, CancellationToken cancellationToken)
    {
        var latest = (await GetBackupsAsync(cancellationToken, TryResolveAccountId(destinationFilePath))).FirstOrDefault();
        if (latest is null)
        {
            return false;
        }

        return await RestoreAsync(latest, destinationFilePath, cancellationToken);
    }

    public Task<bool> RestoreAsync(BackupEntry backup, string destinationFilePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)!);
        File.Copy(backup.FilePath, destinationFilePath, overwrite: true);
        return Task.FromResult(true);
    }

    private static string GetMetadataPath(string backupPath) => backupPath + ".meta.json";

    private static BackupMetadata? ReadMetadata(string backupPath)
    {
        var metadataPath = GetMetadataPath(backupPath);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(metadataPath);
            return JsonSerializer.Deserialize<BackupMetadata>(json, JsonDefaults.Storage);
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteMetadataAsync(string backupPath, BackupMetadata metadata, CancellationToken cancellationToken)
    {
        var metadataPath = GetMetadataPath(backupPath);
        await using var stream = File.Create(metadataPath);
        await JsonSerializer.SerializeAsync(stream, metadata, JsonDefaults.Storage, cancellationToken);
    }

    private static string? TryResolveAccountId(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var parts = fileName.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        return string.Equals(parts[0], "global", StringComparison.OrdinalIgnoreCase) ? null : parts[0];
    }
}
