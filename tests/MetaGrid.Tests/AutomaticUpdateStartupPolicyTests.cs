using MetaGrid.Core.Models;

namespace MetaGrid.Tests;

public sealed class AutomaticUpdateStartupPolicyTests
{
    [Fact]
    public void CreateDecision_ReturnsDisabled_WhenAutomaticUpdatesAreOff()
    {
        var settings = new AppSettings
        {
            AutoUpdateEnabled = false,
            UpdateInterval = UpdateInterval.FifteenMinutes
        };

        var decision = AutomaticUpdateStartupPolicy.CreateDecision(
            settings,
            new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));

        Assert.False(decision.ShouldRunStartupCycle);
        Assert.Null(decision.StartupDelay);
        Assert.Null(decision.NextScheduledCheckAt);
    }

    [Fact]
    public void CreateDecision_RunsStartupCycle_WhenNoRecentLiveCheckExists()
    {
        var settings = new AppSettings
        {
            AutoUpdateEnabled = true,
            UpdateInterval = UpdateInterval.FifteenMinutes,
            LastSuccessfulLiveProviderCheckAt = new DateTimeOffset(2026, 8, 23, 11, 0, 0, TimeSpan.Zero)
        };

        var decision = AutomaticUpdateStartupPolicy.CreateDecision(
            settings,
            new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));

        Assert.True(decision.ShouldRunStartupCycle);
        Assert.Equal(AutomaticUpdateStartupPolicy.StartupDelay, decision.StartupDelay);
        Assert.Null(decision.NextScheduledCheckAt);
    }

    [Fact]
    public void CreateDecision_SkipsStartupCycle_WhenRecentLiveCheckIsWithinCooldown()
    {
        var now = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        var lastLiveCheckAt = now.AddMinutes(-2);
        var settings = new AppSettings
        {
            AutoUpdateEnabled = true,
            UpdateInterval = UpdateInterval.FifteenMinutes,
            LastSuccessfulLiveProviderCheckAt = lastLiveCheckAt
        };

        var decision = AutomaticUpdateStartupPolicy.CreateDecision(settings, now);

        Assert.False(decision.ShouldRunStartupCycle);
        Assert.Null(decision.StartupDelay);
        Assert.Equal(lastLiveCheckAt.AddMinutes(15), decision.NextScheduledCheckAt);
    }

    [Fact]
    public void ComputeNextScheduledCheckAt_UsesCompletionTimeAnchor()
    {
        var completedAt = new DateTimeOffset(2026, 8, 23, 12, 0, 8, TimeSpan.Zero);

        var nextCheckAt = AutomaticUpdateStartupPolicy.ComputeNextScheduledCheckAt(
            completedAt,
            UpdateInterval.FifteenMinutes);

        Assert.Equal(new DateTimeOffset(2026, 8, 23, 12, 15, 8, TimeSpan.Zero), nextCheckAt);
    }

    [Fact]
    public void IsSuccessfulLiveProviderCheck_ReturnsTrue_ForLiveOnlineAutomaticResult()
    {
        var result = new UpdateRunResult
        {
            Status = UpdateStatus.AlreadyUpToDate,
            Message = "No change.",
            GridHash = "ABC123",
            ProviderStatus = ProviderStatus.Online,
            OriginKind = GridOriginKind.AlternativeProvider,
            SourceName = "OpenDota",
            SourceStrategy = "Winrate"
        };

        Assert.True(AutomaticUpdateStartupPolicy.IsSuccessfulLiveProviderCheck(result));
    }

    [Fact]
    public void IsSuccessfulLiveProviderCheck_ReturnsFalse_ForCachedOrFailedResults()
    {
        var cachedResult = new UpdateRunResult
        {
            Status = UpdateStatus.SourceUnavailable,
            Message = "Cached only.",
            GridHash = "ABC123",
            ProviderStatus = ProviderStatus.Cached,
            OriginKind = GridOriginKind.Cached,
            SourceName = "Cache",
            SourceStrategy = "LastKnownGood"
        };

        var failedResult = new UpdateRunResult
        {
            Status = UpdateStatus.Failed,
            Message = "Validation failed.",
            GridHash = "ABC123",
            ProviderStatus = ProviderStatus.Online,
            OriginKind = GridOriginKind.NativeD2pt,
            SourceName = "D2PT",
            SourceStrategy = "Native"
        };

        Assert.False(AutomaticUpdateStartupPolicy.IsSuccessfulLiveProviderCheck(cachedResult));
        Assert.False(AutomaticUpdateStartupPolicy.IsSuccessfulLiveProviderCheck(failedResult));
    }
}
