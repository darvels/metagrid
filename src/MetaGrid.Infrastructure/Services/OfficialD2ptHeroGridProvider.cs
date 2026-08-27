using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class OfficialD2ptHeroGridProvider(
    ID2ptOfficialGridRetrievalService retrievalService,
    GridSnapshotCacheService cacheService,
    ILoggingService loggingService) : IHeroGridProvider
{
    public async Task<HeroGridSnapshot> FetchAsync(HeroGridPreset preset, CancellationToken cancellationToken)
    {
        if (preset != HeroGridPreset.HighWinrate)
        {
            throw new HeroGridProviderUnavailableException(ProviderStatus.Unavailable, "The official D2PT production integration currently supports only the High Winrate preset.");
        }

        var retrieval = await retrievalService.RetrieveOfficialHighWinrateAsync(forceRefresh: false, cancellationToken);
        await loggingService.LogAsync(LogLevelKind.Information, "Official D2PT retrieval finished.", new
        {
            retrieval.Succeeded,
            retrieval.ProviderStatus,
            retrieval.Classification,
            retrieval.Message,
            retrieval.RuntimeVersion,
            retrieval.FinalUrl,
            retrieval.PageLoaded,
            retrieval.HighWinrateButtonFound,
            retrieval.OfficialDownloadTriggered,
            retrieval.DownloadCompleted,
            retrieval.ElapsedMs
        }, cancellationToken);

        if (retrieval.Succeeded && !string.IsNullOrWhiteSpace(retrieval.RawJson))
        {
            var validation = D2ptOfficialGridPayloadValidator.Validate(retrieval.RawJson, retrieval.SuggestedFilename);
            if (!validation.IsValid || validation.Snapshot is null)
            {
                throw new HeroGridProviderParseException(validation.Message);
            }

            var validatedSnapshot = validation.Snapshot;
            var snapshot = new HeroGridSnapshot
            {
                SourceName = validatedSnapshot.SourceName,
                ProviderName = validatedSnapshot.ProviderName,
                SourceStrategy = "OfficialDownloadPayload",
                Preset = validatedSnapshot.Preset,
                PatchLabel = validation.PatchLabel ?? validatedSnapshot.PatchLabel,
                CapturedAt = DateTimeOffset.UtcNow,
                Hash = validation.SemanticHash ?? string.Empty,
                Layouts = validatedSnapshot.Layouts,
                RankBracket = validatedSnapshot.RankBracket,
                PeriodStart = validatedSnapshot.PeriodStart,
                PeriodEnd = validatedSnapshot.PeriodEnd,
                MinimumSampleMatches = validatedSnapshot.MinimumSampleMatches,
                UsesDerivedPositions = validatedSnapshot.UsesDerivedPositions,
                PositionDefinition = validatedSnapshot.PositionDefinition,
                RoleResults = validatedSnapshot.RoleResults,
                ProviderStatus = ProviderStatus.Online,
                OriginKind = GridOriginKind.NativeD2pt,
                SourceDetails = "Official D2PT High Winrate download payload captured through embedded WebView2.",
                RawSha256 = validation.RawSha256,
                OriginalFilename = retrieval.SuggestedFilename,
                ConfigCount = validation.GridFile?.Configs.Count,
                PayloadSizeBytes = retrieval.RawJson.Length,
                RuntimeVersion = retrieval.RuntimeVersion,
                FinalUrl = retrieval.FinalUrl,
                Title = retrieval.Title,
                RawPayloadBytes = System.Text.Encoding.UTF8.GetBytes(retrieval.RawJson),
                ParsedGrid = validation.GridFile
            };

            await cacheService.SaveAsync(snapshot, retrieval.RawJson, cancellationToken);
            await loggingService.LogAsync(LogLevelKind.Information, "Official D2PT snapshot promoted to cache.", new
            {
                CaptureMethod = "WebView2 Download Event",
                retrieval.SuggestedFilename,
                DownloadedByteLength = snapshot.RawPayloadBytes?.Length ?? 0,
                snapshot.SourceName,
                snapshot.SourceStrategy,
                JsonParseSuccess = true,
                ParsedGridVersion = snapshot.ParsedGrid?.Version,
                ParsedGridConfigCount = snapshot.ParsedGrid?.Configs.Count,
                PreCanonicalSemanticHash = snapshot.Hash,
                snapshot.Hash,
                snapshot.RawSha256,
                RawPayloadBytes = snapshot.RawPayloadBytes?.Length ?? 0,
                ParsedGridPresent = snapshot.ParsedGrid is not null,
                snapshot.PatchLabel,
                snapshot.ConfigCount,
                snapshot.CapturedAt
            }, cancellationToken);

            return snapshot;
        }

        var cachedSnapshot = await cacheService.LoadAsync(cancellationToken);
        if (cachedSnapshot is not null)
        {
            await loggingService.LogAsync(LogLevelKind.Warning, "Official D2PT retrieval failed; preserving and using last valid cache.", new
            {
                retrieval.ProviderStatus,
                retrieval.Classification,
                retrieval.Message,
                cachedSnapshot.Hash,
                cachedSnapshot.RawSha256,
                cachedSnapshot.CapturedAt
            }, cancellationToken);

            return new HeroGridSnapshot
            {
                SourceName = cachedSnapshot.SourceName,
                ProviderName = cachedSnapshot.ProviderName,
                SourceStrategy = "CachedOfficialDownloadPayload",
                Preset = cachedSnapshot.Preset,
                PatchLabel = cachedSnapshot.PatchLabel,
                CapturedAt = cachedSnapshot.CapturedAt,
                Hash = cachedSnapshot.Hash,
                Layouts = cachedSnapshot.Layouts,
                RankBracket = cachedSnapshot.RankBracket,
                PeriodStart = cachedSnapshot.PeriodStart,
                PeriodEnd = cachedSnapshot.PeriodEnd,
                MinimumSampleMatches = cachedSnapshot.MinimumSampleMatches,
                UsesDerivedPositions = cachedSnapshot.UsesDerivedPositions,
                PositionDefinition = cachedSnapshot.PositionDefinition,
                RoleResults = cachedSnapshot.RoleResults,
                ProviderStatus = ProviderStatus.Cached,
                OriginKind = GridOriginKind.Cached,
                SourceDetails = $"Could not check Dota2ProTracker right now. The last valid official grid from {cachedSnapshot.CapturedAt:yyyy-MM-dd HH:mm} UTC is still available.",
                RawSha256 = cachedSnapshot.RawSha256,
                OriginalFilename = cachedSnapshot.OriginalFilename,
                ConfigCount = cachedSnapshot.ConfigCount,
                PayloadSizeBytes = cachedSnapshot.PayloadSizeBytes,
                RuntimeVersion = cachedSnapshot.RuntimeVersion,
                FinalUrl = cachedSnapshot.FinalUrl,
                Title = cachedSnapshot.Title,
                RawPayloadBytes = cachedSnapshot.RawPayloadBytes,
                ParsedGrid = cachedSnapshot.ParsedGrid
            };
        }

        throw new HeroGridProviderUnavailableException(retrieval.ProviderStatus, retrieval.Message);
    }
}
