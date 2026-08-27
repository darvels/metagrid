using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

internal sealed record D2ptRetrievalResult(
    ProviderStatus Status,
    string Strategy,
    string? Body,
    int? HttpStatusCode = null,
    string? FinalUri = null,
    bool UsedFallback = false,
    string? Details = null);

public sealed record BrowserDomFetchResult(
    bool Success,
    string BrowserPath,
    bool BrowserDetected,
    bool ProcessStarted,
    int? ExitCode,
    string? Body,
    string? StandardError,
    bool CloudflareDetected,
    string? FinalUri = null,
    string? Details = null);

public sealed record D2ptParseResult(
    ProviderStatus Status,
    string PatchLabel,
    IReadOnlyList<HeroGridLayout> Layouts,
    string? Details = null);
