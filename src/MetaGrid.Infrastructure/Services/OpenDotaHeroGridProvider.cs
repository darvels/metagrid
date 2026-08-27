using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class OpenDotaHeroGridProvider(
    IDotaGridService dotaGridService,
    IHeroCatalogService heroCatalogService)
{
    private static readonly Uri ExplorerUri = new("https://api.opendota.com/api/explorer");
    private static readonly (int Position, string RoleName)[] Roles =
    [
        (1, "Carry"),
        (2, "Mid"),
        (3, "Offlane"),
        (4, "Support"),
        (5, "Hard Support")
    ];

    internal const int MinimumEligibleMatches = 100;
    internal const string RankBracketName = "Immortal+";
    private const string LayoutName = "Immortal+ - 7 Days";
    private const string PatchLabel = "Rolling 7-day window";

    public async Task<HeroGridSnapshot> FetchAsync(HeroGridPreset preset, CancellationToken cancellationToken)
    {
        var periodEnd = DateTimeOffset.UtcNow;
        var periodStart = periodEnd.AddDays(-7);
        var cutoffEpoch = periodStart.ToUnixTimeSeconds();
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(25)
        };

        var requestUri = BuildExplorerUri(cutoffEpoch);
        var response = await client.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HeroGridProviderUnavailableException(
                response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ? ProviderStatus.RateLimited : ProviderStatus.Unavailable,
                $"OpenDota Explorer returned HTTP {(int)response.StatusCode}: {errorBody}");
        }

        var payload = await response.Content.ReadFromJsonAsync<ExplorerResponse>(cancellationToken: cancellationToken);
        if (payload?.Rows is null || payload.Rows.Count == 0)
        {
            throw new HeroGridProviderUnavailableException(ProviderStatus.Unavailable, "OpenDota Explorer returned no role-qualified hero statistics.");
        }

        var heroesByName = await heroCatalogService.LoadByNameAsync(cancellationToken);
        var heroNamesById = heroesByName.Values.ToDictionary(hero => hero.Id, hero => hero.LocalizedName);

        var roleResults = Roles
            .Select(role =>
            {
                var heroes = payload.Rows
                    .Where(row => row.Position == role.Position && row.Matches >= MinimumEligibleMatches)
                    .OrderByDescending(row => row.WinRate)
                    .ThenByDescending(row => row.Matches)
                    .ThenBy(row => row.HeroId)
                    .Take(5)
                    .Select(row => new HeroRoleStat
                    {
                        HeroId = row.HeroId,
                        HeroName = heroNamesById.TryGetValue(row.HeroId, out var heroName) ? heroName : $"Hero {row.HeroId}",
                        Position = role.Position,
                        RoleName = role.RoleName,
                        Wins = row.Wins,
                        Matches = row.Matches,
                        WinRate = row.WinRate,
                        RankBracket = RankBracketName,
                        PeriodStart = periodStart,
                        PeriodEnd = periodEnd,
                        Provider = "OpenDota",
                        SourceTimestamp = periodEnd,
                        ProviderHeroIdentifier = row.HeroId.ToString(CultureInfo.InvariantCulture)
                    })
                    .ToList();

                return new RoleGridResult
                {
                    Position = role.Position,
                    RoleName = role.RoleName,
                    Heroes = heroes
                };
            })
            .ToList();

        var insufficientRole = roleResults.FirstOrDefault(result => result.Heroes.Count < 5);
        if (insufficientRole is not null)
        {
            throw new HeroGridProviderUnavailableException(
                ProviderStatus.Unavailable,
                $"OpenDota could not produce five eligible heroes for {insufficientRole.RoleName} after the {MinimumEligibleMatches}-match minimum sample rule.");
        }

        var layout = new HeroGridLayout
        {
            Name = LayoutName,
            Categories = roleResults
                .Select(result => new HeroGridCategory
                {
                    Name = result.RoleName,
                    HeroIds = result.Heroes.Select(hero => hero.HeroId).ToList()
                })
                .ToList()
        };

        var draft = new HeroGridSnapshot
        {
            SourceName = "OpenDota",
            ProviderName = "OpenDota",
            SourceStrategy = "OpenDotaExplorerImmortal7d",
            Preset = HeroGridPreset.HighWinrate,
            PatchLabel = PatchLabel,
            CapturedAt = periodEnd,
            Hash = string.Empty,
            Layouts = [layout],
            RankBracket = RankBracketName,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            MinimumSampleMatches = MinimumEligibleMatches,
            UsesDerivedPositions = true,
            PositionDefinition = "Positions are reconstructed per team from OpenDota match telemetry: prefer lane_role=2 for Mid, prefer lane_role=1 for Carry, assign the lowest-farm remaining player to Hard Support, the highest-farm remaining player to Offlane, and the final remaining player to Support.",
            RoleResults = roleResults,
            ProviderStatus = ProviderStatus.Online,
            OriginKind = GridOriginKind.AlternativeProvider,
            SourceDetails = "OpenDota Explorer query over ranked Immortal+ matches from the last 7 days with deterministic team-level position reconstruction and ranking by WinRate DESC, Matches DESC, HeroId ASC."
        };

        var hash = dotaGridService.ComputeSnapshotHash(draft);
        return new HeroGridSnapshot
        {
            SourceName = draft.SourceName,
            ProviderName = draft.ProviderName,
            SourceStrategy = draft.SourceStrategy,
            Preset = draft.Preset,
            PatchLabel = draft.PatchLabel,
            CapturedAt = draft.CapturedAt,
            Hash = hash,
            Layouts = draft.Layouts,
            RankBracket = draft.RankBracket,
            PeriodStart = draft.PeriodStart,
            PeriodEnd = draft.PeriodEnd,
            MinimumSampleMatches = draft.MinimumSampleMatches,
            UsesDerivedPositions = draft.UsesDerivedPositions,
            PositionDefinition = draft.PositionDefinition,
            RoleResults = draft.RoleResults,
            ProviderStatus = draft.ProviderStatus,
            OriginKind = draft.OriginKind,
            SourceDetails = draft.SourceDetails
        };
    }

    private static Uri BuildExplorerUri(long cutoffEpoch)
    {
        var sql = $"""
WITH recent AS (
    SELECT
        pm.match_id,
        CASE WHEN pm.player_slot < 128 THEN 1 ELSE 0 END AS team_side,
        pm.player_slot,
        pm.hero_id,
        COALESCE(pm.lane_role, 0) AS lane_role,
        COALESCE(pm.net_worth, 0) AS net_worth,
        COALESCE(pm.gold_per_min, 0) AS gold_per_min,
        COALESCE(pm.last_hits, 0) AS last_hits,
        COALESCE(pm.observers_placed, 0) + COALESCE(pm.sen_placed, 0) AS wards,
        m.radiant_win
    FROM player_matches pm
    JOIN matches m USING (match_id)
    JOIN public_matches pub USING (match_id)
    WHERE m.start_time >= {cutoffEpoch.ToString(CultureInfo.InvariantCulture)}
      AND pub.avg_rank_tier >= 80
      AND m.lobby_type = 7
      AND pm.hero_id > 0
),
mid_pick AS (
    SELECT * FROM (
        SELECT recent.*, ROW_NUMBER() OVER (
            PARTITION BY match_id, team_side
            ORDER BY CASE WHEN lane_role = 2 THEN 0 ELSE 1 END, net_worth DESC, gold_per_min DESC, hero_id ASC) AS rn
        FROM recent
    ) ranked WHERE rn = 1
),
remaining_after_mid AS (
    SELECT r.*
    FROM recent r
    LEFT JOIN mid_pick p
      ON p.match_id = r.match_id
     AND p.team_side = r.team_side
     AND p.player_slot = r.player_slot
    WHERE p.player_slot IS NULL
),
carry_pick AS (
    SELECT * FROM (
        SELECT remaining_after_mid.*, ROW_NUMBER() OVER (
            PARTITION BY match_id, team_side
            ORDER BY CASE WHEN lane_role = 1 THEN 0 ELSE 1 END, net_worth DESC, gold_per_min DESC, last_hits DESC, hero_id ASC) AS rn
        FROM remaining_after_mid
    ) ranked WHERE rn = 1
),
remaining_after_carry AS (
    SELECT r.*
    FROM remaining_after_mid r
    LEFT JOIN carry_pick p
      ON p.match_id = r.match_id
     AND p.team_side = r.team_side
     AND p.player_slot = r.player_slot
    WHERE p.player_slot IS NULL
),
hard_support_pick AS (
    SELECT * FROM (
        SELECT remaining_after_carry.*, ROW_NUMBER() OVER (
            PARTITION BY match_id, team_side
            ORDER BY net_worth ASC, gold_per_min ASC, wards DESC, hero_id ASC) AS rn
        FROM remaining_after_carry
    ) ranked WHERE rn = 1
),
remaining_after_hard_support AS (
    SELECT r.*
    FROM remaining_after_carry r
    LEFT JOIN hard_support_pick p
      ON p.match_id = r.match_id
     AND p.team_side = r.team_side
     AND p.player_slot = r.player_slot
    WHERE p.player_slot IS NULL
),
offlane_pick AS (
    SELECT * FROM (
        SELECT remaining_after_hard_support.*, ROW_NUMBER() OVER (
            PARTITION BY match_id, team_side
            ORDER BY net_worth DESC, gold_per_min DESC, last_hits DESC, hero_id ASC) AS rn
        FROM remaining_after_hard_support
    ) ranked WHERE rn = 1
),
support_pick AS (
    SELECT r.*
    FROM remaining_after_hard_support r
    LEFT JOIN offlane_pick p
      ON p.match_id = r.match_id
     AND p.team_side = r.team_side
     AND p.player_slot = r.player_slot
    WHERE p.player_slot IS NULL
),
position_rows AS (
    SELECT 1 AS position, 'Carry' AS role_name, hero_id, ((team_side = 1 AND radiant_win) OR (team_side = 0 AND NOT radiant_win)) AS won FROM carry_pick
    UNION ALL
    SELECT 2 AS position, 'Mid' AS role_name, hero_id, ((team_side = 1 AND radiant_win) OR (team_side = 0 AND NOT radiant_win)) AS won FROM mid_pick
    UNION ALL
    SELECT 3 AS position, 'Offlane' AS role_name, hero_id, ((team_side = 1 AND radiant_win) OR (team_side = 0 AND NOT radiant_win)) AS won FROM offlane_pick
    UNION ALL
    SELECT 4 AS position, 'Support' AS role_name, hero_id, ((team_side = 1 AND radiant_win) OR (team_side = 0 AND NOT radiant_win)) AS won FROM support_pick
    UNION ALL
    SELECT 5 AS position, 'Hard Support' AS role_name, hero_id, ((team_side = 1 AND radiant_win) OR (team_side = 0 AND NOT radiant_win)) AS won FROM hard_support_pick
)
SELECT
    position,
    role_name,
    hero_id,
    COUNT(*)::int AS matches,
    SUM(CASE WHEN won THEN 1 ELSE 0 END)::int AS wins,
    (SUM(CASE WHEN won THEN 1 ELSE 0 END)::float / COUNT(*)::float) AS win_rate
FROM position_rows
GROUP BY position, role_name, hero_id
ORDER BY position ASC, win_rate DESC, matches DESC, hero_id ASC
""";

        var builder = new UriBuilder(ExplorerUri);
        builder.Query = $"sql={Uri.EscapeDataString(sql)}";
        return builder.Uri;
    }

    private sealed class ExplorerResponse
    {
        [JsonPropertyName("rows")]
        public List<ExplorerRow> Rows { get; init; } = [];
    }

    private sealed class ExplorerRow
    {
        [JsonPropertyName("position")]
        public int Position { get; init; }

        [JsonPropertyName("role_name")]
        public string RoleName { get; init; } = string.Empty;

        [JsonPropertyName("hero_id")]
        public int HeroId { get; init; }

        [JsonPropertyName("matches")]
        public int Matches { get; init; }

        [JsonPropertyName("wins")]
        public int Wins { get; init; }

        [JsonPropertyName("win_rate")]
        public double WinRate { get; init; }
    }
}
