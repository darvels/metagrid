using System.Security.Cryptography;
using System.Text;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;

namespace MetaGrid.Tests.TestSupport;

internal sealed class StubProvider : IHeroGridProvider
{
    private readonly HeroGridSnapshot _snapshot;
    private readonly Func<Task>? _beforeReturn;

    public StubProvider(HeroGridSnapshot snapshot, Func<Task>? beforeReturn = null)
    {
        _snapshot = snapshot;
        _beforeReturn = beforeReturn;
    }

    public async Task<HeroGridSnapshot> FetchAsync(HeroGridPreset preset, CancellationToken cancellationToken)
    {
        if (_beforeReturn is not null)
        {
            await _beforeReturn();
        }

        return _snapshot;
    }

    public static HeroGridSnapshot CreateSnapshot(IReadOnlyList<int> heroes, string sourceName = "Stub", ProviderStatus providerStatus = ProviderStatus.Online)
    {
        var source = heroes.Concat(Enumerable.Range(201, 25)).Distinct().ToList();
        var carry = source.Take(5).ToList();
        var mid = source.Skip(5).Take(5).ToList();
        var offlane = source.Skip(10).Take(5).ToList();
        var support = source.Skip(15).Take(5).ToList();
        var hardSupport = source.Skip(20).Take(5).ToList();

        var categories = new List<HeroGridCategory>
        {
            new() { Name = "Carry", HeroIds = carry },
            new() { Name = "Mid", HeroIds = mid },
            new() { Name = "Offlane", HeroIds = offlane },
            new() { Name = "Support", HeroIds = support },
            new() { Name = "Hard Support", HeroIds = hardSupport }
        };

        var roleResults = new[]
        {
            CreateRoleResult(1, "Carry", carry),
            CreateRoleResult(2, "Mid", mid),
            CreateRoleResult(3, "Offlane", offlane),
            CreateRoleResult(4, "Support", support),
            CreateRoleResult(5, "Hard Support", hardSupport)
        };

        var draft = new HeroGridSnapshot
        {
            SourceName = sourceName,
            ProviderName = sourceName,
            SourceStrategy = "StubSnapshot",
            Preset = HeroGridPreset.HighWinrate,
            PatchLabel = "Rolling 7-day window",
            CapturedAt = DateTimeOffset.UtcNow,
            Hash = string.Empty,
            Layouts =
            [
                new HeroGridLayout
                {
                    Name = "Immortal+ - 7 Days",
                    Categories = categories
                }
            ],
            RankBracket = "Immortal+",
            PeriodStart = DateTimeOffset.UtcNow.AddDays(-7),
            PeriodEnd = DateTimeOffset.UtcNow,
            MinimumSampleMatches = 100,
            UsesDerivedPositions = true,
            PositionDefinition = "Test fixture",
            RoleResults = roleResults,
            ProviderStatus = providerStatus,
            SourceDetails = "Fixture",
            OriginKind = GridOriginKind.AlternativeProvider
        };

        var hash = new DotaGridService().NormalizeSnapshot(draft);
        return new HeroGridSnapshot
        {
            SourceName = draft.SourceName,
            ProviderName = draft.ProviderName,
            SourceStrategy = draft.SourceStrategy,
            Preset = draft.Preset,
            PatchLabel = draft.PatchLabel,
            CapturedAt = draft.CapturedAt,
            Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hash))),
            ProviderStatus = draft.ProviderStatus,
            SourceDetails = draft.SourceDetails,
            Layouts = draft.Layouts,
            RankBracket = draft.RankBracket,
            PeriodStart = draft.PeriodStart,
            PeriodEnd = draft.PeriodEnd,
            MinimumSampleMatches = draft.MinimumSampleMatches,
            UsesDerivedPositions = draft.UsesDerivedPositions,
            PositionDefinition = draft.PositionDefinition,
            RoleResults = draft.RoleResults,
            OriginKind = draft.OriginKind
        };
    }

    private static RoleGridResult CreateRoleResult(int position, string roleName, IReadOnlyList<int> heroIds)
        => new()
        {
            Position = position,
            RoleName = roleName,
            Heroes = heroIds.Select((heroId, index) => new HeroRoleStat
            {
                HeroId = heroId,
                HeroName = $"{roleName} Hero {heroId}",
                Position = position,
                RoleName = roleName,
                Wins = 200 - index * 10,
                Matches = 300 - index * 10,
                WinRate = (200d - index * 10) / (300 - index * 10),
                RankBracket = "Immortal+",
                PeriodStart = DateTimeOffset.UtcNow.AddDays(-7),
                PeriodEnd = DateTimeOffset.UtcNow,
                Provider = "Stub"
            }).ToList()
        };
}
