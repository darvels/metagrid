namespace MetaGrid.Core.Models;

public static class AutomaticUpdateStartupPolicy
{
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan RapidRestartCooldown = TimeSpan.FromMinutes(5);

    public static AutomaticUpdateStartupDecision CreateDecision(AppSettings settings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.AutoUpdateEnabled)
        {
            return new AutomaticUpdateStartupDecision(false, null, null, "Automatic updates disabled");
        }

        var interval = TimeSpan.FromMinutes((int)settings.UpdateInterval);
        var lastSuccessfulLiveCheckAt = settings.LastSuccessfulLiveProviderCheckAt;
        if (lastSuccessfulLiveCheckAt.HasValue)
        {
            var elapsedSinceLiveCheck = now - lastSuccessfulLiveCheckAt.Value;
            if (elapsedSinceLiveCheck >= TimeSpan.Zero && elapsedSinceLiveCheck < RapidRestartCooldown)
            {
                var nextCheckAt = lastSuccessfulLiveCheckAt.Value.Add(interval);
                if (nextCheckAt <= now)
                {
                    nextCheckAt = now.Add(interval);
                }

                return new AutomaticUpdateStartupDecision(
                    ShouldRunStartupCycle: false,
                    StartupDelay: null,
                    NextScheduledCheckAt: nextCheckAt,
                    Reason: "Recent successful live provider check");
            }
        }

        return new AutomaticUpdateStartupDecision(true, StartupDelay, null, "Startup automatic update required");
    }

    public static DateTimeOffset ComputeNextScheduledCheckAt(DateTimeOffset completedAt, UpdateInterval interval)
        => completedAt.Add(TimeSpan.FromMinutes((int)interval));

    public static bool IsSuccessfulLiveProviderCheck(UpdateRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.ProviderStatus == ProviderStatus.Online
            && result.OriginKind is not GridOriginKind.Cached
            && result.Status is UpdateStatus.AlreadyUpToDate or UpdateStatus.Updated or UpdateStatus.UpdateAvailable;
    }
}

public sealed record AutomaticUpdateStartupDecision(
    bool ShouldRunStartupCycle,
    TimeSpan? StartupDelay,
    DateTimeOffset? NextScheduledCheckAt,
    string Reason);
