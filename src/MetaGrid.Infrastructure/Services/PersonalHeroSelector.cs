using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class PersonalHeroSelector(IHeroCatalogService heroCatalogService) : IPersonalHeroSelector
{
    public async Task<PersonalHeroSelection> SelectAsync(string accountId, string? displayName, IReadOnlyList<OpenDotaPlayerHeroStats> heroStats, DateTimeOffset fetchedAt, CancellationToken cancellationToken)
    {
        var heroNames = (await heroCatalogService.LoadByNameAsync(cancellationToken))
            .Values
            .GroupBy(hero => hero.Id)
            .ToDictionary(group => group.Key, group => group.First().LocalizedName);

        var selected = heroStats
            .Where(row => row.Games >= PersonalHeroSelection.CurrentMinGamesInclusive)
            .Select(row => new PersonalHeroRecord
            {
                HeroId = row.HeroId,
                HeroName = heroNames.TryGetValue(row.HeroId, out var name) ? name : $"Hero {row.HeroId}",
                Games = row.Games,
                Wins = row.Wins,
                WinRate = row.Games == 0 ? 0d : row.Wins / (double)row.Games
            })
            .Where(row => row.WinRate >= PersonalHeroSelection.CurrentMinWinRateInclusive)
            .OrderByDescending(row => row.WinRate)
            .ThenByDescending(row => row.Games)
            .ThenBy(row => row.HeroId)
            .Take(PersonalHeroSelection.CurrentMaxHeroes)
            .ToArray();

        return new PersonalHeroSelection
        {
            AccountId = accountId,
            DisplayName = displayName,
            FetchedAt = fetchedAt,
            RuleVersion = PersonalHeroSelection.CurrentRuleVersion,
            WindowDays = PersonalHeroSelection.CurrentWindowDays,
            MinGamesInclusive = PersonalHeroSelection.CurrentMinGamesInclusive,
            MinWinRateInclusive = PersonalHeroSelection.CurrentMinWinRateInclusive,
            MaxHeroes = PersonalHeroSelection.CurrentMaxHeroes,
            SelectedHeroes = selected
        };
    }
}
