namespace MetaGrid.Infrastructure.Services;

public interface IBrowserDomFetcher
{
    Task<IReadOnlyList<BrowserDomFetchResult>> TryFetchAsync(string url, string workingDirectory, CancellationToken cancellationToken);
}
