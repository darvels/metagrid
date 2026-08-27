using System.Text.Json;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Utilities;

namespace MetaGrid.Infrastructure.Services;

public sealed class HistoryService(IAppPaths appPaths) : IHistoryService
{
    public async Task<IReadOnlyList<UpdateHistoryEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(appPaths.HistoryFilePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(appPaths.HistoryFilePath);
        return await JsonSerializer.DeserializeAsync<List<UpdateHistoryEntry>>(stream, JsonDefaults.Storage, cancellationToken)
            ?? [];
    }

    public async Task AppendAsync(IEnumerable<UpdateHistoryEntry> entries, CancellationToken cancellationToken)
    {
        var existing = (await LoadAsync(cancellationToken)).ToList();
        existing.InsertRange(0, entries.OrderByDescending(x => x.Timestamp));
        Directory.CreateDirectory(Path.GetDirectoryName(appPaths.HistoryFilePath)!);
        await using var stream = File.Create(appPaths.HistoryFilePath);
        await JsonSerializer.SerializeAsync(stream, existing.Take(200), JsonDefaults.Storage, cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(appPaths.HistoryFilePath)!);
        await using var stream = File.Create(appPaths.HistoryFilePath);
        await JsonSerializer.SerializeAsync(stream, Array.Empty<UpdateHistoryEntry>(), JsonDefaults.Storage, cancellationToken);
    }
}
