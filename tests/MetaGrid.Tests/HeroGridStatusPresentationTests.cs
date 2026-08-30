using MetaGrid.Core.Models;

namespace MetaGrid.Tests;

public sealed class HeroGridStatusPresentationTests
{
    [Fact]
    public void Overview_WhenAvailableMatchesInstalled_ShowsGridInstalled()
    {
        var settings = new AppSettings
        {
            LastCheckAt = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero),
            LastRemoteHash = "ABC",
            LastGridOrigin = GridOriginKind.NativeD2pt.ToString()
        };

        var presentation = HeroGridStatusPresenter.CreateOverview(settings, hasAccounts: true, InstalledMetaGridState.Present, "ABC");

        Assert.Equal(HeroGridOverviewState.GridInstalled, presentation.State);
        Assert.Equal("Grid Installed", presentation.Title);
        Assert.Equal("Your hero grid matches the latest Dota2ProTracker High Winrate grid.", presentation.Description);
        Assert.Equal("Grid installed", presentation.Chip);
    }

    [Fact]
    public void Overview_WhenAvailableDiffersFromInstalled_ShowsNewGridAvailable()
    {
        var settings = new AppSettings
        {
            LastCheckAt = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero),
            LastRemoteHash = "NEW",
            LastGridOrigin = GridOriginKind.NativeD2pt.ToString()
        };

        var presentation = HeroGridStatusPresenter.CreateOverview(settings, hasAccounts: true, InstalledMetaGridState.Present, "OLD");

        Assert.Equal("New Grid Available", presentation.Title);
        Assert.Equal("A newer Dota2ProTracker High Winrate grid is ready to install.", presentation.Description);
    }

    [Fact]
    public void Overview_WhenAvailableExistsButNoInstalledGrid_ShowsGridReadyToInstall()
    {
        var settings = new AppSettings
        {
            LastCheckAt = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero),
            LastRemoteHash = "NEW",
            LastGridOrigin = GridOriginKind.NativeD2pt.ToString()
        };

        var presentation = HeroGridStatusPresenter.CreateOverview(settings, hasAccounts: true, InstalledMetaGridState.NoManagedGrid, null);

        Assert.Equal(HeroGridOverviewState.GridReadyToInstall, presentation.State);
        Assert.Equal("Grid Ready to Install", presentation.Title);
        Assert.Equal("The latest Dota2ProTracker High Winrate grid is ready to install for this Steam account.", presentation.Description);
    }

    [Fact]
    public void Overview_WhenSettingsContainStaleInstalledHashButSelectedAccountHasNoManagedGrid_DoesNotShowUpToDate()
    {
        var settings = new AppSettings
        {
            LastCheckAt = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero),
            LastRemoteHash = "ABC",
            LastInstalledHash = "ABC",
            LastGridOrigin = GridOriginKind.NativeD2pt.ToString()
        };

        var presentation = HeroGridStatusPresenter.CreateOverview(settings, hasAccounts: true, InstalledMetaGridState.NoManagedGrid, null);

        Assert.Equal("Grid Ready to Install", presentation.Title);
        Assert.Equal("Grid ready to install", presentation.Chip);
    }

    [Fact]
    public void Overview_WhenSelectedAccountGridFileIsMalformed_ShowsNeedsReview()
    {
        var settings = new AppSettings
        {
            LastCheckAt = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero),
            LastRemoteHash = "ABC",
            LastInstalledHash = "ABC",
            LastGridOrigin = GridOriginKind.NativeD2pt.ToString()
        };

        var presentation = HeroGridStatusPresenter.CreateOverview(settings, hasAccounts: true, InstalledMetaGridState.MalformedFile, null);

        Assert.Equal(HeroGridOverviewState.NeedsReview, presentation.State);
        Assert.Equal("Installed Grid Could Not Be Verified", presentation.Title);
        Assert.Equal("Needs review", presentation.Chip);
    }

    [Fact]
    public void Result_WhenAutomaticFailure_RemainsSchedulerFriendly()
    {
        var result = new UpdateRunResult
        {
            Status = UpdateStatus.Failed,
            Message = "Low-level failure.",
            GridHash = "ABC",
            ProviderStatus = ProviderStatus.Unavailable,
            Trigger = UpdateTriggerKind.Automatic
        };

        var presentation = HeroGridStatusPresenter.CreateFromResult(result);

        Assert.Equal(HeroGridOverviewState.AutomaticUpdateFailed, presentation.State);
        Assert.Equal("Automatic Update Failed", presentation.Title);
        Assert.Equal("MetaGrid could not complete this update attempt. Your installed grid was left unchanged, and automatic updates will try again later.", presentation.Description);
        Assert.DoesNotContain("Scheduler paused", presentation.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Result_WhenWaitingForAccount_ShowsSelectSteamAccount()
    {
        var result = new UpdateRunResult
        {
            Status = UpdateStatus.WaitingForAccount,
            Message = "No account.",
            GridHash = string.Empty,
            ProviderStatus = ProviderStatus.Unknown,
            Trigger = UpdateTriggerKind.ManualCheck
        };

        var presentation = HeroGridStatusPresenter.CreateFromResult(result);

        Assert.Equal(HeroGridOverviewState.ActionRequired, presentation.State);
        Assert.Equal("Select a Steam Account", presentation.Title);
        Assert.Equal("Choose the Steam account MetaGrid should manage before installing hero grid updates.", presentation.Description);
    }

    [Fact]
    public void Result_WhenManualCheckIsInReadOnlyUpdateAvailable_ShowsNewGridAvailable()
    {
        var result = new UpdateRunResult
        {
            Status = UpdateStatus.UpdateAvailable,
            Message = "A fresh hero grid is available.",
            GridHash = "NEW",
            InstalledHash = "OLD",
            AvailableHash = "NEW",
            ProviderStatus = ProviderStatus.Online,
            OriginKind = GridOriginKind.NativeD2pt,
            Trigger = UpdateTriggerKind.ManualCheck
        };

        var presentation = HeroGridStatusPresenter.CreateFromResult(result);

        Assert.Equal(HeroGridOverviewState.UpdateAvailable, presentation.State);
        Assert.Equal("New Grid Available", presentation.Title);
        Assert.Equal("A newer Dota2ProTracker High Winrate grid is ready to install.", presentation.Description);
        Assert.Equal("Update available", presentation.Chip);
    }
}
