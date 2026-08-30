namespace MetaGrid.Core.Models;

public sealed record HeroGridStatusPresentation(
    HeroGridOverviewState State,
    string Title,
    string Description,
    string Chip);

public enum HeroGridOverviewState
{
    NotChecked,
    ActionRequired,
    CachedData,
    NeedsReview,
    GridReadyToInstall,
    GridInstalled,
    UpdateAvailable,
    SourceUnavailable,
    UpdateCheckFailed,
    AutomaticUpdateFailed,
    RestoreFailed
}

public static class HeroGridStatusPresenter
{
    public static HeroGridStatusPresentation CreateOverview(
        AppSettings settings,
        bool hasAccounts,
        InstalledMetaGridState installedState,
        string? installedHash)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!hasAccounts)
        {
            return new HeroGridStatusPresentation(
                HeroGridOverviewState.ActionRequired,
                "Select a Steam Account",
                "Choose the Steam account MetaGrid should manage before installing hero grid updates.",
                "Action required");
        }

        var hasChecked = settings.LastCheckAt.HasValue;
        var availableHash = settings.LastEffectiveGridHash ?? settings.LastRemoteHash;
        var hasAvailableGrid = !string.IsNullOrWhiteSpace(availableHash);
        var hasInstalledGrid = installedState == InstalledMetaGridState.Present && !string.IsNullOrWhiteSpace(installedHash);
        var availableMatchesInstalled = hasAvailableGrid
            && hasInstalledGrid
            && string.Equals(availableHash, installedHash, StringComparison.OrdinalIgnoreCase);
        var originKind = ParseOrigin(settings.LastGridOrigin);
        var isCachedOnly = originKind == GridOriginKind.Cached;
        var hasLiveProvider = hasChecked && hasAvailableGrid && originKind is not GridOriginKind.Cached;

        if (!hasChecked && !hasAvailableGrid)
        {
            return new HeroGridStatusPresentation(
                HeroGridOverviewState.NotChecked,
                "Not checked yet",
                "MetaGrid is ready to check Dota2ProTracker for the latest High Winrate grid.",
                "Not checked");
        }

        if (isCachedOnly)
        {
            var description = settings.LastGridCapturedAt is { } capturedAt
                ? $"Showing the last valid Dota2ProTracker grid from {capturedAt.LocalDateTime:g} while the source is unavailable."
                : "Showing the last valid Dota2ProTracker grid while the source is unavailable.";
            return new HeroGridStatusPresentation(
                HeroGridOverviewState.CachedData,
                "Dota2ProTracker Unavailable",
                description,
                "Using cached grid");
        }

        if (installedState == InstalledMetaGridState.MalformedFile)
        {
            return new HeroGridStatusPresentation(
                HeroGridOverviewState.NeedsReview,
                "Installed Grid Could Not Be Verified",
                "MetaGrid found hero_grid_config.json for the selected Steam account, but the file could not be parsed safely. No install state is being inferred from cached metadata.",
                "Needs review");
        }

        if (!hasInstalledGrid && hasAvailableGrid)
        {
            return new HeroGridStatusPresentation(
                HeroGridOverviewState.GridReadyToInstall,
                "Grid Ready to Install",
                "The latest Dota2ProTracker High Winrate grid is ready to install for this Steam account.",
                "Grid ready to install");
        }

        if (hasLiveProvider && availableMatchesInstalled)
        {
            return new HeroGridStatusPresentation(
                HeroGridOverviewState.GridInstalled,
                "Grid Installed",
                "Your hero grid matches the latest Dota2ProTracker High Winrate grid.",
                "Grid installed");
        }

        if (hasLiveProvider && hasAvailableGrid && hasInstalledGrid && !availableMatchesInstalled)
        {
            return new HeroGridStatusPresentation(
                HeroGridOverviewState.UpdateAvailable,
                "New Grid Available",
                "A newer Dota2ProTracker High Winrate grid is ready to install.",
                "Update available");
        }

        if (hasAvailableGrid)
        {
            return new HeroGridStatusPresentation(
                HeroGridOverviewState.GridReadyToInstall,
                "Grid Ready to Install",
                "The latest Dota2ProTracker High Winrate grid is ready to install for this Steam account.",
                "Grid ready to install");
        }

        return new HeroGridStatusPresentation(
            HeroGridOverviewState.SourceUnavailable,
            "Dota2ProTracker Unavailable",
            "MetaGrid could not reach Dota2ProTracker right now.",
            "Source unavailable");
    }

    public static HeroGridStatusPresentation CreateFromResult(UpdateRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.OriginKind == GridOriginKind.Cached)
        {
            return new HeroGridStatusPresentation(
                HeroGridOverviewState.CachedData,
                "Dota2ProTracker Unavailable",
                result.RetrievedAt is { } capturedAt
                    ? $"Showing the last valid Dota2ProTracker grid from {capturedAt.LocalDateTime:g} while the source is unavailable."
                    : "Showing the last valid Dota2ProTracker grid while the source is unavailable.",
                "Using cached grid");
        }

        return result.Status switch
        {
            UpdateStatus.Updated or UpdateStatus.AlreadyUpToDate => new HeroGridStatusPresentation(
                HeroGridOverviewState.GridInstalled,
                "Grid Installed",
                "Your hero grid matches the latest Dota2ProTracker High Winrate grid.",
                "Grid installed"),

            UpdateStatus.UpdateAvailable => string.IsNullOrWhiteSpace(result.InstalledHash)
                ? new HeroGridStatusPresentation(
                    HeroGridOverviewState.GridReadyToInstall,
                    "Grid Ready to Install",
                    "The latest Dota2ProTracker High Winrate grid is ready to install for this Steam account.",
                    "Grid ready to install")
                : new HeroGridStatusPresentation(
                    result.Trigger == UpdateTriggerKind.Automatic ? HeroGridOverviewState.AutomaticUpdateFailed : HeroGridOverviewState.UpdateAvailable,
                    result.Trigger == UpdateTriggerKind.Automatic ? "Automatic Update Failed" : "New Grid Available",
                    result.Trigger == UpdateTriggerKind.Automatic
                        ? "MetaGrid could not complete this update attempt. Your installed grid was left unchanged, and automatic updates will try again later."
                        : "A newer Dota2ProTracker High Winrate grid is ready to install.",
                    result.Trigger == UpdateTriggerKind.Automatic ? "Auto update failed" : "Update available"),

            UpdateStatus.SourceUnavailable => new HeroGridStatusPresentation(
                HeroGridOverviewState.SourceUnavailable,
                "Dota2ProTracker Unavailable",
                result.OriginKind == GridOriginKind.Cached
                    ? (result.RetrievedAt is { } cachedAt
                        ? $"Showing the last valid Dota2ProTracker grid from {cachedAt.LocalDateTime:g} while the source is unavailable."
                        : "Showing the last valid Dota2ProTracker grid while the source is unavailable.")
                    : "MetaGrid could not reach Dota2ProTracker right now.",
                "Source unavailable"),

            UpdateStatus.WaitingForAccount => new HeroGridStatusPresentation(
                HeroGridOverviewState.ActionRequired,
                "Select a Steam Account",
                "Choose the Steam account MetaGrid should manage before installing hero grid updates.",
                "Action required"),

            UpdateStatus.BackupRestored => new HeroGridStatusPresentation(
                HeroGridOverviewState.RestoreFailed,
                "Grid Update Failed",
                "MetaGrid could not install the new grid. Your previous Dota 2 grid was restored or left unchanged.",
                "Restore failed"),

            UpdateStatus.ParsingFailed or UpdateStatus.UnexpectedResponse => new HeroGridStatusPresentation(
                result.Trigger == UpdateTriggerKind.Automatic ? HeroGridOverviewState.AutomaticUpdateFailed : HeroGridOverviewState.UpdateCheckFailed,
                result.Trigger == UpdateTriggerKind.Automatic ? "Automatic Update Failed" : "Update Check Failed",
                result.Trigger == UpdateTriggerKind.Automatic
                    ? "MetaGrid could not complete this update attempt. Your installed grid was left unchanged, and automatic updates will try again later."
                    : "MetaGrid could not check Dota2ProTracker right now. Your installed grid was left unchanged.",
                result.Trigger == UpdateTriggerKind.Automatic ? "Auto update failed" : "Failed"),

            _ => new HeroGridStatusPresentation(
                result.Trigger == UpdateTriggerKind.Automatic ? HeroGridOverviewState.AutomaticUpdateFailed : HeroGridOverviewState.UpdateCheckFailed,
                result.Trigger == UpdateTriggerKind.Automatic ? "Automatic Update Failed" : "Update Check Failed",
                result.Trigger == UpdateTriggerKind.Automatic
                    ? "MetaGrid could not complete this update attempt. Your installed grid was left unchanged, and automatic updates will try again later."
                    : "MetaGrid could not check Dota2ProTracker right now. Your installed grid was left unchanged.",
                result.Trigger == UpdateTriggerKind.Automatic ? "Auto update failed" : "Failed")
        };
    }

    private static GridOriginKind? ParseOrigin(string? value)
        => Enum.TryParse<GridOriginKind>(value, out var origin) ? origin : null;
}
