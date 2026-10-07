using System.Globalization;
using System.Text.RegularExpressions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public static partial class GuideDeletionOwnership
{
    // Path shape alone is never deletion authority: exact subscription and Steam creator must also match.
    [GeneratedRegex(@"^guides/[a-z0-9_]+_metagrid_(?:carry|mid|offlane|support|hardsupport)_(?:[2-9]|[1-9][0-9]+)?\.build$", RegexOptions.CultureInvariant)]
    private static partial Regex IoMidFilePattern();

    public static bool IsSafeFile(string? name) => name is not null && IoMidFilePattern().IsMatch(name);

    public static bool IsKnownSubscription(GuideSubscriptionRecord subscription)
        => subscription.HeroId > 0 && Enum.IsDefined(subscription.Role)
            && Regex.IsMatch(subscription.HeroInternalName, "^npc_dota_hero_[a-z0-9_]+$");

    public static string FilePrefix(GuideSubscriptionRecord subscription)
    {
        if (!IsKnownSubscription(subscription)) throw new InvalidOperationException("Invalid subscription identity.");
        return $"guides/{subscription.HeroInternalName[14..]}_metagrid_{subscription.Role.ToString().ToLowerInvariant()}_";
    }

    public static bool MatchesIdentity(GuideSubscriptionRecord subscription, string remoteFile, ParsedValveGuideDocument document)
    {
        if (!IsKnownSubscription(subscription) || !IsSafeFile(remoteFile) || !remoteFile.StartsWith(FilePrefix(subscription), StringComparison.Ordinal)) return false;
        var tokens = document.Overview.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var legacyIo = subscription.HeroId == 91 && subscription.HeroInternalName == "npc_dota_hero_wisp" && subscription.Role == GuideRole.Mid
            && !tokens.Any(t => t.StartsWith("heroId=", StringComparison.Ordinal));
        if (!legacyIo && !tokens.Contains("heroId=" + subscription.HeroId)) return false;
        var role = subscription.Role is GuideRole.Support or GuideRole.HardSupport ? "Support" : "Core";
        return document.HeroToken == subscription.HeroInternalName[14..] && document.RoleToken == "#DOTA_HeroGuide_Role_" + role
            && document.Title == $"MetaGrid D2PT - {subscription.HeroDisplayName} {subscription.Role}"
            && tokens.Contains("MetaGrid") && tokens.Contains($"owner={subscription.HeroDisplayName.Replace(" ", "")}{subscription.Role}")
            && tokens.Contains("role=" + subscription.Role) && tokens.Contains("source=Dota2ProTracker");
    }

    public static bool IsOwned(SteamAccount account, GuideSubscriptionRecord subscription, string remoteFile,
        ParsedValveGuideDocument document)
    {
        if (!IsKnownSubscription(subscription) || !IsSafeFile(remoteFile)) return false;
        if (subscription.InstalledAccountId is not null && subscription.InstalledAccountId != account.AccountId) return false;
        if (subscription.RemoteFile is not null && !string.Equals(subscription.RemoteFile, remoteFile, StringComparison.Ordinal)) return false;
        if (!ulong.TryParse(account.AccountId, out var accountId) || accountId > uint.MaxValue) return false;
        var creator = document.OriginalCreatorIdHex;
        if (!creator.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            || !ulong.TryParse(creator[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var steamId)
            || steamId != 76561197960265728UL + accountId) return false;
        return MatchesIdentity(subscription, remoteFile, document);
    }
}
