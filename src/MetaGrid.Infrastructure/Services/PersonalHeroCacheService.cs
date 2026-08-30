using System.Text.Json;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Utilities;

namespace MetaGrid.Infrastructure.Services;

public sealed class PersonalHeroCacheService(IAppPaths appPaths) : IPersonalHeroCacheService
{
    public async Task<PersonalizationCacheEntry?> LoadAsync(string accountId, CancellationToken cancellationToken)
    {
        var path = GetCachePath(accountId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PersonalizationCacheEntry>(stream, JsonDefaults.Storage, cancellationToken);
    }

    public async Task SaveAsync(PersonalizationCacheEntry selection, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(appPaths.PersonalizationCacheDirectory);
        var path = GetCachePath(selection.AccountId);
        var tempPath = path + ".tmp";

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, selection, JsonDefaults.Storage, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    public Task DeleteAsync(string accountId, CancellationToken cancellationToken)
    {
        var path = GetCachePath(accountId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetCachePath(string accountId)
        => Path.Combine(appPaths.PersonalizationCacheDirectory, $"{accountId}.json");
}

