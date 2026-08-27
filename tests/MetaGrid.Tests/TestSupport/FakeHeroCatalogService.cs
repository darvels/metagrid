using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Tests.TestSupport;

internal sealed class FakeHeroCatalogService : IHeroCatalogService
{
    private readonly IReadOnlyDictionary<string, HeroDefinition> _heroes;

    public FakeHeroCatalogService(int maxHeroId = 300)
    {
        var dict = new Dictionary<string, HeroDefinition>(StringComparer.OrdinalIgnoreCase);
        for (var id = 1; id <= maxHeroId; id++)
        {
            dict[$"Hero {id}"] = new HeroDefinition
            {
                Id = id,
                InternalName = $"npc_dota_hero_test_{id}",
                LocalizedName = $"Hero {id}",
                Slug = $"hero-{id}"
            };
        }

        _heroes = dict;
    }

    public Task<IReadOnlyDictionary<string, HeroDefinition>> LoadByNameAsync(CancellationToken cancellationToken)
        => Task.FromResult(_heroes);
}
