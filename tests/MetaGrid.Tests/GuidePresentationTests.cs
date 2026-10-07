using MetaGrid.Core.Models;
using MetaGrid.Core.Services;
using MetaGrid.UI.ViewModels;

namespace MetaGrid.Tests;

public sealed class GuidePresentationTests
{
    [Fact]
    public async Task Toggles_AreIndependent_AndDoNotSyncOnConstruction()
    {
        var changes = new List<(GuideRole Role, bool Enabled)>();
        var roles = Enum.GetValues<GuideRole>().Select(role => new GuideRoleOptionViewModel(role, role.ToString(), false,
            enabled => { changes.Add((role, enabled)); return Task.CompletedTask; }, () => Task.CompletedTask)).ToArray();
        Assert.Empty(changes);
        roles[0].IsEnabled = true;
        roles[1].IsEnabled = true;
        roles[0].IsEnabled = false;
        await Task.WhenAll(roles.Select(role => role.ToggleTask));
        Assert.True(roles[1].IsEnabled);
        Assert.False(roles[0].IsEnabled);
        Assert.Equal(3, changes.Count);
        roles[1].IsEnabled = true;
        Assert.Equal(3, changes.Count);
    }

    [Fact]
    public async Task Startup_EnumeratesEveryEnabledHeroRole_Once_WithoutDisabledEntries()
    {
        var records = new[]
        {
            new GuideSubscriptionRecord { HeroId = 91, Role = GuideRole.Mid, IsEnabled = true },
            new GuideSubscriptionRecord { HeroId = 1, Role = GuideRole.Carry, IsEnabled = true },
            new GuideSubscriptionRecord { HeroId = 91, Role = GuideRole.Mid, IsEnabled = true },
            new GuideSubscriptionRecord { HeroId = 91, Role = GuideRole.Support, IsEnabled = false }
        };
        var seen = new List<string>();
        await GuideStartupSync.RunAsync(records, (id, role, _) =>
        {
            seen.Add(GuideSubscriptionRecord.CreateStableKey(id, role));
            return Task.CompletedTask;
        }, CancellationToken.None);
        Assert.Equal(new[] { "91:Mid", "1:Carry" }, seen);
    }

    [Fact]
    public async Task Startup_CancellationStopsFurtherSubscriptions()
    {
        using var cts = new CancellationTokenSource();
        var records = Enum.GetValues<GuideRole>().Select(role => new GuideSubscriptionRecord { HeroId = 91, Role = role, IsEnabled = true });
        var count = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GuideStartupSync.RunAsync(records, (_, _, _) =>
        {
            count++;
            cts.Cancel();
            return Task.CompletedTask;
        }, cts.Token));
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData(false, GuideSubscriptionStatus.Installed, "Off")]
    [InlineData(true, GuideSubscriptionStatus.Installed, "Success")]
    [InlineData(true, GuideSubscriptionStatus.UpToDate, "Success")]
    [InlineData(true, GuideSubscriptionStatus.Installing, "Busy")]
    [InlineData(true, GuideSubscriptionStatus.MappingFailed, "Warning")]
    [InlineData(true, GuideSubscriptionStatus.Unsupported, "Warning")]
    public void Tone_MatchesActualSubscription(bool enabled, GuideSubscriptionStatus status, string expected)
        => Assert.Equal(expected, GuideStatusPresentation.Tone(enabled, status));

    [Fact]
    public void BusyChip_DisablesFurtherActions_AndNotifiesTooltip()
    {
        var chip = new GuideRoleOptionViewModel(GuideRole.Mid, "Mid", true, _ => Task.CompletedTask, () => Task.CompletedTask);
        var changed = new List<string?>();
        chip.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        chip.IsBusy = true;
        chip.StatusChip = "Syncing";
        Assert.False(chip.CanToggle);
        Assert.False(chip.CanSync);
        Assert.Equal("Mid: Syncing", chip.StatusTooltip);
        Assert.Contains(nameof(chip.StatusTooltip), changed);
    }

    [Fact]
    public void HeroIcons_UseSharedCatalog_AndUnknownIdsRemainVisible()
    {
        var hero = new HeroDefinition { Id = 91, LocalizedName = "Io", InternalName = "npc_dota_hero_wisp", Slug = "io", IconPath = "/apps/dota2/images/dota_react/heroes/icons/wisp.png" };
        var catalog = new Dictionary<int, HeroDefinition> { [91] = hero };
        var icon = HeroIconPresentation.Create(91, catalog);
        var card = new GuideHeroCardViewModel(hero, _ => { });
        Assert.Equal(card.IconUrl, icon.IconUrl);
        Assert.Equal("Io", icon.Name);
        var missing = HeroIconPresentation.Create(999, catalog);
        Assert.Equal("999", missing.Fallback);
        Assert.Equal("Hero 999", missing.Name);
        Assert.Empty(missing.IconUrl);
        Assert.Empty(HeroImageSource.ToCdnUrl(null));
    }

    [Fact]
    public void NewCopy_AndAllStatusLabels_AreLocalized()
    {
        var en = new UiTextService();
        var ru = new UiTextService { Language = AppLanguage.Russian };
        Assert.NotEqual(en.GuidesDescription, ru.GuidesDescription);
        Assert.NotEqual(en.GuideCatalog, ru.GuideCatalog);
        Assert.NotEqual(en.GuideRoles, ru.GuideRoles);
        Assert.NotEqual(en.GuideRolesHint, ru.GuideRolesHint);
        foreach (var status in Enum.GetValues<GuideSubscriptionStatus>())
            Assert.NotEqual(GuideStatusPresentation.Label(en, true, status), GuideStatusPresentation.Label(ru, true, status));
    }
}
