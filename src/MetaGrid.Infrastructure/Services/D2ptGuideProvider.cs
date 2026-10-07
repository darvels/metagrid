using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Utilities;

namespace MetaGrid.Infrastructure.Services;

public sealed class D2ptGuideProvider(ILoggingService loggingService, ID2ptGuidePageCapture capture, IAppPaths paths) : ID2ptGuideProvider
{
    private static readonly Uri IoMidBuildUri = new("https://dota2protracker.com/builds");
    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private string? _indexHtml;
    private DateTimeOffset _indexRetrieved;
    private Exception? _captureFailure;
    private DateTimeOffset _captureFailureAt;
    private readonly Dictionary<string, (string Html, DateTimeOffset Retrieved)> _openingPages = [];

    public async Task<NormalizedHeroGuideBuild> FetchAsync(GuideSubscriptionRecord subscription, CancellationToken cancellationToken)
    {
        var probe = _indexHtml is null && DateTimeOffset.UtcNow - _captureFailureAt > TimeSpan.FromSeconds(30)
            ? await ProbeLivePageAsync(cancellationToken) : null;
        await loggingService.LogAsync(LogLevelKind.Information, "Guide provider probe completed.", new
        {
            Hero = subscription.HeroInternalName,
            subscription.Role,
            StatusCode = probe?.StatusCode,
            BlockedByCloudflare = probe?.BlockedByCloudflare,
            FinalUrl = probe?.FinalUrl,
            Error = probe?.Error
        }, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var cachePath = Path.Combine(paths.GuideCacheDirectory, $"d2pt-{subscription.HeroId}-{subscription.Role.ToString().ToLowerInvariant()}-v4.json");
        try
        {
            await _indexGate.WaitAsync(cancellationToken);
            NormalizedHeroGuideBuild build;
            try
            {
                if (_indexHtml is null || DateTimeOffset.UtcNow - _indexRetrieved > TimeSpan.FromMinutes(15))
                {
                    if (_captureFailure is not null && DateTimeOffset.UtcNow - _captureFailureAt < TimeSpan.FromSeconds(30))
                        throw new InvalidOperationException("D2PT index retrieval is in a brief failure cooldown; no repeated browser launch.", _captureFailure);
                    try
                    {
                        var html = await capture.CaptureIndexAsync(cancellationToken);
                        if (D2ptPageDataExtractor.LooksLikeCloudflareChallenge(html))
                            throw new InvalidOperationException("D2PT public build index is blocked by Cloudflare.");
                        using var validated = JsonDocument.Parse(D2ptPageDataExtractor.ExtractHydrationJson(html));
                        _indexHtml = html;
                        _indexRetrieved = DateTimeOffset.UtcNow;
                        _captureFailure = null;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _captureFailure = ex;
                        _captureFailureAt = DateTimeOffset.UtcNow;
                        throw;
                    }
                }
                build = D2ptLiveGuideParser.ParseIndex(_indexHtml, subscription.HeroId, subscription.Role);
                using var index = JsonDocument.Parse(D2ptPageDataExtractor.ExtractHydrationJson(_indexHtml));
                var hero = index.RootElement[0].GetProperty("data").GetProperty("heroesMapping").GetProperty(subscription.HeroId.ToString());
                var heroName = hero.GetProperty("displayName").GetString()!;
                var key = subscription.StableKey;
                if (!_openingPages.TryGetValue(key, out var opening) || DateTimeOffset.UtcNow - opening.Retrieved > TimeSpan.FromMinutes(15))
                {
                    var inventoryHtml = await capture.CaptureStartingInventoriesAsync(heroName, subscription.Role, cancellationToken);
                    D2ptLiveGuideParser.ApplyExactStartingInventories(build, inventoryHtml);
                    opening = (inventoryHtml, DateTimeOffset.UtcNow);
                    _openingPages[key] = opening;
                }
                else D2ptLiveGuideParser.ApplyExactStartingInventories(build, opening.Html);
                build.RetrievedAtUtc = _indexRetrieved;
            }
            finally { _indexGate.Release(); }
            Directory.CreateDirectory(paths.GuideCacheDirectory);
            var temporary = cachePath + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(build, JsonDefaults.Storage), cancellationToken);
            File.Move(temporary, cachePath, true);
            await loggingService.LogAsync(LogLevelKind.Information, "Live D2PT guide parsed; mapping validation is still required.",
                new { build.SourceClassification, build.Matches, build.WinRate, build.CanonicalSourceHash,
                    Skills = build.SkillOrder.Count, Talents = build.TalentChoices.Count, build.StartingItemsVersion,
                    StartingStrategy = "Public role page exact starting_inventory_options",
                    StartingVariants = build.StartingInventories.Count,
                    SelectedOpening = D2ptLiveGuideParser.SelectStartingInventory(build.StartingInventories) }, cancellationToken);
            return build;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (D2ptNoGuideBuildException) { throw; }
        catch (GuideSourceDataException) { throw; }
        catch (Exception ex)
        {
            await loggingService.LogAsync(LogLevelKind.Warning, "Live D2PT guide capture failed.", new { ex.Message }, cancellationToken);
            if (File.Exists(cachePath))
            {
                var cached = JsonSerializer.Deserialize<NormalizedHeroGuideBuild>(await File.ReadAllTextAsync(cachePath, cancellationToken), JsonDefaults.Storage);
                if (cached is not null && cached.SourceClassification == "Live" && cached.SchemaVersion == 1
                    && D2ptLiveGuideParser.HasCurrentStartingInventory(cached)
                    && cached.HeroId == subscription.HeroId && cached.Role == subscription.Role && cached.HeroInternalName == subscription.HeroInternalName
                    && (cached.TalentChoices.Count == 0 || cached.RawTalentCandidates.Count > 0)
                    && cached.RetrievedAtUtc <= DateTimeOffset.UtcNow
                    && DateTimeOffset.UtcNow - cached.RetrievedAtUtc <= TimeSpan.FromHours(24)
                    && cached.CanonicalSourceHash == ComputeCanonicalSourceHash(cached))
                {
                    cached.SourceClassification = "Cached live";
                    return cached;
                }
            }
            throw new InvalidOperationException("D2PT unavailable: no valid live guide or recent live cache. Installed guide unchanged.", ex);
        }
    }

    internal static string ComputeCanonicalSourceHash(NormalizedHeroGuideBuild build)
    {
        var normalized = string.Join("|", new[]
        {
            build.HeroId.ToString(),
            build.HeroInternalName.Trim(),
            build.Role.ToString(),
            build.Source.Trim(),
            build.SourceBuildId.Trim(),
            build.SourceTitle.Trim(),
            build.GameplayVersion.Trim(),
            build.WinRate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            build.Matches.ToString(),
            string.Join(";", build.ItemGroups.Select(group =>
                $"{group.CategoryKey.Trim()}={string.Join(",", group.ItemIds.Select(item => item.Trim()))}")),
            string.Join(",", build.SkillOrder.Select(skill => skill.Trim())),
            string.Join(";", build.TalentChoices
                .OrderBy(choice => choice.Level)
                .Select(choice => $"{choice.Level}:{choice.TalentId.Trim()}:{choice.SelectedLabel.Trim()}"))
        });

        if (build.SkillCandidates.Count > 0)
            normalized += "|candidates=" + JsonSerializer.Serialize(build.SkillCandidates);
        if (build.RawTalentCandidates.Count > 0)
            normalized += "|talentEvidence=" + JsonSerializer.Serialize(build.RawTalentCandidates);
        if (build.StartingItemsVersion > 0)
            normalized += "|startingItemsVersion=" + build.StartingItemsVersion + "|startingEvidence=" + JsonSerializer.Serialize(build.StartingInventories);
        return HashUtilities.Sha256(normalized);
    }

    private static async Task<GuidePageProbeResult> ProbeLivePageAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient(new HttpClientHandler
        {
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        })
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MetaGrid", "0.2.0"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

        try
        {
            using var response = await client.GetAsync(IoMidBuildUri, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var blocked = body.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
                          || body.Contains("Enable JavaScript and cookies to continue", StringComparison.OrdinalIgnoreCase)
                          || response.Headers.TryGetValues("cf-mitigated", out var values) && values.Any(value => value.Contains("challenge", StringComparison.OrdinalIgnoreCase));
            return new GuidePageProbeResult((int)response.StatusCode, response.RequestMessage?.RequestUri?.ToString(), blocked, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new GuidePageProbeResult(null, IoMidBuildUri.ToString(), false, ex.Message);
        }
    }

    private sealed record GuidePageProbeResult(int? StatusCode, string? FinalUrl, bool BlockedByCloudflare, string? Error);
}
