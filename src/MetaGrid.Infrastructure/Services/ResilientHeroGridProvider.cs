using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class ResilientHeroGridProvider(
    OpenDotaHeroGridProvider openDotaProvider,
    GridSnapshotCacheService cacheService,
    ILoggingService loggingService) : IHeroGridProvider
{
    public async Task<HeroGridSnapshot> FetchAsync(HeroGridPreset preset, CancellationToken cancellationToken)
    {
        var failures = new List<string>();

        var liveResult = await TryProviderAsync(
            "OpenDota live grid",
            () => openDotaProvider.FetchAsync(preset, cancellationToken),
            cancellationToken);
        if (liveResult.Snapshot is not null)
        {
            await cacheService.SaveAsync(liveResult.Snapshot, cancellationToken);
            return liveResult.Snapshot;
        }

        failures.Add(liveResult.FailureMessage!);

        var cachedSnapshot = await cacheService.LoadAsync(cancellationToken);
        if (cachedSnapshot is not null)
        {
            await loggingService.LogAsync(LogLevelKind.Warning, "All live providers failed; using cached hero grid.", new
            {
                Failures = failures.ToArray(),
                cachedSnapshot.SourceName,
                cachedSnapshot.CapturedAt,
                cachedSnapshot.Hash
            }, cancellationToken);

            return CloneSnapshot(
                cachedSnapshot,
                ProviderStatus.Cached,
                GridOriginKind.Cached,
                "CachedLastKnownGood",
                $"Using cached grid saved from {cachedSnapshot.SourceName} at {cachedSnapshot.CapturedAt:yyyy-MM-dd HH:mm} UTC. Live provider failures: {string.Join(" | ", failures)}");
        }

        throw new HeroGridProviderUnavailableException(ResolveFailureStatus(failures), string.Join(" | ", failures));
    }

    private async Task<ProviderAttemptResult> TryProviderAsync(
        string providerLabel,
        Func<Task<HeroGridSnapshot>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await action();
            await loggingService.LogAsync(LogLevelKind.Information, "Hero grid provider succeeded", new
            {
                providerLabel,
                snapshot.SourceName,
                snapshot.SourceStrategy,
                snapshot.OriginKind,
                snapshot.Hash,
                snapshot.CapturedAt
            }, cancellationToken);
            return new ProviderAttemptResult(snapshot, null);
        }
        catch (HeroGridProviderUnavailableException ex)
        {
            await loggingService.LogAsync(LogLevelKind.Warning, "Hero grid provider unavailable", new
            {
                providerLabel,
                ex.Status,
                ex.Message
            }, cancellationToken);
            return new ProviderAttemptResult(null, $"{providerLabel}: {ex.Status} - {ex.Message}");
        }
        catch (HeroGridProviderParseException ex)
        {
            await loggingService.LogAsync(LogLevelKind.Warning, "Hero grid provider parse failure", new
            {
                providerLabel,
                ex.Message
            }, cancellationToken);
            return new ProviderAttemptResult(null, $"{providerLabel}: ParsingFailed - {ex.Message}");
        }
    }

    private static ProviderStatus ResolveFailureStatus(IEnumerable<string> failures)
        => failures.Any(failure => failure.Contains(nameof(ProviderStatus.RateLimited), StringComparison.OrdinalIgnoreCase))
            ? ProviderStatus.RateLimited
            : ProviderStatus.Unavailable;

    private static HeroGridSnapshot CloneSnapshot(
        HeroGridSnapshot snapshot,
        ProviderStatus providerStatus,
        GridOriginKind originKind,
        string sourceStrategy,
        string sourceDetails)
        => new()
        {
            SourceName = snapshot.SourceName,
            ProviderName = snapshot.ProviderName,
            SourceStrategy = sourceStrategy,
            Preset = snapshot.Preset,
            PatchLabel = snapshot.PatchLabel,
            CapturedAt = snapshot.CapturedAt,
            Hash = snapshot.Hash,
            Layouts = snapshot.Layouts,
            RankBracket = snapshot.RankBracket,
            PeriodStart = snapshot.PeriodStart,
            PeriodEnd = snapshot.PeriodEnd,
            MinimumSampleMatches = snapshot.MinimumSampleMatches,
            UsesDerivedPositions = snapshot.UsesDerivedPositions,
            PositionDefinition = snapshot.PositionDefinition,
            RoleResults = snapshot.RoleResults,
            ProviderStatus = providerStatus,
            OriginKind = originKind,
            SourceDetails = sourceDetails
        };

    private sealed record ProviderAttemptResult(HeroGridSnapshot? Snapshot, string? FailureMessage);
}
