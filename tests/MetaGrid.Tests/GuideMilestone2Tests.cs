using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;

namespace MetaGrid.Tests;

public sealed partial class GuideMilestone2Tests
{
    private readonly ValveGuideSerializer _serializer = new();

    [Fact]
    public void IoMidFixture_Parses_WithGuideFormatVersion2Shape_AndSelectedTalents()
    {
        var text = File.ReadAllText(GetFixturePath("d2pt-io-mid.build"));

        var parsed = _serializer.Parse(text);

        Assert.Equal("wisp", parsed.HeroToken);
        Assert.Equal("MetaGrid D2PT Io Mid - Aghanim's Scepter Core", parsed.Title);
        Assert.Equal("#DOTA_HeroGuide_Role_Core", parsed.RoleToken);
        Assert.Equal("7.41e", parsed.GameplayVersion);
        Assert.Equal(5, parsed.ItemGroups.Count);
        Assert.Equal(10, parsed.SkillOrder.Count);
        Assert.Collection(
            parsed.TalentChoices.OrderBy(choice => choice.Level),
            choice => Assert.Equal(("special_bonus_unique_wisp", 10), (choice.TalentId, choice.Level)),
            choice => Assert.Equal(("special_bonus_strength_8", 15), (choice.TalentId, choice.Level)),
            choice => Assert.Equal(("special_bonus_unique_wisp_10", 20), (choice.TalentId, choice.Level)),
            choice => Assert.Equal(("special_bonus_unique_wisp_relocate_delay", 25), (choice.TalentId, choice.Level)));
        Assert.Contains("\"GuideFormatVersion\"", text);
        Assert.Contains("\"TimePublished\"", text);
    }

    [Fact]
    public async Task MappingResolver_Resolves_CurrentIoItemsAbilities_AndTalents()
    {
        var resolver = new DotaGuideMappingResolver(allowReferenceData: true);
        var account = CreateAccount();
        var subscription = CreateSubscription();

        var result = await resolver.ResolveAsync(account, subscription, CreateBuild(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Build);
        Assert.Equal("wisp", result.Build!.HeroToken);
        Assert.Equal("#DOTA_HeroGuide_Role_Core", result.Build.RoleToken);
        Assert.Equal("0x000000000000007B", result.Build.OriginalCreatorIdHex);
        Assert.Equal(4, result.Build.TalentChoices.Count);
        Assert.Contains(result.Build.TalentChoices, talent => talent.TalentId == "special_bonus_unique_wisp_relocate_delay");
    }

    [Fact]
    public void CanonicalSourceHash_IsDeterministic_ForEquivalentIoMidBuilds()
    {
        var build = CreateBuild();

        var first = _serializer.ComputeCanonicalSourceHash(build);
        var second = _serializer.ComputeCanonicalSourceHash(CreateBuild());

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task FirstCreate_PersistsRemoteFile_AndHashes()
    {
        var provider = new FakeGuideProvider(CreateBuild());
        var remote = new FakeRemoteStorageGuideService();
        var service = CreateGuideSubscriptionService(provider, remote);

        var result = await service.SyncAsync(CreateAccount(), CreateSubscription(), CancellationToken.None);

        Assert.Equal(GuideSubscriptionStatus.Installed, result.Status);
        Assert.Equal("guides/wisp_metagrid_mid_.build", result.RemoteFile);
        Assert.NotNull(result.CanonicalSourceHash);
        Assert.NotNull(result.EffectiveGuideHash);
        Assert.Equal(1, remote.WriteCount);
    }

    [Fact]
    public async Task ChangedSourceThenRestore_ReusesSameFile_AndMatchesOriginalSemantics()
    {
        var original = CreateBuild();
        var changed = CreateBuild();
        changed.ItemGroups[2].ItemIds.Reverse();
        var remote = new FakeRemoteStorageGuideService();
        var service = CreateGuideSubscriptionService(new SequencedGuideProvider(original, changed, original, original), remote);
        var subscription = CreateSubscription();
        string? path = null;
        for (var i = 0; i < 4; i++)
        {
            var result = await service.SyncAsync(CreateAccount(), subscription, default);
            Assert.True(subscription.ApplyVerifiedInstallation(result));
            path ??= result.RemoteFile;
            Assert.Equal(path, result.RemoteFile);
            Assert.Single(remote.Files);
            Assert.Equal(i == 3 ? GuideSubscriptionStatus.UpToDate : GuideSubscriptionStatus.Installed, result.Status);
        }
        Assert.Equal(3, remote.WriteCount);
        var installed = _serializer.Parse(System.Text.Encoding.UTF8.GetString(remote.Files[path!]));
        Assert.Equal(original.ItemGroups[2].ItemIds, installed.ItemGroups[2].ItemIds);
    }

    [Fact]
    public async Task FailedReadBack_DoesNotCommitInstalledHashes()
    {
        var remote = new FakeRemoteStorageGuideService { CorruptReadBack = true };
        var service = CreateGuideSubscriptionService(new FakeGuideProvider(CreateBuild()), remote);
        var subscription = CreateSubscription();
        subscription.InstalledHash = "previous";
        var result = await service.SyncAsync(CreateAccount(), subscription, default);
        Assert.Equal(GuideSubscriptionStatus.Error, result.Status);
        Assert.False(subscription.ApplyVerifiedInstallation(result));
        Assert.Equal("previous", subscription.InstalledHash);
        Assert.Null(subscription.RemoteFile);
        Assert.Null(subscription.CanonicalSourceHash);
    }

    [Fact]
    public async Task UnchangedRefresh_DoesNotWriteAgain_AndReusesSameRemoteFile()
    {
        var provider = new FakeGuideProvider(CreateBuild());
        var remote = new FakeRemoteStorageGuideService();
        var service = CreateGuideSubscriptionService(provider, remote);
        var account = CreateAccount();
        var subscription = CreateSubscription();

        var first = await service.SyncAsync(account, subscription, CancellationToken.None);
        subscription.RemoteFile = first.RemoteFile;
        subscription.CanonicalSourceHash = first.CanonicalSourceHash;
        subscription.EffectiveGuideHash = first.EffectiveGuideHash;
        subscription.GuideRevision = 1;

        var second = await service.SyncAsync(account, subscription, CancellationToken.None);

        Assert.Equal(GuideSubscriptionStatus.UpToDate, second.Status);
        Assert.Equal(first.RemoteFile, second.RemoteFile);
        Assert.Equal(1, remote.WriteCount);
    }

    [Fact]
    public async Task StartupSync_InstallsMissing_ThenUnchangedRelaunchDoesNotWrite()
    {
        var remote = new FakeRemoteStorageGuideService();
        var service = CreateGuideSubscriptionService(new FakeGuideProvider(CreateBuild()), remote);
        var subscription = CreateSubscription();
        subscription.IsEnabled = true;
        var disabled = CreateSubscription();
        disabled.Role = GuideRole.Support;
        disabled.IsEnabled = false;
        var subscriptions = new[] { subscription, disabled };
        var statuses = new List<GuideSubscriptionStatus>();
        async Task Sync(int id, GuideRole role, CancellationToken token)
        {
            var record = subscriptions.Single(item => item.HeroId == id && item.Role == role);
            var result = await service.SyncAsync(CreateAccount(), record, token);
            record.ApplyVerifiedInstallation(result);
            statuses.Add(result.Status);
        }
        await MetaGrid.Core.Services.GuideStartupSync.RunAsync(subscriptions, Sync, CancellationToken.None);
        Assert.Equal(1, remote.WriteCount);
        await MetaGrid.Core.Services.GuideStartupSync.RunAsync(subscriptions, Sync, CancellationToken.None);
        Assert.Equal(new[] { GuideSubscriptionStatus.Installed, GuideSubscriptionStatus.UpToDate }, statuses);
        Assert.Equal(1, remote.WriteCount);
        Assert.Single(remote.Files);
    }

    [Fact]
    public async Task ChangedSource_UpdatesSameRemoteFilename_WithoutDuplicateCreation()
    {
        var remote = new FakeRemoteStorageGuideService();
        var firstBuild = CreateBuild();
        var secondBuild = CreateBuild();
        secondBuild.ItemGroups[2].ItemIds = ["item_ultimate_scepter", "item_black_king_bar", "item_octarine_core"];
        secondBuild.CanonicalSourceHash = string.Empty;

        var provider = new SequencedGuideProvider(firstBuild, secondBuild);
        var service = CreateGuideSubscriptionService(provider, remote);
        var account = CreateAccount();
        var subscription = CreateSubscription();

        var first = await service.SyncAsync(account, subscription, CancellationToken.None);
        subscription.RemoteFile = first.RemoteFile;
        subscription.CanonicalSourceHash = first.CanonicalSourceHash;
        subscription.EffectiveGuideHash = first.EffectiveGuideHash;
        subscription.GuideRevision = 1;

        var second = await service.SyncAsync(account, subscription, CancellationToken.None);

        Assert.Equal(GuideSubscriptionStatus.Installed, second.Status);
        Assert.Equal(first.RemoteFile, second.RemoteFile);
        Assert.Equal(2, remote.WriteCount);
        Assert.Single(remote.Files);
    }

    [Fact]
    public async Task RecoveryFromMissingRemoteFile_ReattachesWithoutDuplicate()
    {
        var build = CreateBuild();
        var resolved = await new DotaGuideMappingResolver(allowReferenceData: true).ResolveAsync(CreateAccount(), CreateSubscription(), build, CancellationToken.None);
        var serialized = _serializer.Serialize(resolved.Build!, 1, DateTimeOffset.UtcNow);

        var remote = new FakeRemoteStorageGuideService();
        remote.Seed("guides/wisp_metagrid_mid_.build", serialized.Bytes);

        var provider = new FakeGuideProvider(build);
        var service = CreateGuideSubscriptionService(provider, remote);
        var subscription = CreateSubscription();
        subscription.CanonicalSourceHash = build.CanonicalSourceHash;
        subscription.EffectiveGuideHash = serialized.EffectiveGuideHash;

        var result = await service.SyncAsync(CreateAccount(), subscription, CancellationToken.None);

        Assert.Equal(GuideSubscriptionStatus.UpToDate, result.Status);
        Assert.Equal("guides/wisp_metagrid_mid_.build", result.RemoteFile);
        Assert.Equal(0, remote.WriteCount);
    }

    [Fact]
    public async Task AmbiguousRecovery_RefusesOverwrite()
    {
        var build = CreateBuild();
        var resolved = await new DotaGuideMappingResolver(allowReferenceData: true).ResolveAsync(CreateAccount(), CreateSubscription(), build, CancellationToken.None);
        var serialized = _serializer.Serialize(resolved.Build!, 1, DateTimeOffset.UtcNow);

        var remote = new FakeRemoteStorageGuideService();
        remote.Seed("guides/wisp_metagrid_mid_.build", serialized.Bytes);
        remote.Seed("guides/wisp_metagrid_mid_2.build", serialized.Bytes);

        var service = CreateGuideSubscriptionService(new FakeGuideProvider(build), remote);
        var result = await service.SyncAsync(CreateAccount(), CreateSubscription(), CancellationToken.None);

        Assert.Equal(GuideSubscriptionStatus.Conflict, result.Status);
        Assert.Equal(0, remote.WriteCount);
    }

    [Fact]
    public async Task ProviderFailure_PreservesSubscription_AndReturnsSourceUnavailable()
    {
        var remote = new FakeRemoteStorageGuideService();
        var service = CreateGuideSubscriptionService(new ThrowingGuideProvider("Cloudflare blocked Io Mid build page."), remote);

        var result = await service.SyncAsync(CreateAccount(), CreateSubscription(), CancellationToken.None);

        Assert.Equal(GuideSubscriptionStatus.SourceUnavailable, result.Status);
        Assert.Equal(0, remote.WriteCount);
    }

    [Fact]
    public async Task SteamUnavailable_PreservesSubscription_AndSkipsWrite()
    {
        var remote = new FakeRemoteStorageGuideService
        {
            Available = false
        };
        var service = CreateGuideSubscriptionService(new FakeGuideProvider(CreateBuild()), remote);

        var result = await service.SyncAsync(CreateAccount(), CreateSubscription(), CancellationToken.None);

        Assert.Equal(GuideSubscriptionStatus.SteamUnavailable, result.Status);
        Assert.Equal(0, remote.WriteCount);
    }

    [Fact]
    public async Task UnknownMapping_BlocksInstallation()
    {
        var remote = new FakeRemoteStorageGuideService();
        var service = new GuideSubscriptionService(
            new FakeGuideProvider(CreateBuild()),
            new FailingMappingResolver("Current Dota mapping could not resolve talent 'unknown_talent'."),
            _serializer,
            remote,
            new FakeLoggingService());

        var result = await service.SyncAsync(CreateAccount(), CreateSubscription(), CancellationToken.None);

        Assert.Equal(GuideSubscriptionStatus.MappingFailed, result.Status);
        Assert.Equal(0, remote.WriteCount);
    }

    private GuideSubscriptionService CreateGuideSubscriptionService(ID2ptGuideProvider provider, FakeRemoteStorageGuideService remote)
        => new(provider, new DotaGuideMappingResolver(allowReferenceData: true), _serializer, remote, new FakeLoggingService());

    private static SteamAccount CreateAccount() => new()
    {
        AccountId = "123",
        SteamRootPath = @"C:\Program Files (x86)\Steam",
        UserDataPath = @"C:\FixtureSteam\userdata\123",
        DotaConfigDirectory = @"C:\FixtureSteam\userdata\123\570\remote\cfg",
        DisplayName = "Fixture player"
    };

    private static GuideSubscriptionRecord CreateSubscription() => new()
    {
        HeroId = 91,
        HeroInternalName = "npc_dota_hero_wisp",
        HeroDisplayName = "Io",
        Role = GuideRole.Mid,
        IsEnabled = true
    };

    private NormalizedHeroGuideBuild CreateBuild()
    {
        var build = new NormalizedHeroGuideBuild
        {
            HeroId = 91,
            HeroInternalName = "npc_dota_hero_wisp",
            Role = GuideRole.Mid,
            Source = "Dota2ProTracker",
            SourceBuildId = "io-pos2-1",
            SourceTitle = "Aghanim's Scepter Core",
            GameplayVersion = "7.41e",
            WinRate = 64,
            Matches = 11,
            SourceUrl = "https://dota2protracker.com/builds/io-pos2-1",
            RetrievedAtUtc = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero),
            ItemGroups =
            [
                new NormalizedGuideItemGroup { CategoryKey = "#DOTA_Item_Build_Starting_Items", DisplayName = "Starting", ItemIds = ["item_branches", "item_branches", "item_branches", "item_branches", "item_branches"] },
                new NormalizedGuideItemGroup { CategoryKey = "#DOTA_Item_Build_Early_Game", DisplayName = "Early", ItemIds = ["item_bottle", "item_magic_wand", "item_null_talisman"] },
                new NormalizedGuideItemGroup { CategoryKey = "#DOTA_Item_Build_Core_Items", DisplayName = "Core", ItemIds = ["item_ultimate_scepter", "item_black_king_bar", "item_shivas_guard"] },
                new NormalizedGuideItemGroup { CategoryKey = "EXTENSION ITEMS", DisplayName = "Extension", ItemIds = ["item_octarine_core", "item_heart", "item_sheepstick"] },
                new NormalizedGuideItemGroup { CategoryKey = "SITUATIONAL ITEMS", DisplayName = "Situational", ItemIds = ["item_cyclone", "item_aeon_disk", "item_lesser_crit"] }
            ],
            SkillOrder =
            [
                "wisp_spirits",
                "wisp_tether",
                "wisp_spirits",
                "wisp_overcharge",
                "wisp_spirits",
                "wisp_relocate",
                "wisp_spirits",
                "wisp_overcharge",
                "wisp_overcharge",
                "wisp_overcharge"
            ],
            TalentChoices =
            [
                new NormalizedGuideTalentChoice { Level = 10, TalentId = "special_bonus_unique_wisp", SelectedLabel = "+1.5s Overcharge Duration" },
                new NormalizedGuideTalentChoice { Level = 15, TalentId = "special_bonus_strength_8", SelectedLabel = "+8 Strength" },
                new NormalizedGuideTalentChoice { Level = 20, TalentId = "special_bonus_unique_wisp_10", SelectedLabel = "+50% Spirits Damage" },
                new NormalizedGuideTalentChoice { Level = 25, TalentId = "special_bonus_unique_wisp_relocate_delay", SelectedLabel = "-2s Relocate Cast Delay" }
            ]
        };
        build.CanonicalSourceHash = _serializer.ComputeCanonicalSourceHash(build);
        return build;
    }

    private static string GetFixturePath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private sealed class FakeGuideProvider(NormalizedHeroGuideBuild build) : ID2ptGuideProvider
    {
        public Task<NormalizedHeroGuideBuild> FetchAsync(GuideSubscriptionRecord subscription, CancellationToken cancellationToken)
            => Task.FromResult(Clone(build));
    }

    private sealed class SequencedGuideProvider(params NormalizedHeroGuideBuild[] builds) : ID2ptGuideProvider
    {
        private readonly Queue<NormalizedHeroGuideBuild> _builds = new(builds.Select(Clone));

        public Task<NormalizedHeroGuideBuild> FetchAsync(GuideSubscriptionRecord subscription, CancellationToken cancellationToken)
            => Task.FromResult(Clone(_builds.Count > 1 ? _builds.Dequeue() : _builds.Peek()));
    }

    private sealed class ThrowingGuideProvider(string message) : ID2ptGuideProvider
    {
        public Task<NormalizedHeroGuideBuild> FetchAsync(GuideSubscriptionRecord subscription, CancellationToken cancellationToken)
            => throw new InvalidOperationException(message);
    }

    private sealed class FailingMappingResolver(string error) : IDotaGuideMappingResolver
    {
        public Task<DotaGuideMappingResult> ResolveAsync(SteamAccount account, GuideSubscriptionRecord subscription, NormalizedHeroGuideBuild build, CancellationToken cancellationToken)
            => Task.FromResult(DotaGuideMappingResult.Failure(error));
    }

    private sealed class FakeRemoteStorageGuideService : ISteamRemoteStorageGuideService
    {
        public bool Available { get; set; } = true;
        public bool CorruptReadBack { get; set; }
        public int WriteCount { get; private set; }
        public int DeleteCount { get; private set; }
        public bool DeleteSucceeds { get; set; } = true;
        public bool KeepFileAfterDelete { get; set; }
        public bool ChangeBeforeDelete { get; set; }
        public Func<CancellationToken, Task<bool>>? AvailabilityProbe { get; set; }
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<bool> IsAvailableAsync(SteamAccount account, CancellationToken cancellationToken)
            => AvailabilityProbe?.Invoke(cancellationToken) ?? Task.FromResult(Available);

        public Task<IReadOnlyList<RemoteGuideFileEntry>> ListFilesAsync(SteamAccount account, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RemoteGuideFileEntry>>(Files.Select(pair => new RemoteGuideFileEntry
            {
                Name = pair.Key,
                Size = pair.Value.Length,
                Exists = true,
                Persisted = true,
                Timestamp = 0
            }).ToList());

        public Task<byte[]?> ReadFileAsync(SteamAccount account, string remoteFile, CancellationToken cancellationToken)
            => Task.FromResult(Files.TryGetValue(remoteFile, out var bytes) ? (CorruptReadBack ? new byte[] { 1, 2, 3 } : bytes) : null);

        public Task<RemoteGuideWriteResult> WriteFileAsync(SteamAccount account, string remoteFile, byte[] bytes, CancellationToken cancellationToken)
        {
            WriteCount++;
            Files[remoteFile] = bytes.ToArray();
            return Task.FromResult(new RemoteGuideWriteResult
            {
                Succeeded = true,
                RemoteFile = remoteFile,
                BytesWritten = bytes.Length,
                ExistsAfterWrite = true,
                PersistedAfterWrite = true,
                ReadBackHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))
            });
        }

        public void Seed(string remoteFile, byte[] bytes)
            => Files[remoteFile] = bytes.ToArray();

        public Task<RemoteGuideDeleteResult> DeleteOwnedFileAsync(SteamAccount account, string remoteFile, string expectedContentHash, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ChangeBeforeDelete) Files[remoteFile] = [1, 2, 3];
            if (!Files.TryGetValue(remoteFile, out var bytes))
                return Task.FromResult(new RemoteGuideDeleteResult(true, false, false));
            if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) != expectedContentHash)
                return Task.FromResult(new RemoteGuideDeleteResult(false, true, true, "Content changed."));
            DeleteCount++;
            if (!DeleteSucceeds) return Task.FromResult(new RemoteGuideDeleteResult(false, true, true, "Delete failed."));
            if (!KeepFileAfterDelete) Files.Remove(remoteFile);
            return Task.FromResult(new RemoteGuideDeleteResult(true, false, false));
        }
    }

    private sealed class FakeLoggingService : ILoggingService
    {
        public string GetLogsDirectory() => string.Empty;

        public Task LogAsync(LogLevelKind level, string message, object? data = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static NormalizedHeroGuideBuild Clone(NormalizedHeroGuideBuild build)
        => new()
        {
            HeroId = build.HeroId,
            HeroInternalName = build.HeroInternalName,
            Role = build.Role,
            Source = build.Source,
            SourceBuildId = build.SourceBuildId,
            SourceTitle = build.SourceTitle,
            GameplayVersion = build.GameplayVersion,
            WinRate = build.WinRate,
            Matches = build.Matches,
            SourceUrl = build.SourceUrl,
            RetrievedAtUtc = build.RetrievedAtUtc,
            CanonicalSourceHash = build.CanonicalSourceHash,
            ItemGroups = build.ItemGroups.Select(group => new NormalizedGuideItemGroup
            {
                CategoryKey = group.CategoryKey,
                DisplayName = group.DisplayName,
                ItemIds = [.. group.ItemIds]
            }).ToList(),
            SkillOrder = [.. build.SkillOrder],
            TalentChoices = build.TalentChoices.Select(choice => new NormalizedGuideTalentChoice
            {
                Level = choice.Level,
                TalentId = choice.TalentId,
                SelectedLabel = choice.SelectedLabel,
                AlternativeLabel = choice.AlternativeLabel
            }).ToList()
        };
}
