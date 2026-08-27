using System.Net;
using System.Text.RegularExpressions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public static partial class D2ptProviderParser
{
    private static readonly IReadOnlyDictionary<HeroGridPreset, string> PresetDescriptions = new Dictionary<HeroGridPreset, string>
    {
        [HeroGridPreset.MostPlayed] = "Top 7 most picked heroes by role in High MMR Pub Matches. Based on pick frequency in the current patch.",
        [HeroGridPreset.HighWinrate] = "Most played heroes with >50% winrate by role. Balanced approach focusing on both popularity and success rate.",
        [HeroGridPreset.D2ptRating] = "Top heroes ranked by D2PT's Rating System. Advanced algorithm considering multiple performance metrics."
    };

    public static D2ptParseResult Parse(string html, HeroGridPreset preset, IReadOnlyDictionary<string, HeroDefinition> heroCatalog)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return new D2ptParseResult(ProviderStatus.UnexpectedResponse, "Unknown Patch", [], "The D2PT response body was empty.");
        }

        if (html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("Enable JavaScript and cookies to continue", StringComparison.OrdinalIgnoreCase))
        {
            return new D2ptParseResult(ProviderStatus.CloudflareBlocked, "Unknown Patch", [], "Cloudflare challenge page was returned instead of hero-grid content.");
        }

        var patchMatch = PatchRegex().Match(html);
        var patchLabel = patchMatch.Success ? patchMatch.Groups["patch"].Value.Trim() : "Unknown Patch";
        var selectedSection = SelectPresetSection(html, preset);

        var roleOrder = new[] { "Carry", "Mid", "Offlane", "Support", "Hard Support", "All Heroes" };
        var parsedCategories = new List<HeroGridCategory>();
        foreach (var role in roleOrder)
        {
            var heroes = ParseRoleHeroes(selectedSection, role, heroCatalog);
            if (heroes.Count > 0)
            {
                parsedCategories.Add(new HeroGridCategory
                {
                    Name = role,
                    HeroIds = heroes
                });
            }
        }

        if (parsedCategories.Count == 0)
        {
            return new D2ptParseResult(ProviderStatus.ParsingFailed, patchLabel, [], "No hero roles could be parsed from the current D2PT response.");
        }

        var allHeroes = parsedCategories.FirstOrDefault(category => category.Name == "All Heroes")
                       ?? new HeroGridCategory { Name = "All Heroes", HeroIds = [] };
        var roleCategories = parsedCategories.Where(category => category.Name != "All Heroes").ToList();

        var layouts = new List<HeroGridLayout>
        {
            new()
            {
                Name = "Overview",
                Categories = parsedCategories
            }
        };

        layouts.AddRange(roleCategories.Select(role => new HeroGridLayout
        {
            Name = role.Name,
            Categories = allHeroes.HeroIds.Count > 0
                ? new[] { role, allHeroes }
                : new[] { role }
        }));

        return new D2ptParseResult(ProviderStatus.Online, patchLabel, layouts, $"Parsed {layouts.Count} layouts from D2PT.");
    }

    private static string SelectPresetSection(string html, HeroGridPreset preset)
    {
        if (!PresetDescriptions.TryGetValue(preset, out var description))
        {
            return html;
        }

        var startIndex = html.IndexOf(description, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return html;
        }

        var nextSectionIndex = html.IndexOf("### ", startIndex + description.Length, StringComparison.OrdinalIgnoreCase);
        if (nextSectionIndex < 0)
        {
            nextSectionIndex = html.Length;
        }

        return html[startIndex..nextSectionIndex];
    }

    private static List<int> ParseRoleHeroes(string html, string role, IReadOnlyDictionary<string, HeroDefinition> heroes)
    {
        var headingMatch = Regex.Match(
            html,
            $@"<h3\b[^>]*>\s*{Regex.Escape(role)}\s*</h3>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var startIndex = headingMatch.Success ? headingMatch.Index : -1;

        if (startIndex < 0)
        {
            var textMarker = $"heading \"{role}\"";
            startIndex = html.IndexOf(textMarker, StringComparison.OrdinalIgnoreCase);
        }

        if (startIndex < 0)
        {
            return [];
        }

        var endIndex = html.IndexOf("<h3>", startIndex + 4, StringComparison.OrdinalIgnoreCase);
        if (endIndex < 0)
        {
            endIndex = html.IndexOf("Download Hero Grid Configuration", startIndex, StringComparison.OrdinalIgnoreCase);
        }

        if (endIndex < 0)
        {
            endIndex = Math.Min(html.Length, startIndex + 5000);
        }

        var section = html[startIndex..endIndex];
        var heroNames = ImgAltRegex().Matches(section)
            .Select(match => WebUtility.HtmlDecode(match.Groups["name"].Value.Trim()))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return heroNames
            .Where(heroes.ContainsKey)
            .Select(name => heroes[name].Id)
            .ToList();
    }

    [GeneratedRegex("Last update:\\s*(?<date>[A-Za-z]{3}\\s+\\d{1,2},\\s+\\d{4})\\s*(?:\\u2022|-)\\s*Patch\\s*(?<patch>[^<\\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex PatchRegex();

    [GeneratedRegex("alt=\"(?<name>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ImgAltRegex();
}
