namespace MetaGrid.Core.Models;

public sealed record HeroGridStatusPresentation(
    string Title,
    string Description,
    string Chip);

public static class HeroGridStatusPresenter
{
    public static HeroGridStatusPresentation CreateOverview(AppSettings settings, bool hasAccounts)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!hasAccounts)
        {
            return new HeroGridStatusPresentation(
                "Select a Steam Account",
                "Choose the Steam account MetaGrid should manage before installing hero grid updates.",
                "Action required");
        }

        var hasChecked = settings.LastCheckAt.HasValue;
        var hasAvailableGrid = !string.IsNullOrWhiteSpace(settings.LastRemoteHash);
        var hasInstalledGrid = !string.IsNullOrWhiteSpace(settings.LastInstalledHash);
        var availableMatchesInstalled = hasAvailableGrid
            && hasInstalledGrid
            && string.Equals(settings.LastRemoteHash, settings.LastInstalledHash, StringComparison.OrdinalIgnoreCase);
        var originKind = ParseOrigin(settings.LastGridOrigin);
        var isCachedOnly = originKind == GridOriginKind.Cached;
        var hasLiveProvider = hasChecked && hasAvailableGrid && originKind == GridOriginKind.NativeD2pt;

        if (!hasChecked && !hasAvailableGrid)
        {
            return new HeroGridStatusPresentation(
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
                "Dota2ProTracker Unavailable",
                description,
                "Using cached grid");
        }

        if (!hasInstalledGrid && hasAvailableGrid)
        {
            return new HeroGridStatusPresentation(
                "Grid Ready to Install",
                "The latest Dota2ProTracker High Winrate grid is ready to install for this Steam account.",
                hasLiveProvider ? "Ready to install" : "Available");
        }

        if (hasLiveProvider && availableMatchesInstalled)
        {
            return new HeroGridStatusPresentation(
                "Latest Grid Installed",
                "Your hero grid matches the latest Dota2ProTracker High Winrate grid.",
                "Up to date");
        }

        if (hasLiveProvider && hasAvailableGrid && hasInstalledGrid && !availableMatchesInstalled)
        {
            return new HeroGridStatusPresentation(
                "New Grid Available",
                "A newer Dota2ProTracker High Winrate grid is ready to install.",
                "Update available");
        }

        if (hasAvailableGrid)
        {
            return new HeroGridStatusPresentation(
                "Grid Ready to Install",
                "The latest Dota2ProTracker High Winrate grid is ready to install for this Steam account.",
                "Available");
        }

        return new HeroGridStatusPresentation(
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
                "Dota2ProTracker Unavailable",
                result.RetrievedAt is { } capturedAt
                    ? $"Showing the last valid Dota2ProTracker grid from {capturedAt.LocalDateTime:g} while the source is unavailable."
                    : "Showing the last valid Dota2ProTracker grid while the source is unavailable.",
                "Using cached grid");
        }

        return result.Status switch
        {
            UpdateStatus.Updated or UpdateStatus.AlreadyUpToDate => new HeroGridStatusPresentation(
                "Latest Grid Installed",
                "Your hero grid matches the latest Dota2ProTracker High Winrate grid.",
                "Up to date"),

            UpdateStatus.UpdateAvailable => string.IsNullOrWhiteSpace(result.InstalledHash)
                ? new HeroGridStatusPresentation(
                    "Grid Ready to Install",
                    "The latest Dota2ProTracker High Winrate grid is ready to install for this Steam account.",
                    "Ready to install")
                : new HeroGridStatusPresentation(
                    result.Trigger == UpdateTriggerKind.Automatic ? "Automatic Update Failed" : "New Grid Available",
                    result.Trigger == UpdateTriggerKind.Automatic
                        ? "MetaGrid could not complete this update attempt. Your installed grid was left unchanged, and automatic updates will try again later."
                        : "A newer Dota2ProTracker High Winrate grid is ready to install.",
                    result.Trigger == UpdateTriggerKind.Automatic ? "Auto update failed" : "Update available"),

            UpdateStatus.SourceUnavailable => new HeroGridStatusPresentation(
                "Dota2ProTracker Unavailable",
                result.OriginKind == GridOriginKind.Cached
                    ? (result.RetrievedAt is { } cachedAt
                        ? $"Showing the last valid Dota2ProTracker grid from {cachedAt.LocalDateTime:g} while the source is unavailable."
                        : "Showing the last valid Dota2ProTracker grid while the source is unavailable.")
                    : "MetaGrid could not reach Dota2ProTracker right now.",
                "Source unavailable"),

            UpdateStatus.WaitingForAccount => new HeroGridStatusPresentation(
                "Select a Steam Account",
                "Choose the Steam account MetaGrid should manage before installing hero grid updates.",
                "Action required"),

            UpdateStatus.BackupRestored => new HeroGridStatusPresentation(
                "Grid Update Failed",
                "MetaGrid could not install the new grid. Your previous Dota 2 grid was restored or left unchanged.",
                "Restore failed"),

            UpdateStatus.ParsingFailed or UpdateStatus.UnexpectedResponse => new HeroGridStatusPresentation(
                result.Trigger == UpdateTriggerKind.Automatic ? "Automatic Update Failed" : "Update Check Failed",
                result.Trigger == UpdateTriggerKind.Automatic
                    ? "MetaGrid could not complete this update attempt. Your installed grid was left unchanged, and automatic updates will try again later."
                    : "MetaGrid could not check Dota2ProTracker right now. Your installed grid was left unchanged.",
                result.Trigger == UpdateTriggerKind.Automatic ? "Auto update failed" : "Failed"),

            _ => new HeroGridStatusPresentation(
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
