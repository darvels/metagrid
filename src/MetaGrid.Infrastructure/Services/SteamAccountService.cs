using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using System.Text.RegularExpressions;

namespace MetaGrid.Infrastructure.Services;

public sealed class SteamAccountService(
    ISteamLocatorService locatorService,
    IDotaGridService dotaGridService) : ISteamAccountService
{
    public async Task<IReadOnlyList<SteamAccount>> DetectAccountsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var installations = await locatorService.LocateAsync(settings, cancellationToken);
        var accounts = new List<SteamAccount>();

        foreach (var installation in installations)
        {
            if (!Directory.Exists(installation.UserDataPath))
            {
                continue;
            }

            foreach (var accountDirectory in Directory.EnumerateDirectories(installation.UserDataPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var accountId = Path.GetFileName(accountDirectory);
                var dotaConfigDirectory = Path.Combine(accountDirectory, "570", "remote", "cfg");
                if (!Directory.Exists(dotaConfigDirectory))
                {
                    continue;
                }

                var gridPath = Path.Combine(dotaConfigDirectory, "hero_grid_config.json");
                var personaName = TryReadPersonaName(accountDirectory);
                var account = new SteamAccount
                {
                    AccountId = accountId,
                    SteamRootPath = installation.RootPath,
                    UserDataPath = accountDirectory,
                    DotaConfigDirectory = dotaConfigDirectory,
                    DisplayName = string.IsNullOrWhiteSpace(personaName) ? $"Steam {accountId}" : personaName,
                    PersonaName = personaName,
                    HasDotaUserData = true,
                    HasHeroGridConfig = File.Exists(gridPath),
                    IsSelected = settings.SelectedAccountIds.Contains(accountId) ||
                                 string.Equals(settings.PreferredAccountId, accountId, StringComparison.OrdinalIgnoreCase)
                };

                var inspection = await dotaGridService.InspectInstalledMetaGridAsync(gridPath, cancellationToken);
                account.CurrentMetaGridHash = inspection.InstalledHash;
                account.InstalledMetaGridState = inspection.State;
                account.InstalledMetaGridError = inspection.Error;
                accounts.Add(account);
            }
        }

        if (accounts.Count > 0 && accounts.All(x => !x.IsSelected))
        {
            accounts[0].IsSelected = true;
        }

        return accounts.OrderBy(x => x.AccountId).ToList();
    }

    private static string? TryReadPersonaName(string accountDirectory)
    {
        var localConfigPath = Path.Combine(accountDirectory, "config", "localconfig.vdf");
        if (!File.Exists(localConfigPath))
        {
            return null;
        }

        try
        {
            var contents = File.ReadAllText(localConfigPath);
            var match = Regex.Match(contents, "\"PersonaName\"\\s+\"(?<name>[^\"]+)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["name"].Value.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
