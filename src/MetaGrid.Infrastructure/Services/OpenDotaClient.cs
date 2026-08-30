using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class OpenDotaClient(HttpClient httpClient) : IOpenDotaClient
{
    public async Task<OpenDotaPlayerProfileResponse> GetPlayerProfileAsync(string accountId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"/api/players/{accountId}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"OpenDota profile request failed with HTTP {(int)response.StatusCode}: {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<PlayerProfileEnvelope>(cancellationToken: cancellationToken);
        if (payload?.Profile is null)
        {
            throw new InvalidOperationException("OpenDota returned an invalid player profile payload.");
        }

        return new OpenDotaPlayerProfileResponse
        {
            AccountId = payload.Profile.AccountId.ToString(),
            PersonaName = string.IsNullOrWhiteSpace(payload.Profile.PersonaName) ? null : payload.Profile.PersonaName.Trim(),
            IsProfileUnavailable = payload.Profile.FullHistoryUnavailable
        };
    }

    public async Task<IReadOnlyList<OpenDotaPlayerHeroStats>> GetPlayerHeroesAsync(string accountId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"/api/players/{accountId}/heroes?date=90", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"OpenDota heroes request failed with HTTP {(int)response.StatusCode}: {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<List<PlayerHeroRow>>(cancellationToken: cancellationToken);
        if (payload is null)
        {
            throw new InvalidOperationException("OpenDota returned an invalid player hero payload.");
        }

        var results = new List<OpenDotaPlayerHeroStats>(payload.Count);
        foreach (var row in payload)
        {
            if (row.HeroId <= 0 || row.Games < 0 || row.Win < 0 || row.Win > row.Games || row.LastPlayed < 0)
            {
                throw new InvalidOperationException("OpenDota returned impossible player hero values.");
            }

            results.Add(new OpenDotaPlayerHeroStats
            {
                HeroId = row.HeroId,
                Games = row.Games,
                Wins = row.Win,
                LastPlayedUnixSeconds = row.LastPlayed
            });
        }

        return results;
    }

    private sealed class PlayerProfileEnvelope
    {
        [JsonPropertyName("profile")]
        public PlayerProfilePayload? Profile { get; init; }
    }

    private sealed class PlayerProfilePayload
    {
        [JsonPropertyName("account_id")]
        public long AccountId { get; init; }

        [JsonPropertyName("personaname")]
        public string? PersonaName { get; init; }

        [JsonPropertyName("fh_unavailable")]
        public bool FullHistoryUnavailable { get; init; }
    }

    private sealed class PlayerHeroRow
    {
        [JsonPropertyName("hero_id")]
        public int HeroId { get; init; }

        [JsonPropertyName("games")]
        public int Games { get; init; }

        [JsonPropertyName("win")]
        public int Win { get; init; }

        [JsonPropertyName("last_played")]
        public long LastPlayed { get; init; }
    }
}

