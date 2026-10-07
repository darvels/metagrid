using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Tests.TestSupport;

internal sealed class FakeHeroCatalogService : IHeroCatalogService
{
    private readonly IReadOnlyDictionary<string, HeroDefinition> _heroes;
    private readonly IReadOnlyList<HeroDefinition> _allHeroes;

    public FakeHeroCatalogService(int maxHeroId = 300)
    {
        var dict = new Dictionary<string, HeroDefinition>(StringComparer.OrdinalIgnoreCase);
        var allHeroes = new List<HeroDefinition>();
        for (var id = 1; id <= maxHeroId; id++)
        {
            var hero = new HeroDefinition
            {
                Id = id,
                InternalName = $"npc_dota_hero_test_{id}",
                LocalizedName = $"Hero {id}",
                Slug = $"hero-{id}",
                PortraitPath = $"/heroes/{id}.png",
                IconPath = $"/icons/{id}.png"
            };

            dict[$"Hero {id}"] = hero;
            allHeroes.Add(hero);
        }

        _heroes = dict;
        _allHeroes = allHeroes;
    }

    public Task<IReadOnlyDictionary<string, HeroDefinition>> LoadByNameAsync(CancellationToken cancellationToken)
        => Task.FromResult(_heroes);

    public Task<IReadOnlyList<HeroDefinition>> LoadAllAsync(CancellationToken cancellationToken)
        => Task.FromResult(_allHeroes);
}
