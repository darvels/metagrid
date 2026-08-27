using System.Text.Json;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class HeroCatalogService : IHeroCatalogService
{
    private IReadOnlyDictionary<string, HeroDefinition>? _cache;

    public async Task<IReadOnlyDictionary<string, HeroDefinition>> LoadByNameAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        var assetPath = Path.Combine(AppContext.BaseDirectory, "Assets", "heroes.json");
        await using var stream = File.OpenRead(assetPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var dict = new Dictionary<string, HeroDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var item = property.Value;
            var internalName = item.GetProperty("name").GetString() ?? string.Empty;
            var localizedName = item.GetProperty("localized_name").GetString() ?? string.Empty;
            var slug = internalName.Replace("npc_dota_hero_", string.Empty, StringComparison.OrdinalIgnoreCase);

            var hero = new HeroDefinition
            {
                Id = item.GetProperty("id").GetInt32(),
                InternalName = internalName,
                LocalizedName = localizedName,
                Slug = slug
            };

            dict[localizedName] = hero;
        }

        _cache = dict;
        return _cache;
    }
}
