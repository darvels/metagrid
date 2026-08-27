using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;
using MetaGrid.Tests.TestSupport;

namespace MetaGrid.Tests;

public sealed class ResilientHeroGridProviderTests
{
    [Fact]
    public async Task CacheService_RoundTripsSnapshot()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var cache = new GridSnapshotCacheService(paths);
        var snapshot = StubProvider.CreateSnapshot([1, 2, 3], sourceName: "OpenDota");

        await cache.SaveAsync(snapshot, CancellationToken.None);
        var loaded = await cache.LoadAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(snapshot.Hash, loaded!.Hash);
        Assert.Equal(snapshot.SourceName, loaded.SourceName);
    }
}
