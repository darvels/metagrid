using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class GitHubReleaseClient(HttpClient httpClient) : IGitHubReleaseClient
{
    private static readonly Uri ReleasesUri = new("https://api.github.com/repos/darvels/metagrid/releases");

    public async Task<IReadOnlyList<AppReleaseMetadata>> GetReleasesAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("MetaGrid/1.0");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new GitHubReleaseClientException(response.StatusCode, body);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new GitHubReleaseClientException(HttpStatusCode.OK, "GitHub Releases payload was not an array.");
        }

        var releases = new List<AppReleaseMetadata>();
        foreach (var releaseElement in document.RootElement.EnumerateArray())
        {
            var assets = new List<AppReleaseAsset>();
            if (releaseElement.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var assetElement in assetsElement.EnumerateArray())
                {
                    var name = assetElement.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                    var downloadUrl = assetElement.TryGetProperty("browser_download_url", out var downloadElement) ? downloadElement.GetString() : null;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(downloadUrl))
                    {
                        continue;
                    }

                    assets.Add(new AppReleaseAsset(
                        name.Trim(),
                        downloadUrl.Trim(),
                        assetElement.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var size) ? size : 0,
                        assetElement.TryGetProperty("digest", out var digestElement) ? digestElement.GetString() : null));
                }
            }

            releases.Add(new AppReleaseMetadata(
                releaseElement.TryGetProperty("tag_name", out var tagNameElement) ? tagNameElement.GetString() ?? string.Empty : string.Empty,
                releaseElement.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? string.Empty : string.Empty,
                releaseElement.TryGetProperty("draft", out var draftValue) && draftValue.ValueKind == JsonValueKind.True,
                releaseElement.TryGetProperty("prerelease", out var prereleaseValue) && prereleaseValue.ValueKind == JsonValueKind.True,
                releaseElement.TryGetProperty("published_at", out var publishedValue) && publishedValue.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(publishedValue.GetString(), out var publishedAt)
                    ? publishedAt
                    : null,
                assets,
                releaseElement.TryGetProperty("html_url", out var htmlValue) ? htmlValue.GetString() : null));
        }

        return releases;
    }
}

public sealed class GitHubReleaseClientException(HttpStatusCode statusCode, string body) : Exception($"GitHub Releases request failed with {(int)statusCode} {statusCode}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Body { get; } = body;
}
