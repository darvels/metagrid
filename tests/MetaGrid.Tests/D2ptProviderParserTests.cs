using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;

namespace MetaGrid.Tests;

public sealed class D2ptProviderParserTests
{
    private static readonly IReadOnlyDictionary<string, HeroDefinition> HeroCatalog = new Dictionary<string, HeroDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        ["Axe"] = new() { Id = 2, InternalName = "npc_dota_hero_axe", LocalizedName = "Axe", Slug = "axe" },
        ["Juggernaut"] = new() { Id = 8, InternalName = "npc_dota_hero_juggernaut", LocalizedName = "Juggernaut", Slug = "juggernaut" },
        ["Crystal Maiden"] = new() { Id = 5, InternalName = "npc_dota_hero_crystal_maiden", LocalizedName = "Crystal Maiden", Slug = "crystal-maiden" }
    };

    [Fact]
    public void Parse_ReturnsLayouts_ForExpectedMarkup()
    {
        var html = """
        Last update: Aug 17, 2026 • Patch 7.41e
        Most played heroes with >50% winrate by role. Balanced approach focusing on both popularity and success rate.
        <h3>Carry</h3>
        <img alt="Juggernaut" />
        <h3>Support</h3>
        <img alt="Crystal Maiden" />
        <h3>All Heroes</h3>
        <img alt="Juggernaut" />
        <img alt="Crystal Maiden" />
        Download Hero Grid Configuration
        """;

        var result = D2ptProviderParser.Parse(html, HeroGridPreset.HighWinrate, HeroCatalog);

        Assert.Equal(ProviderStatus.Online, result.Status);
        Assert.NotEmpty(result.Layouts);
        Assert.Equal("7.41e", result.PatchLabel);
    }

    [Fact]
    public void Parse_ReturnsCloudflareBlocked_ForChallengePage()
    {
        var html = "<html><title>Just a moment...</title><span>Enable JavaScript and cookies to continue</span></html>";
        var result = D2ptProviderParser.Parse(html, HeroGridPreset.HighWinrate, HeroCatalog);
        Assert.Equal(ProviderStatus.CloudflareBlocked, result.Status);
    }

    [Fact]
    public void Parse_ReturnsParsingFailed_ForUnexpectedMarkup()
    {
        var html = "<html><body><h1>No roles here</h1></body></html>";
        var result = D2ptProviderParser.Parse(html, HeroGridPreset.HighWinrate, HeroCatalog);
        Assert.Equal(ProviderStatus.ParsingFailed, result.Status);
    }

    [Fact]
    public void Parse_IgnoresUnknownHeroes_AndDeduplicates()
    {
        var html = """
        Last update: Aug 17, 2026 • Patch 7.41e
        Most played heroes with >50% winrate by role. Balanced approach focusing on both popularity and success rate.
        <h3>Carry</h3>
        <img alt="Juggernaut" />
        <img alt="Juggernaut" />
        <img alt="Unknown Hero" />
        <h3>All Heroes</h3>
        <img alt="Juggernaut" />
        Download Hero Grid Configuration
        """;

        var result = D2ptProviderParser.Parse(html, HeroGridPreset.HighWinrate, HeroCatalog);
        var carryCategories = result.Layouts.SelectMany(x => x.Categories).Where(x => x.Name == "Carry").ToList();
        Assert.NotEmpty(carryCategories);
        Assert.All(carryCategories, category => Assert.Single(category.HeroIds));
    }

    [Fact]
    public void ExtractMatchesWr_ReturnsGrid_FromSvelteKitHydrationPayload()
    {
        var html = """
        <html>
        <body>
        <script>
        window.__sveltekit_meta = {
          data: [
            {
              grids: {
                matches_wr: {
                  version: 3,
                  configs: [
                    {
                      config_name: 'Overview',
                      categories: [
                        { category_name: 'Carry', hero_ids: [8], x_position: .5, y_position: 0, width: 1, height: 1 },
                        { category_name: 'All Heroes', hero_ids: [8, 5], x_position: 1, y_position: 0, width: 1, height: 1 }
                      ]
                    }
                  ]
                }
              },
              availableModes: [{ id: 'matches_wr' }, { id: 'matches' }],
              lastUpdated: '2026-08-16T18:02:12',
              version: '7.41e'
            }
          ]
        };
        </script>
        </body>
        </html>
        """;

        var result = D2ptPageDataExtractor.ExtractMatchesWr(html);

        Assert.True(result.Success);
        Assert.Equal("ExtractionSuccess", result.Classification);
        Assert.Equal("7.41e", result.PatchLabel);
        Assert.Equal("2026-08-16T18:02:12", result.LastUpdated);
        Assert.Contains("matches_wr", result.AvailableModes);
        Assert.NotNull(result.ExtractedGrid);
        Assert.Equal(3, result.ExtractedGrid!.Version);
        Assert.Single(result.ExtractedGrid.Configs);
    }

    [Fact]
    public void ExtractMatchesWr_ReturnsCloudflareChallenge_WhenChallengeMarkupReturned()
    {
        var html = "<html><title>Just a moment...</title><span>Enable JavaScript and cookies to continue</span></html>";

        var result = D2ptPageDataExtractor.ExtractMatchesWr(html);

        Assert.False(result.Success);
        Assert.Equal("CloudflareChallenge", result.Classification);
    }
}
