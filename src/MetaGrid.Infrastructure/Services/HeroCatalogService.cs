using System.Text.Json;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class HeroCatalogService : IHeroCatalogService
{
    private IReadOnlyDictionary<string, HeroDefinition>? _cache;
    private IReadOnlyList<HeroDefinition>? _allHeroes;

    public async Task<IReadOnlyDictionary<string, HeroDefinition>> LoadByNameAsync(CancellationToken cancellationToken)
    {
        if (_cache is null)
        {
            await EnsureCacheAsync(cancellationToken);
        }

        return _cache!;
    }

    public async Task<IReadOnlyList<HeroDefinition>> LoadAllAsync(CancellationToken cancellationToken)
    {
        if (_allHeroes is null)
        {
            await EnsureCacheAsync(cancellationToken);
        }

        return _allHeroes!;
    }

    private async Task EnsureCacheAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null && _allHeroes is not null)
        {
            return;
        }

        var assetPath = Path.Combine(AppContext.BaseDirectory, "Assets", "heroes.json");
        await using var stream = File.OpenRead(assetPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var heroes = new List<HeroDefinition>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var item = property.Value;
            var internalName = item.GetProperty("name").GetString() ?? string.Empty;
            var localizedName = item.GetProperty("localized_name").GetString() ?? string.Empty;
            var slug = internalName.Replace("npc_dota_hero_", string.Empty, StringComparison.OrdinalIgnoreCase);

            heroes.Add(new HeroDefinition
            {
                Id = item.GetProperty("id").GetInt32(),
                InternalName = internalName,
                LocalizedName = localizedName,
                Slug = slug,
                PortraitPath = item.TryGetProperty("img", out var portrait) ? portrait.GetString() : null,
                IconPath = item.TryGetProperty("icon", out var icon) ? icon.GetString() : null
            });
        }

        _allHeroes = heroes
            .OrderBy(hero => hero.LocalizedName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _cache = _allHeroes.ToDictionary(hero => hero.LocalizedName, StringComparer.OrdinalIgnoreCase);
    }
}
