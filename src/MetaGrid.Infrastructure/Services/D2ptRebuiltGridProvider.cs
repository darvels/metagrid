using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Utilities;

namespace MetaGrid.Infrastructure.Services;

public sealed partial class D2ptRebuiltGridProvider(
    IHeroCatalogService heroCatalogService,
    IDotaGridService dotaGridService,
    ILoggingService loggingService)
{
    private static readonly Uri MetaPageUri = new("https://dota2protracker.com/meta");
    private static readonly string[] Roles = ["Carry", "Mid", "Offlane", "Support", "Hard Support"];

    public async Task<HeroGridSnapshot> FetchAsync(HeroGridPreset preset, CancellationToken cancellationToken)
    {
        if (preset != HeroGridPreset.HighWinrate)
        {
            throw new HeroGridProviderUnavailableException(ProviderStatus.Unavailable, "The rebuilt D2PT provider is currently implemented only for the High Winrate preset.");
        }

        var html = await FetchMetaHtmlAsync(cancellationToken);
        var heroCatalog = await heroCatalogService.LoadByNameAsync(cancellationToken);
        var layouts = ParseLayouts(html, heroCatalog);
        var patch = ParsePatch(html);
        var hash = dotaGridService.ComputeSnapshotHash(new HeroGridSnapshot
        {
            SourceName = "Dota2ProTracker",
            ProviderName = "Dota2ProTracker",
            SourceStrategy = "D2PT Meta Rebuild",
            Preset = preset,
            PatchLabel = patch,
            CapturedAt = DateTimeOffset.UtcNow,
            Hash = string.Empty,
            Layouts = layouts,
            ProviderStatus = ProviderStatus.Online,
            OriginKind = GridOriginKind.RebuiltD2pt,
            SourceDetails = "Rebuilt from the public D2PT meta page by taking the rendered hero ordering per role."
        });

        return new HeroGridSnapshot
        {
            SourceName = "Dota2ProTracker",
            ProviderName = "Dota2ProTracker",
            SourceStrategy = "D2PT Meta Rebuild",
            Preset = preset,
            PatchLabel = patch,
            CapturedAt = DateTimeOffset.UtcNow,
            Hash = hash,
            Layouts = layouts,
            ProviderStatus = ProviderStatus.Online,
            OriginKind = GridOriginKind.RebuiltD2pt,
            SourceDetails = "Rebuilt from the public D2PT meta page by taking the rendered hero ordering per role."
        };
    }

    private async Task<string> FetchMetaHtmlAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MetaGrid", "1.0"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

        try
        {
            var response = await client.GetAsync(MetaPageUri, cancellationToken);
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            await loggingService.LogAsync(LogLevelKind.Information, "D2PT rebuilt provider HTTP result", new
            {
                StatusCode = (int)response.StatusCode,
                FinalUri = response.RequestMessage?.RequestUri?.ToString(),
                CloudflareDetected = IsCloudflarePage(response, html)
            }, cancellationToken);

            if (IsCloudflarePage(response, html))
            {
                throw new HeroGridProviderUnavailableException(ProviderStatus.CloudflareBlocked, "The public D2PT meta page is currently blocked by Cloudflare on this PC.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HeroGridProviderUnavailableException(ProviderStatus.Unavailable, $"The public D2PT meta page returned HTTP {(int)response.StatusCode}.");
            }

            return html;
        }
        catch (HeroGridProviderUnavailableException)
        {
            throw;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HeroGridProviderUnavailableException(ProviderStatus.NetworkUnavailable, $"The public D2PT meta page timed out: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            throw new HeroGridProviderUnavailableException(ProviderStatus.NetworkUnavailable, ex.Message);
        }
    }

    private static bool IsCloudflarePage(HttpResponseMessage response, string html)
    {
        return html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
               html.Contains("Enable JavaScript and cookies to continue", StringComparison.OrdinalIgnoreCase) ||
               response.Headers.TryGetValues("cf-mitigated", out var values) && values.Any(value => value.Contains("challenge", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<HeroGridLayout> ParseLayouts(string html, IReadOnlyDictionary<string, HeroDefinition> heroCatalog)
    {
        var categories = new List<HeroGridCategory>();
        foreach (var role in Roles)
        {
            var heroIds = ParseRoleHeroes(html, role, heroCatalog);
            if (heroIds.Count > 0)
            {
                categories.Add(new HeroGridCategory
                {
                    Name = role,
                    HeroIds = heroIds
                });
            }
        }

        if (categories.Count == 0)
        {
            throw new HeroGridProviderParseException("The public D2PT meta page was fetched, but MetaGrid could not parse role-based hero rankings from it.");
        }

        var allHeroes = categories.SelectMany(category => category.HeroIds).Distinct().ToList();
        categories.Add(new HeroGridCategory
        {
            Name = "All Heroes",
            HeroIds = allHeroes
        });

        return
        [
            new HeroGridLayout
            {
                Name = "Overview",
                Categories = categories
            }
        ];
    }

    private static List<int> ParseRoleHeroes(string html, string role, IReadOnlyDictionary<string, HeroDefinition> heroCatalog)
    {
        var index = html.IndexOf($">{role}<", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return [];
        }

        var nextIndexes = Roles
            .Where(otherRole => !string.Equals(otherRole, role, StringComparison.OrdinalIgnoreCase))
            .Select(otherRole => html.IndexOf($">{otherRole}<", index + role.Length, StringComparison.OrdinalIgnoreCase))
            .Where(next => next > index)
            .ToList();
        var endIndex = nextIndexes.Count > 0 ? nextIndexes.Min() : Math.Min(html.Length, index + 12000);
        var section = html[index..endIndex];

        var heroes = new List<int>();
        foreach (Match match in HeroNameRegex().Matches(section))
        {
            var heroName = System.Net.WebUtility.HtmlDecode(match.Groups["name"].Value.Trim());
            if (!heroCatalog.TryGetValue(heroName, out var hero))
            {
                continue;
            }

            if (!heroes.Contains(hero.Id))
            {
                heroes.Add(hero.Id);
            }

            if (heroes.Count >= 12)
            {
                break;
            }
        }

        return heroes;
    }

    private static string ParsePatch(string html)
    {
        var match = PatchRegex().Match(html);
        return match.Success ? match.Groups["patch"].Value.Trim() : "Unknown Patch";
    }

    [GeneratedRegex(">(?<name>[A-Za-z'\\-\\s]+)</a>", RegexOptions.IgnoreCase)]
    private static partial Regex HeroNameRegex();

    [GeneratedRegex("Current Patch\\s*(?<patch>[^<\\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex PatchRegex();
}
