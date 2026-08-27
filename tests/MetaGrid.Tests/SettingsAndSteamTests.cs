using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;
using MetaGrid.Tests.TestSupport;
using System.Runtime.Versioning;
using System.Text.Json;

namespace MetaGrid.Tests;

public sealed class SettingsAndSteamTests
{
    [Fact]
    public async Task SettingsService_Persists_AndLoads()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logger = new FileLoggingService(paths);
        var service = new SettingsService(paths, logger);
        var settings = new AppSettings
        {
            AutoUpdateEnabled = false,
            PreferredAccountId = "123",
            BackupRetentionCount = 7,
            SteamDirectoryOverride = @"C:\Steam"
        };

        await service.SaveAsync(settings, CancellationToken.None);
        var loaded = await service.LoadAsync(CancellationToken.None);

        Assert.False(loaded.AutoUpdateEnabled);
        Assert.Equal("123", loaded.PreferredAccountId);
        Assert.Equal(7, loaded.BackupRetentionCount);
        Assert.Equal(@"C:\Steam", loaded.SteamDirectoryOverride);
    }

    [Fact]
    public async Task SettingsService_RecoversFromEmptyFile()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.SettingsFilePath)!);
        await File.WriteAllTextAsync(paths.SettingsFilePath, string.Empty);

        var service = new SettingsService(paths, new FileLoggingService(paths));
        var loaded = await service.LoadAsync(CancellationToken.None);

        Assert.True(File.Exists(paths.SettingsFilePath));
        Assert.True(loaded.AutomaticallyDetectSteam);
        Assert.Contains(Directory.GetFiles(Path.GetDirectoryName(paths.SettingsFilePath)!).Select(Path.GetFileName), name => name is not null && name.Contains(".corrupt-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SettingsService_LoadsOldSettingsJson_WithObsoleteDotaWritesEnabledField()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.SettingsFilePath)!);
        await File.WriteAllTextAsync(paths.SettingsFilePath, """
        {
          "autoUpdateEnabled": true,
          "preferredAccountId": "123456789",
          "dotaWritesEnabled": false,
          "lastRemoteHash": "ABC123"
        }
        """);

        var service = new SettingsService(paths, new FileLoggingService(paths));
        var loaded = await service.LoadAsync(CancellationToken.None);

        Assert.True(loaded.AutoUpdateEnabled);
        Assert.Equal("123456789", loaded.PreferredAccountId);
        Assert.Equal("ABC123", loaded.LastRemoteHash);
    }

    [Fact]
    public async Task SettingsService_SaveAsync_DoesNotWriteObsoleteDotaWritesEnabledField()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var service = new SettingsService(paths, new FileLoggingService(paths));

        await service.SaveAsync(new AppSettings
        {
            AutoUpdateEnabled = true,
            PreferredAccountId = "123456789"
        }, CancellationToken.None);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(paths.SettingsFilePath));
        Assert.False(document.RootElement.TryGetProperty("dotaWritesEnabled", out _));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task SteamAccountService_ReadsPersonaNameFromLocalConfig()
    {
        using var temp = new TemporaryDirectory();
        var steamRoot = Path.Combine(temp.Path, "Steam");
        var accountDirectory = Path.Combine(steamRoot, "userdata", "76561198000000000");
        var dotaDirectory = Path.Combine(accountDirectory, "570", "remote", "cfg");
        var localConfigPath = Path.Combine(accountDirectory, "config", "localconfig.vdf");
        Directory.CreateDirectory(dotaDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(localConfigPath)!);
        await File.WriteAllTextAsync(Path.Combine(steamRoot, "steam.exe"), string.Empty);
        await File.WriteAllTextAsync(localConfigPath, "\"friends\"\n{\n\t\"PersonaName\"\t\t\"Meta Tester\"\n}");
        await File.WriteAllTextAsync(Path.Combine(dotaDirectory, "hero_grid_config.json"), "{\"version\":3,\"configs\":[]}");

        var settings = new AppSettings
        {
            AutomaticallyDetectSteam = false,
            SteamDirectoryOverride = steamRoot
        };

        var locator = new SteamLocatorService(new FileLoggingService(new TestAppPaths(Path.Combine(temp.Path, "logs"))));
        var accounts = await new SteamAccountService(locator, new DotaGridService()).DetectAccountsAsync(settings, CancellationToken.None);

        var detected = Assert.Single(accounts);
        Assert.Equal("Meta Tester", detected.DisplayName);
        Assert.True(detected.HasHeroGridConfig);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task SteamLocatorService_IgnoresDirectoriesWithoutSteamExecutable()
    {
        using var temp = new TemporaryDirectory();
        var steamRoot = Path.Combine(temp.Path, "Steam");
        Directory.CreateDirectory(Path.Combine(steamRoot, "userdata"));

        var settings = new AppSettings
        {
            AutomaticallyDetectSteam = false,
            SteamDirectoryOverride = steamRoot
        };

        var locator = new SteamLocatorService(new FileLoggingService(new TestAppPaths(Path.Combine(temp.Path, "logs"))));
        var installations = await locator.LocateAsync(settings, CancellationToken.None);

        Assert.Empty(installations);
    }
}
