using System.Text;
using MetaGrid.Core.Models;
using MetaGrid.Core.Services;

namespace MetaGrid.Tests;

public sealed partial class GuideMilestone2Tests
{
    private const string OwnedPath = "guides/wisp_metagrid_mid_.build";

    private byte[] OwnedBytes(string title = "MetaGrid D2PT - Io Mid", string overview = "MetaGrid owner=IoMid role=Mid source=Dota2ProTracker", string hero = "wisp", string role = "#DOTA_HeroGuide_Role_Core", string? creator = null)
    {
        var build = CreateBuild();
        return _serializer.Serialize(new ResolvedHeroGuideBuild
        {
            SourceBuild = build, HeroToken = hero, RoleToken = role, GuideTitle = title,
            Overview = overview, OriginalCreatorIdHex = creator ?? $"0x{76561197960265728UL + ulong.Parse(CreateAccount().AccountId):X}",
            ItemGroups = build.ItemGroups, SkillOrder = build.SkillOrder,
            TalentChoices = build.TalentChoices.Select(talent => new ResolvedGuideTalentChoice
            { Level = talent.Level, TalentId = talent.TalentId, SelectedLabel = talent.SelectedLabel }).ToArray()
        }, 3, DateTimeOffset.UtcNow).Bytes;
    }

    private static GuideSubscriptionRecord RemovalRecord() => new()
    {
        HeroId = 91, HeroInternalName = "npc_dota_hero_wisp", HeroDisplayName = "Io", Role = GuideRole.Mid,
        IsEnabled = false, RemovalRequested = true, Status = GuideSubscriptionStatus.RemovalPending,
        RemoteFile = OwnedPath, InstalledHash = "installed", EffectiveGuideHash = "effective", GuideRevision = 3,
        InstalledAccountId = CreateAccount().AccountId
    };

    [Fact]
    public async Task Removal_DeletesOnlyExactOwnedRole_AndClearsMetadataAfterVerification()
    {
        var remote = new FakeRemoteStorageGuideService();
        remote.Seed(OwnedPath, OwnedBytes());
        remote.Seed("guides/my_custom_io.build", OwnedBytes());
        remote.Seed("guides/wisp_metagrid_carry_.build", OwnedBytes());
        var record = RemovalRecord();
        var result = await CreateGuideSubscriptionService(new FakeGuideProvider(CreateBuild()), remote).RemoveAsync(CreateAccount(), record, CancellationToken.None);
        Assert.True(result.VerifiedAbsent);
        Assert.Equal("installed", record.InstalledHash);
        Assert.True(record.ApplyVerifiedRemoval(result));
        Assert.Null(record.RemoteFile);
        Assert.Null(record.InstalledHash);
        Assert.Null(record.EffectiveGuideHash);
        Assert.Null(record.InstalledAccountId);
        Assert.Equal(0, record.GuideRevision);
        Assert.False(record.NeedsRemoval);
        Assert.Empty(GuideWorkspace.ConfiguredHeroes([record]));
        Assert.Equal(1, remote.DeleteCount);
        Assert.Equal(2, remote.Files.Count);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("marker")]
    [InlineData("hero")]
    [InlineData("role")]
    [InlineData("creator")]
    [InlineData("filename")]
    [InlineData("account")]
    public async Task Removal_RejectsOwnershipMismatch_WithoutDeletion(string mismatch)
    {
        var record = RemovalRecord();
        if (mismatch == "filename") record.RemoteFile = "guides/custom_io.build";
        if (mismatch == "account") record.InstalledAccountId = "456";
        var remote = new FakeRemoteStorageGuideService();
        remote.Seed(record.RemoteFile!, OwnedBytes(
            title: mismatch == "title" ? "MetaGrid personal guide" : "MetaGrid D2PT - Io Mid",
            overview: mismatch == "marker" ? "MetaGrid owner=Other role=Mid source=Dota2ProTracker" : "MetaGrid owner=IoMid role=Mid source=Dota2ProTracker",
            hero: mismatch == "hero" ? "pudge" : "wisp", role: mismatch == "role" ? "Support" : "#DOTA_HeroGuide_Role_Core",
            creator: mismatch == "creator" ? "0x123" : null));
        var result = await CreateGuideSubscriptionService(new FakeGuideProvider(CreateBuild()), remote).RemoveAsync(CreateAccount(), record, CancellationToken.None);
        Assert.False(result.VerifiedAbsent);
        Assert.False(record.ApplyVerifiedRemoval(result));
        Assert.Equal("installed", record.InstalledHash);
        Assert.Equal(0, remote.DeleteCount);
        Assert.Single(remote.Files);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("deletefailed")]
    [InlineData("stillpresent")]
    [InlineData("changed")]
    public async Task Removal_FailureRetainsOwnershipAndMyHero_ForRetry(string failure)
    {
        var remote = new FakeRemoteStorageGuideService
        {
            Available = failure != "unavailable", DeleteSucceeds = failure != "deletefailed",
            KeepFileAfterDelete = failure == "stillpresent", ChangeBeforeDelete = failure == "changed"
        };
        remote.Seed(OwnedPath, OwnedBytes());
        var record = RemovalRecord();
        var service = CreateGuideSubscriptionService(new FakeGuideProvider(CreateBuild()), remote);
        var result = await service.RemoveAsync(CreateAccount(), record, CancellationToken.None);
        Assert.False(result.VerifiedAbsent);
        Assert.False(record.ApplyVerifiedRemoval(result));
        Assert.Equal(OwnedPath, record.RemoteFile);
        Assert.Equal("installed", record.InstalledHash);
        Assert.Single(GuideWorkspace.ConfiguredHeroes([record]));
        remote.Available = remote.DeleteSucceeds = true;
        remote.KeepFileAfterDelete = remote.ChangeBeforeDelete = false;
        remote.Seed(OwnedPath, OwnedBytes());
        var retry = await service.RemoveAsync(CreateAccount(), record, CancellationToken.None);
        Assert.True(record.ApplyVerifiedRemoval(retry));
        Assert.Empty(remote.Files);
    }

    [Fact]
    public async Task Removal_AlreadyAbsent_IsIdempotent()
    {
        var remote = new FakeRemoteStorageGuideService();
        var service = CreateGuideSubscriptionService(new FakeGuideProvider(CreateBuild()), remote);
        Assert.True((await service.RemoveAsync(CreateAccount(), RemovalRecord(), CancellationToken.None)).VerifiedAbsent);
        Assert.Equal(0, remote.DeleteCount);
    }

    [Fact]
    public async Task Removal_ExplicitRecoveryFindsOnlyStrictOwnedFile()
    {
        var remote = new FakeRemoteStorageGuideService();
        remote.Seed(OwnedPath, OwnedBytes());
        remote.Seed("guides/custom_io.build", OwnedBytes());
        var record = RemovalRecord();
        record.RemoteFile = null;
        var result = await CreateGuideSubscriptionService(new FakeGuideProvider(CreateBuild()), remote).RemoveAsync(CreateAccount(), record, CancellationToken.None);
        Assert.True(result.VerifiedAbsent);
        Assert.Equal(OwnedPath, result.RemoteFile);
        Assert.Single(remote.Files);
    }

    [Fact]
    public async Task Removal_AmbiguousRecoveryDeletesNothing()
    {
        var remote = new FakeRemoteStorageGuideService();
        remote.Seed(OwnedPath, OwnedBytes());
        remote.Seed("guides/wisp_metagrid_mid_2.build", OwnedBytes());
        var record = RemovalRecord();
        record.RemoteFile = null;
        var result = await CreateGuideSubscriptionService(new FakeGuideProvider(CreateBuild()), remote).RemoveAsync(CreateAccount(), record, CancellationToken.None);
        Assert.False(result.VerifiedAbsent);
        Assert.Equal(0, remote.DeleteCount);
    }

    [Fact]
    public async Task Removal_EnabledAndLegacyDisabledCannotDelete()
    {
        var remote = new FakeRemoteStorageGuideService();
        remote.Seed(OwnedPath, OwnedBytes());
        var service = CreateGuideSubscriptionService(new FakeGuideProvider(CreateBuild()), remote);
        var enabled = RemovalRecord(); enabled.IsEnabled = true;
        var legacy = RemovalRecord(); legacy.RemovalRequested = false; legacy.Status = GuideSubscriptionStatus.Disabled;
        Assert.False((await service.RemoveAsync(CreateAccount(), enabled, CancellationToken.None)).VerifiedAbsent);
        Assert.False((await service.RemoveAsync(CreateAccount(), legacy, CancellationToken.None)).VerifiedAbsent);
        Assert.Equal(0, remote.DeleteCount);
    }

    [Fact]
    public async Task Removal_ThenReenableUsesExistingCreatePipelineWithoutDuplicate()
    {
        var remote = new FakeRemoteStorageGuideService();
        remote.Seed(OwnedPath, OwnedBytes());
        var service = CreateGuideSubscriptionService(new FakeGuideProvider(CreateBuild()), remote);
        var record = RemovalRecord();
        Assert.True(record.ApplyVerifiedRemoval(await service.RemoveAsync(CreateAccount(), record, CancellationToken.None)));
        record.IsEnabled = true;
        var installed = await service.SyncAsync(CreateAccount(), record, CancellationToken.None);
        Assert.True(record.ApplyVerifiedInstallation(installed));
        Assert.Single(remote.Files);
        var unchanged = await service.SyncAsync(CreateAccount(), record, CancellationToken.None);
        Assert.True(record.ApplyVerifiedInstallation(unchanged));
        Assert.Equal(1, remote.WriteCount);
    }

    [Fact]
    public async Task Startup_ReconcilesEnabledAndPendingRemoval_ButNotLegacyDisabled()
    {
        var enabled = CreateSubscription();
        var pending = RemovalRecord(); pending.Role = GuideRole.Support;
        var legacy = RemovalRecord(); legacy.Role = GuideRole.Carry; legacy.RemovalRequested = false; legacy.Status = GuideSubscriptionStatus.Disabled;
        var visited = new List<GuideRole>();
        await GuideStartupSync.RunAsync([enabled, pending, legacy], (_, role, _) => { visited.Add(role); return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal([GuideRole.Mid, GuideRole.Support], visited);
    }

    [Fact]
    public void MyHeroes_AreGroupedFromPersistedRoles_AndRemovalFailureRemainsVisible()
    {
        var first = CreateSubscription();
        var second = RemovalRecord(); second.Role = GuideRole.Support;
        var off = CreateSubscription(); off.HeroId = 14; off.IsEnabled = false;
        var roundtrip = System.Text.Json.JsonSerializer.Deserialize<List<GuideSubscriptionRecord>>(System.Text.Json.JsonSerializer.Serialize(new[] { first, second, off }))!;
        var group = Assert.Single(GuideWorkspace.ConfiguredHeroes(roundtrip));
        Assert.Equal(91, group.Key);
        Assert.Equal(2, group.Count());
    }

    [Theory]
    [InlineData("IO", true)]
    [InlineData(" wIsP ", true)]
    [InlineData("unrelated", false)]
    [InlineData("", true)]
    public void PickerSearch_IsCaseInsensitiveAndDoesNotChangeSubscriptions(string query, bool expected)
    {
        var hero = new HeroDefinition { Id = 91, LocalizedName = "Io", InternalName = "npc_dota_hero_wisp", Slug = "io" };
        Assert.Equal(expected, GuideWorkspace.Matches(hero, query));
    }

    [Fact]
    public void PickerSelection_IsTransient_AndBusyRoleRejectsDoubleToggle()
    {
        var settings = new AppSettings();
        MetaGrid.UI.ViewModels.GuideHeroCardViewModel? selected = null;
        var card = new MetaGrid.UI.ViewModels.GuideHeroCardViewModel(new HeroDefinition { Id = 91, LocalizedName = "Io", InternalName = "npc_dota_hero_wisp", Slug = "io" }, hero => selected = hero);
        card.SelectCommand.Execute(null);
        Assert.Same(card, selected);
        Assert.Empty(settings.GuideSubscriptions);
        var calls = 0;
        var role = new MetaGrid.UI.ViewModels.GuideRoleOptionViewModel(GuideRole.Mid, "Mid", true,
            _ => { calls++; return Task.CompletedTask; }, () => Task.CompletedTask) { IsBusy = true };
        role.IsEnabled = false;
        role.IsEnabled = true;
        Assert.True(role.IsEnabled);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Removal_CancellationRetainsMetadata_AndReleasesOperationGate()
    {
        var remote = new FakeRemoteStorageGuideService();
        remote.Seed(OwnedPath, OwnedBytes());
        var record = RemovalRecord();
        var service = CreateGuideSubscriptionService(new FakeGuideProvider(CreateBuild()), remote);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RemoveAsync(CreateAccount(), record, cancelled.Token));
        Assert.Equal(OwnedPath, record.RemoteFile);
        Assert.Equal(0, remote.DeleteCount);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Assert.True((await service.RemoveAsync(CreateAccount(), record, deadline.Token)).VerifiedAbsent);
    }

    [Fact]
    public async Task RemovalAndInstallation_AreSerialized_WithoutRacingWrites()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probes = 0;
        var remote = new FakeRemoteStorageGuideService
        {
            AvailabilityProbe = async token =>
            {
                Interlocked.Increment(ref probes);
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
                return true;
            }
        };
        remote.Seed(OwnedPath, OwnedBytes());
        var service = CreateGuideSubscriptionService(new FakeGuideProvider(CreateBuild()), remote);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var removal = service.RemoveAsync(CreateAccount(), RemovalRecord(), deadline.Token);
        await entered.Task.WaitAsync(deadline.Token);
        var install = service.SyncAsync(CreateAccount(), CreateSubscription(), deadline.Token);
        try
        {
            Assert.Equal(1, probes);
            Assert.Equal(0, remote.WriteCount);
            Assert.False(install.IsCompleted);
        }
        finally { release.TrySetResult(); }
        Assert.True((await removal).VerifiedAbsent);
        Assert.Equal(GuideSubscriptionStatus.Installed, (await install).Status);
        Assert.Single(remote.Files);
        Assert.Equal(1, remote.WriteCount);
    }
}
