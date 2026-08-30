using System.Text.Json.Serialization;

namespace MetaGrid.Core.Models;

public enum PersonalizationAccountSourceMode
{
    SelectedSteamAccount,
    ManualAccount
}

public enum PersonalizationStatus
{
    Disabled,
    Ready,
    NoQualifyingHeroes,
    ProfileUnavailable,
    Cached,
    OpenDotaUnavailable,
    InvalidInput
}

public sealed class PersonalHeroRecord
{
    public required int HeroId { get; init; }
    public string HeroName { get; init; } = string.Empty;
    public required int Games { get; init; }
    public required int Wins { get; init; }
    public required double WinRate { get; init; }
}

public sealed class PersonalHeroSelection
{
    public const string CurrentRuleVersion = "opendota-90d-v2-inclusive";
    public const int CurrentWindowDays = 90;
    public const int CurrentMinGamesInclusive = 10;
    public const double CurrentMinWinRateInclusive = 0.53d;
    public const int CurrentMaxHeroes = 7;

    private int _minGamesInclusive = CurrentMinGamesInclusive;
    private double _minWinRateInclusive = CurrentMinWinRateInclusive;

    public required string AccountId { get; init; }
    public string? DisplayName { get; init; }
    public required DateTimeOffset FetchedAt { get; init; }
    public string RuleVersion { get; init; } = CurrentRuleVersion;
    public int WindowDays { get; init; } = CurrentWindowDays;
    public int MinGamesInclusive
    {
        get => _minGamesInclusive;
        init => _minGamesInclusive = value;
    }

    public double MinWinRateInclusive
    {
        get => _minWinRateInclusive;
        init => _minWinRateInclusive = value;
    }

    [JsonPropertyName("minGamesExclusive")]
    public int? LegacyMinGamesExclusive
    {
        init
        {
            if (value.HasValue)
            {
                _minGamesInclusive = value.Value;
            }
        }
    }

    [JsonPropertyName("minWinRateExclusive")]
    public double? LegacyMinWinRateExclusive
    {
        init
        {
            if (value.HasValue)
            {
                _minWinRateInclusive = value.Value;
            }
        }
    }

    public int MaxHeroes { get; init; } = CurrentMaxHeroes;
    public required IReadOnlyList<PersonalHeroRecord> SelectedHeroes { get; init; }

    public bool UsesCurrentRules()
        => string.Equals(RuleVersion, CurrentRuleVersion, StringComparison.Ordinal)
           && WindowDays == CurrentWindowDays
           && MinGamesInclusive == CurrentMinGamesInclusive
           && Math.Abs(MinWinRateInclusive - CurrentMinWinRateInclusive) < 0.0000001d
           && MaxHeroes == CurrentMaxHeroes;
}

public sealed class PersonalizationCacheEntry
{
    private int _minGamesInclusive = PersonalHeroSelection.CurrentMinGamesInclusive;
    private double _minWinRateInclusive = PersonalHeroSelection.CurrentMinWinRateInclusive;

    public required string AccountId { get; init; }
    public string? DisplayName { get; init; }
    public required DateTimeOffset FetchedAt { get; init; }
    public string RuleVersion { get; init; } = PersonalHeroSelection.CurrentRuleVersion;
    public int WindowDays { get; init; } = PersonalHeroSelection.CurrentWindowDays;
    public int MinGamesInclusive
    {
        get => _minGamesInclusive;
        init => _minGamesInclusive = value;
    }

    public double MinWinRateInclusive
    {
        get => _minWinRateInclusive;
        init => _minWinRateInclusive = value;
    }

    [JsonPropertyName("minGamesExclusive")]
    public int? LegacyMinGamesExclusive
    {
        init
        {
            if (value.HasValue)
            {
                _minGamesInclusive = value.Value;
            }
        }
    }

    [JsonPropertyName("minWinRateExclusive")]
    public double? LegacyMinWinRateExclusive
    {
        init
        {
            if (value.HasValue)
            {
                _minWinRateInclusive = value.Value;
            }
        }
    }

    public int MaxHeroes { get; init; } = PersonalHeroSelection.CurrentMaxHeroes;
    public required PersonalizationStatus Status { get; init; }
    public required IReadOnlyList<PersonalHeroRecord> SelectedHeroes { get; init; }

    public bool UsesCurrentRules()
        => string.Equals(RuleVersion, PersonalHeroSelection.CurrentRuleVersion, StringComparison.Ordinal)
           && WindowDays == PersonalHeroSelection.CurrentWindowDays
           && MinGamesInclusive == PersonalHeroSelection.CurrentMinGamesInclusive
           && Math.Abs(MinWinRateInclusive - PersonalHeroSelection.CurrentMinWinRateInclusive) < 0.0000001d
           && MaxHeroes == PersonalHeroSelection.CurrentMaxHeroes;
}

public sealed class PersonalizationResolution
{
    public required PersonalizationStatus Status { get; init; }
    public PersonalizationAccountSourceMode SourceMode { get; init; } = PersonalizationAccountSourceMode.SelectedSteamAccount;
    public string Message { get; init; } = string.Empty;
    public string? AccountId { get; init; }
    public string? DisplayName { get; init; }
    public DateTimeOffset? FetchedAt { get; init; }
    public bool UsedCache { get; init; }
    public bool IsFresh { get; init; }
    public PersonalHeroSelection? Selection { get; init; }
}

public sealed class PersonalizationAccountContext
{
    public PersonalizationAccountSourceMode SourceMode { get; init; } = PersonalizationAccountSourceMode.SelectedSteamAccount;
    public string? AccountId { get; init; }
    public string? DisplayName { get; init; }
    public bool HasAccount => !string.IsNullOrWhiteSpace(AccountId);
}

public sealed class ProfileInputParseResult
{
    public bool Succeeded { get; init; }
    public string? AccountId { get; init; }
    public string? Error { get; init; }

    public static ProfileInputParseResult Success(string accountId) => new()
    {
        Succeeded = true,
        AccountId = accountId
    };

    public static ProfileInputParseResult Failure(string error) => new()
    {
        Succeeded = false,
        Error = error
    };
}

public sealed class EffectiveGridCompositionResult
{
    public required HeroGridSnapshot BaseSnapshot { get; init; }
    public required HeroGridSnapshot EffectiveSnapshot { get; init; }
    public required string BaseSourceHash { get; init; }
    public required string EffectiveGridHash { get; init; }
    public required PersonalizationResolution Personalization { get; init; }
    public bool PersonalRowApplied => EffectiveGridHash != BaseSourceHash;
}

public sealed class OpenDotaPlayerProfileResponse
{
    public required string AccountId { get; init; }
    public string? PersonaName { get; init; }
    public bool IsProfileUnavailable { get; init; }
}

public sealed class OpenDotaPlayerHeroStats
{
    public required int HeroId { get; init; }
    public required int Games { get; init; }
    public required int Wins { get; init; }
    public required long LastPlayedUnixSeconds { get; init; }
}
