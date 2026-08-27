using System.Runtime.Versioning;
using Microsoft.Win32;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

[SupportedOSPlatform("windows")]
public sealed class SteamLocatorService(ILoggingService loggingService) : ISteamLocatorService
{
    public async Task<IReadOnlyList<SteamInstallation>> LocateAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!settings.AutomaticallyDetectSteam && !string.IsNullOrWhiteSpace(settings.SteamDirectoryOverride))
        {
            candidates.Add(settings.SteamDirectoryOverride);
        }
        else
        {
            AddIfValid(candidates, settings.SteamDirectoryOverride);
            AddIfValid(candidates, TryGetRegistryValue(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"));
            AddIfValid(candidates, TryGetRegistryValue(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"));
            AddIfValid(candidates, @"C:\Program Files (x86)\Steam");
            AddIfValid(candidates, @"C:\Program Files\Steam");
        }

        var installations = candidates
            .Where(IsValidSteamRoot)
            .Select(path => new SteamInstallation { RootPath = path })
            .ToList();

        await loggingService.LogAsync(LogLevelKind.Information, "Steam installation scan completed", new
        {
            candidates = candidates.ToArray(),
            found = installations.Select(x => x.RootPath).ToArray()
        }, cancellationToken);

        return installations;
    }

    private static void AddIfValid(ISet<string> items, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            items.Add(path.Replace('/', '\\'));
        }
    }

    private static bool IsValidSteamRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        return File.Exists(Path.Combine(path, "steam.exe")) &&
               Directory.Exists(Path.Combine(path, "userdata"));
    }

    private static string? TryGetRegistryValue(RegistryKey baseKey, string subKey, string valueName)
    {
        try
        {
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName)?.ToString();
        }
        catch
        {
            return null;
        }
    }
}
