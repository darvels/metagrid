using System.Net.Http.Headers;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Utilities;

namespace MetaGrid.Infrastructure.Services;

public sealed class D2ptHeroGridProvider(
    IHeroCatalogService heroCatalogService,
    IDotaGridService dotaGridService,
    ILoggingService loggingService) : IHeroGridProvider
{
    private static readonly Uri HeroGridPageUri = new("https://dota2protracker.com/meta-hero-grids");

    public async Task<HeroGridSnapshot> FetchAsync(HeroGridPreset preset, CancellationToken cancellationToken)
    {
        var retrieval = await RetrieveAsync(cancellationToken);
        await loggingService.LogAsync(LogLevelKind.Information, "D2PT retrieval result", new
        {
            retrieval.Status,
            retrieval.Strategy,
            retrieval.HttpStatusCode,
            retrieval.FinalUri,
            retrieval.UsedFallback,
            retrieval.Details
        }, cancellationToken);

        if (retrieval.Status is ProviderStatus.CloudflareBlocked or ProviderStatus.RateLimited or ProviderStatus.NetworkUnavailable or ProviderStatus.Unavailable)
        {
            throw new HeroGridProviderUnavailableException(retrieval.Status, retrieval.Details ?? "Dota2ProTracker was unavailable.");
        }

        if (retrieval.Body is null)
        {
            throw new HeroGridProviderUnavailableException(ProviderStatus.Unavailable, "Dota2ProTracker did not return a usable hero-grid response body.");
        }

        var heroCatalog = await heroCatalogService.LoadByNameAsync(cancellationToken);
        var parseResult = D2ptProviderParser.Parse(retrieval.Body, preset, heroCatalog);
        await loggingService.LogAsync(LogLevelKind.Information, "D2PT parse result", new
        {
            parseResult.Status,
            parseResult.PatchLabel,
            LayoutCount = parseResult.Layouts.Count,
            parseResult.Details
        }, cancellationToken);

        if (parseResult.Status == ProviderStatus.CloudflareBlocked)
        {
            throw new HeroGridProviderUnavailableException(ProviderStatus.CloudflareBlocked, parseResult.Details ?? "Cloudflare blocked D2PT retrieval.");
        }

        if (parseResult.Status is ProviderStatus.UnexpectedResponse or ProviderStatus.ParsingFailed)
        {
            throw new HeroGridProviderParseException(parseResult.Details ?? "D2PT returned an unexpected hero-grid format.");
        }

        var hash = dotaGridService.ComputeSnapshotHash(new HeroGridSnapshot
        {
            SourceName = "Dota2ProTracker",
            ProviderName = "Dota2ProTracker",
            SourceStrategy = retrieval.Strategy,
            Preset = preset,
            PatchLabel = parseResult.PatchLabel,
            CapturedAt = DateTimeOffset.UtcNow,
            Hash = string.Empty,
            Layouts = parseResult.Layouts,
            ProviderStatus = ProviderStatus.Online,
            OriginKind = GridOriginKind.NativeD2pt,
            SourceDetails = $"{retrieval.Strategy} - {parseResult.PatchLabel}"
        });

        await loggingService.LogAsync(LogLevelKind.Information, "D2PT normalization result", new
        {
            Hash = hash,
            parseResult.PatchLabel,
            retrieval.Strategy
        }, cancellationToken);

        return new HeroGridSnapshot
        {
            SourceName = "Dota2ProTracker",
            ProviderName = "Dota2ProTracker",
            SourceStrategy = retrieval.Strategy,
            Preset = preset,
            PatchLabel = parseResult.PatchLabel,
            CapturedAt = DateTimeOffset.UtcNow,
            Hash = hash,
            Layouts = parseResult.Layouts,
            ProviderStatus = ProviderStatus.Online,
            OriginKind = GridOriginKind.NativeD2pt,
            SourceDetails = $"{retrieval.Strategy} - {parseResult.PatchLabel}"
        };
    }

    private async Task<D2ptRetrievalResult> RetrieveAsync(CancellationToken cancellationToken)
    {
        var directHttp = await TryFetchViaHttpAsync(cancellationToken);
        await loggingService.LogAsync(LogLevelKind.Information, "D2PT direct HTTP result", new
        {
            directHttp.Status,
            directHttp.Strategy,
            directHttp.HttpStatusCode,
            directHttp.FinalUri,
            directHttp.Details
        }, cancellationToken);

        if (directHttp.Body is not null && directHttp.Status == ProviderStatus.Online)
        {
            return directHttp;
        }

        await loggingService.LogAsync(LogLevelKind.Information, "D2PT automatic browser fallback skipped", new
        {
            directHttp.Status,
            directHttp.HttpStatusCode,
            directHttp.FinalUri,
            directHttp.Details,
            Reason = "Automatic browser fallback is disabled. MetaGrid now classifies D2PT availability accurately and relies on the resilient provider fallback chain instead of launching a browser."
        }, cancellationToken);

        return directHttp with
        {
            UsedFallback = false,
            Details = directHttp.Details ?? "D2PT direct retrieval did not return usable hero-grid content."
        };
    }

    private async Task<D2ptRetrievalResult> TryFetchViaHttpAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MetaGrid", "1.0"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

        try
        {
            var response = await client.GetAsync(HeroGridPageUri, cancellationToken);
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("Enable JavaScript and cookies to continue", StringComparison.OrdinalIgnoreCase) ||
                response.Headers.TryGetValues("cf-mitigated", out var mitigatedValues) && mitigatedValues.Any(value => value.Contains("challenge", StringComparison.OrdinalIgnoreCase)))
            {
                return new D2ptRetrievalResult(ProviderStatus.CloudflareBlocked, "DirectHttp", null, (int)response.StatusCode, response.RequestMessage?.RequestUri?.ToString(), Details: "Cloudflare challenge page returned.");
            }

            if ((int)response.StatusCode == 429)
            {
                return new D2ptRetrievalResult(ProviderStatus.RateLimited, "DirectHttp", null, 429, response.RequestMessage?.RequestUri?.ToString(), Details: "D2PT rate-limited the request.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new D2ptRetrievalResult(ProviderStatus.Unavailable, "DirectHttp", null, (int)response.StatusCode, response.RequestMessage?.RequestUri?.ToString(), Details: $"HTTP {(int)response.StatusCode} was returned.");
            }

            return new D2ptRetrievalResult(ProviderStatus.Online, "DirectHttp", html, (int)response.StatusCode, response.RequestMessage?.RequestUri?.ToString());
        }
        catch (HttpRequestException ex)
        {
            return new D2ptRetrievalResult(ProviderStatus.NetworkUnavailable, "DirectHttp", null, Details: ex.Message);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new D2ptRetrievalResult(ProviderStatus.NetworkUnavailable, "DirectHttp", null, Details: $"D2PT request timed out: {ex.Message}");
        }
    }
}

public sealed class HeroGridProviderUnavailableException(ProviderStatus status, string message) : Exception(message)
{
    public ProviderStatus Status { get; } = status;
}

public sealed class HeroGridProviderParseException(string message) : Exception(message);
