namespace MetaGrid.Core.Models;

public sealed class SteamInstallation
{
    public required string RootPath { get; init; }
    public string UserDataPath => Path.Combine(RootPath, "userdata");
}

public sealed class SteamAccount
{
    public required string AccountId { get; init; }
    public required string SteamRootPath { get; init; }
    public required string UserDataPath { get; init; }
    public required string DotaConfigDirectory { get; init; }
    public string HeroGridConfigPath => Path.Combine(DotaConfigDirectory, "hero_grid_config.json");
    public string DisplayName { get; set; } = string.Empty;
    public string? PersonaName { get; set; }
    public bool HasDotaUserData { get; set; }
    public bool HasHeroGridConfig { get; set; }
    public bool IsSelected { get; set; }
    public string? CurrentMetaGridHash { get; set; }
    public InstalledMetaGridState InstalledMetaGridState { get; set; } = InstalledMetaGridState.Unknown;
    public string? InstalledMetaGridError { get; set; }
}
