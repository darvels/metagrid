using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class GridComparisonService : IGridComparisonService
{
    public int CountChangedHeroes(HeroGridSnapshot current, HeroGridSnapshot previous)
    {
        var currentHeroes = current.Layouts
            .SelectMany(layout => layout.Categories)
            .SelectMany(category => category.HeroIds)
            .ToHashSet();

        var previousHeroes = previous.Layouts
            .SelectMany(layout => layout.Categories)
            .SelectMany(category => category.HeroIds)
            .ToHashSet();

        currentHeroes.SymmetricExceptWith(previousHeroes);
        return currentHeroes.Count;
    }
}
