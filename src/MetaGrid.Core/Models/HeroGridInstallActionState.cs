namespace MetaGrid.Core.Models;

public sealed record HeroGridInstallActionState(
    bool CanOfferAction,
    string ActionLabel);

public static class HeroGridInstallActionResolver
{
    public static HeroGridInstallActionState Resolve(
        bool hasValidSelectedAccount,
        bool hasAvailableSnapshot,
        string? availableHash,
        string? installedHash,
        GridOriginKind? originKind)
    {
        var hasLiveAvailableGrid =
            !string.IsNullOrWhiteSpace(availableHash) &&
            originKind is not GridOriginKind.Cached;

        var hasInstalledGrid = !string.IsNullOrWhiteSpace(installedHash);
        var hashesMatch = hasLiveAvailableGrid
            && hasInstalledGrid
            && string.Equals(availableHash, installedHash, StringComparison.OrdinalIgnoreCase);

        return new HeroGridInstallActionState(
            CanOfferAction: hasValidSelectedAccount
                && hasAvailableSnapshot
                && hasLiveAvailableGrid
                && !hashesMatch,
            ActionLabel: hasInstalledGrid ? "Update Grid" : "Install Grid");
    }
}
