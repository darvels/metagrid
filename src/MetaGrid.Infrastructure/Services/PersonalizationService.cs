using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class PersonalizationService(
    IPlayerProfileInputParser profileInputParser,
    IOpenDotaClient openDotaClient,
    IPersonalHeroSelector personalHeroSelector,
    IPersonalHeroCacheService personalHeroCacheService,
    IAppClock appClock,
    ILoggingService loggingService) : IPersonalizationService
{
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromHours(6);

    public ProfileInputParseResult ParseProfileInput(string? input) => profileInputParser.Parse(input);

    public async Task<PersonalizationResolution> ConnectAsync(string profileInput, CancellationToken cancellationToken)
    {
        var parseResult = profileInputParser.Parse(profileInput);
        if (!parseResult.Succeeded || string.IsNullOrWhiteSpace(parseResult.AccountId))
        {
            return new PersonalizationResolution
            {
                Status = PersonalizationStatus.InvalidInput,
                SourceMode = PersonalizationAccountSourceMode.ManualAccount,
                Message = parseResult.Error ?? "Invalid player profile input."
            };
        }

        var resolution = await FetchCurrentAsync(
            parseResult.AccountId,
            displayName: null,
            sourceMode: PersonalizationAccountSourceMode.ManualAccount,
            forceRefresh: true,
            allowCacheFallback: true,
            cancellationToken);
        return PersonalizationResolutionExtensions.WithAccount(resolution, parseResult.AccountId, PersonalizationAccountSourceMode.ManualAccount);
    }

    public Task DisconnectAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(settings.PersonalizationAccountId))
        {
            return personalHeroCacheService.DeleteAsync(settings.PersonalizationAccountId, cancellationToken);
        }

        return Task.CompletedTask;
    }

    public async Task<PersonalizationResolution> ResolveAsync(AppSettings settings, PersonalizationAccountContext accountContext, bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!settings.PersonalizationEnabled)
        {
            return new PersonalizationResolution
            {
                Status = PersonalizationStatus.Disabled,
                SourceMode = accountContext.SourceMode,
                Message = "Personal heroes are disabled."
            };
        }

        if (!accountContext.HasAccount)
        {
            return new PersonalizationResolution
            {
                Status = accountContext.SourceMode == PersonalizationAccountSourceMode.ManualAccount
                    ? PersonalizationStatus.InvalidInput
                    : PersonalizationStatus.ProfileUnavailable,
                SourceMode = accountContext.SourceMode,
                Message = accountContext.SourceMode == PersonalizationAccountSourceMode.ManualAccount
                    ? "Enter an OpenDota account ID or player URL to enable personal heroes."
                    : "Select a Steam account to enable personal heroes."
            };
        }

        return await FetchCurrentAsync(accountContext.AccountId!, accountContext.DisplayName, accountContext.SourceMode, forceRefresh, allowCacheFallback: true, cancellationToken);
    }

    public async Task<PersonalizationResolution> RefreshAsync(AppSettings settings, PersonalizationAccountContext accountContext, CancellationToken cancellationToken)
    {
        if (!accountContext.HasAccount)
        {
            return new PersonalizationResolution
            {
                Status = accountContext.SourceMode == PersonalizationAccountSourceMode.ManualAccount
                    ? PersonalizationStatus.InvalidInput
                    : PersonalizationStatus.ProfileUnavailable,
                SourceMode = accountContext.SourceMode,
                Message = accountContext.SourceMode == PersonalizationAccountSourceMode.ManualAccount
                    ? "Enter an OpenDota account ID or player URL before refreshing personal heroes."
                    : "Select a Steam account before refreshing personal heroes."
            };
        }

        return await FetchCurrentAsync(accountContext.AccountId!, accountContext.DisplayName, accountContext.SourceMode, forceRefresh: true, allowCacheFallback: true, cancellationToken);
    }

    private async Task<PersonalizationResolution> FetchCurrentAsync(
        string accountId,
        string? displayName,
        PersonalizationAccountSourceMode sourceMode,
        bool forceRefresh,
        bool allowCacheFallback,
        CancellationToken cancellationToken)
    {
        var cache = await personalHeroCacheService.LoadAsync(accountId, cancellationToken);
        if (!forceRefresh && IsReusableCurrentCache(cache) && appClock.Now - cache!.FetchedAt < FreshnessWindow)
        {
            return FromCache(cache!, statusOverride: cache!.Status == PersonalizationStatus.Ready ? PersonalizationStatus.Ready : cache.Status, usedCache: true, isFresh: true, sourceMode);
        }

        try
        {
            var profile = await openDotaClient.GetPlayerProfileAsync(accountId, cancellationToken);
            var heroes = await openDotaClient.GetPlayerHeroesAsync(accountId, cancellationToken);
            var fetchedAt = appClock.Now;

            var hasUsableRows = heroes.Any(row => row.Games > 0 || row.LastPlayedUnixSeconds > 0);
            if (profile.IsProfileUnavailable || !hasUsableRows)
            {
                await loggingService.LogAsync(LogLevelKind.Warning, "OpenDota profile is unavailable or unusable for personalization.", new
                {
                    accountId,
                    profile.IsProfileUnavailable,
                    HeroRowCount = heroes.Count
                }, cancellationToken);

                if (allowCacheFallback && IsReusableCurrentCache(cache))
                {
                    return FromCache(cache!, statusOverride: cache!.SelectedHeroes.Count > 0 ? PersonalizationStatus.Cached : cache.Status, usedCache: true, isFresh: false, sourceMode);
                }

                return new PersonalizationResolution
                {
                    Status = PersonalizationStatus.ProfileUnavailable,
                    SourceMode = sourceMode,
                    AccountId = accountId,
                    DisplayName = profile.PersonaName ?? displayName,
                    Message = "Profile private or match data unavailable."
                };
            }

            var resolvedDisplayName = profile.PersonaName ?? displayName;
            var selection = await personalHeroSelector.SelectAsync(accountId, resolvedDisplayName, heroes, fetchedAt, cancellationToken);
            var status = selection.SelectedHeroes.Count == 0 ? PersonalizationStatus.NoQualifyingHeroes : PersonalizationStatus.Ready;
            var cacheEntry = new PersonalizationCacheEntry
            {
                AccountId = selection.AccountId,
                DisplayName = selection.DisplayName,
                FetchedAt = selection.FetchedAt,
                RuleVersion = selection.RuleVersion,
                WindowDays = selection.WindowDays,
                MinGamesInclusive = selection.MinGamesInclusive,
                MinWinRateInclusive = selection.MinWinRateInclusive,
                MaxHeroes = selection.MaxHeroes,
                Status = status,
                SelectedHeroes = selection.SelectedHeroes
            };
            await personalHeroCacheService.SaveAsync(cacheEntry, cancellationToken);

            return new PersonalizationResolution
            {
                Status = status,
                SourceMode = sourceMode,
                AccountId = accountId,
                DisplayName = resolvedDisplayName,
                FetchedAt = fetchedAt,
                Message = status == PersonalizationStatus.Ready
                    ? "Personal heroes ready."
                    : "No qualifying heroes in last 90 days.",
                Selection = selection,
                IsFresh = true
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await loggingService.LogAsync(LogLevelKind.Warning, "OpenDota personalization refresh failed.", new
            {
                accountId,
                ex.Message
            }, cancellationToken);

            if (allowCacheFallback && IsReusableCurrentCache(cache))
            {
                return FromCache(cache!, statusOverride: cache!.SelectedHeroes.Count > 0 ? PersonalizationStatus.Cached : cache.Status, usedCache: true, isFresh: false, sourceMode);
            }

            return new PersonalizationResolution
            {
                Status = PersonalizationStatus.OpenDotaUnavailable,
                SourceMode = sourceMode,
                AccountId = accountId,
                DisplayName = displayName,
                Message = "OpenDota unavailable."
            };
        }
    }

    private static bool IsReusableCurrentCache(PersonalizationCacheEntry? cache)
        => cache is not null && cache.UsesCurrentRules();

    private static PersonalizationResolution FromCache(PersonalizationCacheEntry cache, PersonalizationStatus statusOverride, bool usedCache, bool isFresh, PersonalizationAccountSourceMode sourceMode)
        => new()
        {
            Status = statusOverride,
            SourceMode = sourceMode,
            AccountId = cache.AccountId,
            DisplayName = cache.DisplayName,
            FetchedAt = cache.FetchedAt,
            UsedCache = usedCache,
            IsFresh = isFresh,
            Message = statusOverride switch
            {
                PersonalizationStatus.Ready => "Personal heroes ready.",
                PersonalizationStatus.NoQualifyingHeroes => "No qualifying heroes in the last 90 days.",
                _ => "Using cached personal heroes."
            },
            Selection = new PersonalHeroSelection
            {
                AccountId = cache.AccountId,
                DisplayName = cache.DisplayName,
                FetchedAt = cache.FetchedAt,
                RuleVersion = cache.RuleVersion,
                WindowDays = cache.WindowDays,
                MinGamesInclusive = cache.MinGamesInclusive,
                MinWinRateInclusive = cache.MinWinRateInclusive,
                MaxHeroes = cache.MaxHeroes,
                SelectedHeroes = cache.SelectedHeroes
            }
        };
}

internal static class PersonalizationResolutionExtensions
{
    public static PersonalizationResolution WithAccount(this PersonalizationResolution resolution, string accountId, PersonalizationAccountSourceMode sourceMode)
        => new()
        {
            Status = resolution.Status,
            SourceMode = sourceMode,
            Message = resolution.Message,
            AccountId = accountId,
            DisplayName = resolution.DisplayName,
            FetchedAt = resolution.FetchedAt,
            UsedCache = resolution.UsedCache,
            IsFresh = resolution.IsFresh,
            Selection = resolution.Selection
        };
}
