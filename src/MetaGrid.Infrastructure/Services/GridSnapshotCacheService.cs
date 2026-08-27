using System.Text.Json;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class GridSnapshotCacheService(IAppPaths appPaths)
{
    public async Task SaveAsync(HeroGridSnapshot snapshot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(appPaths.CachedGridSnapshotPath)!);
        await using var stream = File.Create(appPaths.CachedGridSnapshotPath);
        await JsonSerializer.SerializeAsync(stream, snapshot, cancellationToken: cancellationToken);
    }

    public async Task SaveAsync(HeroGridSnapshot snapshot, string rawJson, CancellationToken cancellationToken)
    {
        await SaveAsync(snapshot, cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(appPaths.CachedGridPayloadPath)!);
        await File.WriteAllTextAsync(appPaths.CachedGridPayloadPath, rawJson, cancellationToken);
    }

    public async Task<HeroGridSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(appPaths.CachedGridSnapshotPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(appPaths.CachedGridSnapshotPath);
        return await JsonSerializer.DeserializeAsync<HeroGridSnapshot>(stream, cancellationToken: cancellationToken);
    }

    public async Task<string?> LoadRawPayloadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(appPaths.CachedGridPayloadPath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(appPaths.CachedGridPayloadPath, cancellationToken);
    }
}
