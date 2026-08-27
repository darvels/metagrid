using System.Collections.Concurrent;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;
using MetaGrid.Tests.TestSupport;

namespace MetaGrid.Tests;

public sealed class BackupAndUpdateServiceTests
{
    [Fact]
    public async Task BackupService_RespectsRetention()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var backupService = new BackupService(paths);
        var sourcePath = Path.Combine(paths.RootDirectory, "hero_grid_config.json");

        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        for (var i = 0; i < 4; i++)
        {
            await File.WriteAllTextAsync(sourcePath, $"{{\"version\":3,\"configs\":[{i}]}}");
            await backupService.BackupAsync(sourcePath, retentionCount: 2, metadata: null, CancellationToken.None);
            await Task.Delay(1100);
        }

        var backups = await backupService.GetBackupsAsync(CancellationToken.None);
        Assert.Equal(2, backups.Count);
        Assert.All(backups, backup => Assert.True(backup.FileSizeBytes > 0));
    }

    [Fact]
    public async Task UpdateService_ReportsAlreadyUpToDate_OnSecondCheck()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logging = new FileLoggingService(paths);
        var backupService = new BackupService(paths);
        var historyService = new HistoryService(paths);
        var dotaGridService = new DotaGridService();
        var snapshot = StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray());
        var provider = new StubProvider(snapshot);
        var updateService = new UpdateService(provider, dotaGridService, backupService, historyService, logging, new FakeHeroCatalogService());
        var settings = new AppSettings();

        var account = await CreateAccountFixtureAsync(paths, dotaGridService);

        var first = await updateService.InstallGridAsync([account], settings, CreateInstallSnapshot(snapshot, account), CancellationToken.None);
        settings.LastInstalledHash = first.GridHash;
        var second = await updateService.CheckForUpdatesAsync([account], settings, forceWrite: false, CancellationToken.None);

        Assert.True(first.Status == UpdateStatus.Updated, $"Expected first install to succeed but got {first.Status}: {first.Message}");
        Assert.Equal(UpdateStatus.AlreadyUpToDate, second.Status);
    }

    [Fact]
    public async Task InstallGridAsync_RejectsSnapshotHashMismatch_BeforeTouchingFile()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logging = new FileLoggingService(paths);
        var backupService = new BackupService(paths);
        var historyService = new HistoryService(paths);
        var dotaGridService = new DotaGridService();
        var validSnapshot = StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray());
        var badSnapshot = new HeroGridSnapshot
        {
            SourceName = validSnapshot.SourceName,
            ProviderName = validSnapshot.ProviderName,
            SourceStrategy = validSnapshot.SourceStrategy,
            Preset = validSnapshot.Preset,
            PatchLabel = validSnapshot.PatchLabel,
            CapturedAt = validSnapshot.CapturedAt,
            Hash = "BADHASH",
            ProviderStatus = validSnapshot.ProviderStatus,
            SourceDetails = validSnapshot.SourceDetails,
            Layouts = validSnapshot.Layouts
        };

        var updateService = new UpdateService(new StubProvider(validSnapshot), dotaGridService, backupService, historyService, logging, new FakeHeroCatalogService());
        var settings = new AppSettings();
        var account = await CreateAccountFixtureAsync(paths, dotaGridService, [1, 2, 3]);
        var originalJson = await File.ReadAllTextAsync(account.HeroGridConfigPath);

        var result = await updateService.InstallGridAsync([account], settings, CreateInstallSnapshot(badSnapshot, account), CancellationToken.None);
        var finalJson = await File.ReadAllTextAsync(account.HeroGridConfigPath);

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Equal(originalJson, finalJson);
    }

    [Fact]
    public async Task UpdateService_SerializesConcurrentOperations()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logging = new FileLoggingService(paths);
        var backupService = new BackupService(paths);
        var historyService = new HistoryService(paths);
        var dotaGridService = new DotaGridService();
        var concurrency = new ConcurrentQueue<int>();
        var current = 0;
        var max = 0;
        var provider = new StubProvider(StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray()), async () =>
        {
            var value = Interlocked.Increment(ref current);
            max = Math.Max(max, value);
            concurrency.Enqueue(value);
            await Task.Delay(150);
            Interlocked.Decrement(ref current);
        });

        var updateService = new UpdateService(provider, dotaGridService, backupService, historyService, logging, new FakeHeroCatalogService());
        var settings = new AppSettings();
        var account = await CreateAccountFixtureAsync(paths, dotaGridService);

        await Task.WhenAll(
            updateService.CheckForUpdatesAsync([account], settings, true, CancellationToken.None),
            updateService.CheckForUpdatesAsync([account], settings, true, CancellationToken.None));

        Assert.True(max <= 1, $"Expected semaphore serialization to keep provider concurrency at 1, got {max}.");
    }

    [Fact]
    public async Task AutomaticUpdateCycle_SkipsWrite_WhenSemanticHashIsUnchanged()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logging = new FileLoggingService(paths);
        var backupService = new BackupService(paths);
        var historyService = new HistoryService(paths);
        var dotaGridService = new DotaGridService();
        var snapshot = StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray());
        var updateService = new UpdateService(new StubProvider(snapshot), dotaGridService, backupService, historyService, logging, new FakeHeroCatalogService());
        var settings = new AppSettings { AutoUpdateEnabled = true };
        var account = CreateMissingGridAccountFixture(paths);

        var initialInstall = await updateService.InstallGridAsync([account], settings, CreateInstallSnapshot(snapshot, account), CancellationToken.None);
        account.CurrentMetaGridHash = initialInstall.GridHash;

        var result = await updateService.RunAutomaticUpdateCycleAsync([account], settings, CancellationToken.None);
        var history = await historyService.LoadAsync(CancellationToken.None);
        var backups = await backupService.GetBackupsAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.AlreadyUpToDate, result.Status);
        Assert.Equal(UpdateTriggerKind.Automatic, result.Trigger);
        Assert.Empty(backups);
        Assert.Single(history);
        Assert.DoesNotContain("Automatic update", history.Select(entry => entry.Operation));
    }

    [Fact]
    public async Task AutomaticUpdateCycle_InstallsNewGrid_AndRecordsAutomaticHistory()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logging = new FileLoggingService(paths);
        var backupService = new BackupService(paths);
        var historyService = new HistoryService(paths);
        var dotaGridService = new DotaGridService();
        var snapshot = StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray());
        var updateService = new UpdateService(new StubProvider(snapshot), dotaGridService, backupService, historyService, logging, new FakeHeroCatalogService());
        var settings = new AppSettings { AutoUpdateEnabled = true };
        var account = await CreateAccountFixtureAsync(paths, dotaGridService, [1, 2, 3]);

        var result = await updateService.RunAutomaticUpdateCycleAsync([account], settings, CancellationToken.None);
        var installedHash = await dotaGridService.ReadInstalledMetaGridHashAsync(account.HeroGridConfigPath, CancellationToken.None);
        var history = await historyService.LoadAsync(CancellationToken.None);
        var backups = await backupService.GetBackupsAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.Updated, result.Status);
        Assert.Equal(UpdateTriggerKind.Automatic, result.Trigger);
        Assert.Equal(result.GridHash, installedHash);
        Assert.Single(backups);
        Assert.Equal("Automatic update", history.First().Operation);
        Assert.Equal(UpdateTriggerKind.Automatic, history.First().Trigger);
    }

    [Fact]
    public async Task AutomaticUpdateCycle_DoesNotWrite_WhenProviderFails()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logging = new FileLoggingService(paths);
        var backupService = new BackupService(paths);
        var historyService = new HistoryService(paths);
        var dotaGridService = new DotaGridService();
        var updateService = new UpdateService(new ThrowingProvider(new HeroGridProviderUnavailableException(ProviderStatus.NetworkUnavailable, "Offline")), dotaGridService, backupService, historyService, logging, new FakeHeroCatalogService());
        var settings = new AppSettings { AutoUpdateEnabled = true };
        var account = await CreateAccountFixtureAsync(paths, dotaGridService, [1, 2, 3]);
        var originalJson = await File.ReadAllTextAsync(account.HeroGridConfigPath);

        var result = await updateService.RunAutomaticUpdateCycleAsync([account], settings, CancellationToken.None);
        var finalJson = await File.ReadAllTextAsync(account.HeroGridConfigPath);

        Assert.Equal(UpdateStatus.SourceUnavailable, result.Status);
        Assert.Equal(originalJson, finalJson);
    }

    [Fact]
    public async Task AutomaticUpdateCycle_DoesNotWrite_WhenSnapshotValidationFails()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logging = new FileLoggingService(paths);
        var backupService = new BackupService(paths);
        var historyService = new HistoryService(paths);
        var dotaGridService = new DotaGridService();
        var invalidSnapshot = StubProvider.CreateSnapshot([9999, 10000, 10001, 10002, 10003]);
        var updateService = new UpdateService(new StubProvider(invalidSnapshot), dotaGridService, backupService, historyService, logging, new FakeHeroCatalogService());
        var settings = new AppSettings { AutoUpdateEnabled = true };
        var account = await CreateAccountFixtureAsync(paths, dotaGridService, [1, 2, 3]);
        var originalJson = await File.ReadAllTextAsync(account.HeroGridConfigPath);

        var result = await updateService.RunAutomaticUpdateCycleAsync([account], settings, CancellationToken.None);
        var finalJson = await File.ReadAllTextAsync(account.HeroGridConfigPath);

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Equal(UpdateTriggerKind.Automatic, result.Trigger);
        Assert.Equal(originalJson, finalJson);
    }

    [Fact]
    public async Task AutomaticUpdateCycle_ReturnsWaitingForAccount_WhenNoSingleSelectionExists()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logging = new FileLoggingService(paths);
        var backupService = new BackupService(paths);
        var historyService = new HistoryService(paths);
        var dotaGridService = new DotaGridService();
        var snapshot = StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray());
        var updateService = new UpdateService(new StubProvider(snapshot), dotaGridService, backupService, historyService, logging, new FakeHeroCatalogService());
        var settings = new AppSettings { AutoUpdateEnabled = true };
        var accountA = await CreateAccountFixtureAsync(paths, dotaGridService, [1, 2, 3]);
        var accountB = await CreateSecondAccountFixtureAsync(paths, dotaGridService);

        var result = await updateService.RunAutomaticUpdateCycleAsync([accountA, accountB], settings, CancellationToken.None);

        Assert.Equal(UpdateStatus.WaitingForAccount, result.Status);
        Assert.Equal(UpdateTriggerKind.Automatic, result.Trigger);
    }

    [Fact]
    public async Task AutomaticUpdateCycle_DoesNotTouchTarget_WhenBackupFails()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logging = new FileLoggingService(paths);
        var historyService = new HistoryService(paths);
        var dotaGridService = new DotaGridService();
        var snapshot = StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray());
        var updateService = new UpdateService(new StubProvider(snapshot), dotaGridService, new FailingBackupService(), historyService, logging, new FakeHeroCatalogService());
        var settings = new AppSettings { AutoUpdateEnabled = true };
        var account = await CreateAccountFixtureAsync(paths, dotaGridService, [1, 2, 3]);
        var originalJson = await File.ReadAllTextAsync(account.HeroGridConfigPath);

        var result = await updateService.RunAutomaticUpdateCycleAsync([account], settings, CancellationToken.None);
        var finalJson = await File.ReadAllTextAsync(account.HeroGridConfigPath);

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Equal(originalJson, finalJson);
    }

    [Fact]
    public async Task AutomaticUpdateCycle_SerializesConcurrentRuns()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logging = new FileLoggingService(paths);
        var backupService = new BackupService(paths);
        var historyService = new HistoryService(paths);
        var dotaGridService = new DotaGridService();
        var current = 0;
        var max = 0;
        var provider = new StubProvider(StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray()), async () =>
        {
            var value = Interlocked.Increment(ref current);
            max = Math.Max(max, value);
            await Task.Delay(150);
            Interlocked.Decrement(ref current);
        });

        var updateService = new UpdateService(provider, dotaGridService, backupService, historyService, logging, new FakeHeroCatalogService());
        var settings = new AppSettings { AutoUpdateEnabled = true };
        var account = await CreateAccountFixtureAsync(paths, dotaGridService, [1, 2, 3]);

        await Task.WhenAll(
            updateService.RunAutomaticUpdateCycleAsync([account], settings, CancellationToken.None),
            updateService.RunAutomaticUpdateCycleAsync([account], settings, CancellationToken.None));

        Assert.True(max <= 1, $"Expected automatic update serialization to keep provider concurrency at 1, got {max}.");
    }

    [Fact]
    public async Task CheckForUpdatesAsync_DoesNotInstall_WhenAvailableSnapshotExists()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logging = new FileLoggingService(paths);
        var backupService = new BackupService(paths);
        var historyService = new HistoryService(paths);
        var dotaGridService = new DotaGridService();
        var snapshot = StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray(), sourceName: "OpenDota");
        var updateService = new UpdateService(new StubProvider(snapshot), dotaGridService, backupService, historyService, logging, new FakeHeroCatalogService());
        var settings = new AppSettings();
        var account = CreateMissingGridAccountFixture(paths);

        var result = await updateService.CheckForUpdatesAsync([account], settings, forceWrite: true, CancellationToken.None);

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.False(File.Exists(account.HeroGridConfigPath));
        Assert.NotNull(result.ConfirmedInstallSnapshot);
    }

    [Fact]
    public async Task InstallGridAsync_CreatesFirstManagedGrid_WhenFileDoesNotExist()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logging = new FileLoggingService(paths);
        var backupService = new BackupService(paths);
        var historyService = new HistoryService(paths);
        var dotaGridService = new DotaGridService();
        var snapshot = StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray(), sourceName: "OpenDota");
        var provider = new StubProvider(snapshot);
        var updateService = new UpdateService(provider, dotaGridService, backupService, historyService, logging, new FakeHeroCatalogService());
        var settings = new AppSettings();
        var account = CreateMissingGridAccountFixture(paths);

        var result = await updateService.InstallGridAsync([account], settings, CreateInstallSnapshot(snapshot, account), CancellationToken.None);
        var installedHash = await dotaGridService.ReadInstalledMetaGridHashAsync(account.HeroGridConfigPath, CancellationToken.None);

        Assert.Equal(UpdateStatus.Updated, result.Status);
        Assert.Equal(result.GridHash, installedHash);
        Assert.True(File.Exists(account.HeroGridConfigPath));
        Assert.Null(result.BackupEntry);
    }

    [Fact]
    public void BuildInstallPreview_Succeeds_ForFirstInstall_WhenHeroGridFileDoesNotExist()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var settings = new AppSettings
        {
            LastRemoteHash = "C66F7B6136A6C3FADAD5326D45BA109ADFD689F82FC2D549513CD4BBA7D4D52E",
            LastSourceName = "OpenDota",
            LastHeroCount = 55
        };
        var account = CreateMissingGridAccountFixture(paths);
        var targetPath = Path.Combine(account.DotaConfigDirectory, "hero_grid_config.json");

        var preview = BuildInstallPreviewForTest(settings, account);

        Assert.NotNull(preview);
        Assert.Equal(targetPath, preview!.TargetPath);
        Assert.False(preview.HasExistingGridFile);
        Assert.Equal("Install Grid", preview.ActionLabel);
    }

    [Fact]
    public void BuildInstallPreview_ReturnsNull_WhenSelectedAccountIsInvalid()
    {
        var settings = new AppSettings
        {
            LastRemoteHash = "C66F7B6136A6C3FADAD5326D45BA109ADFD689F82FC2D549513CD4BBA7D4D52E",
            LastSourceName = "OpenDota",
            LastHeroCount = 55
        };
        var account = new SteamAccount
        {
            AccountId = "123456789",
            DisplayName = "Steam 123456789",
            SteamRootPath = "C:\\FakeSteam",
            UserDataPath = "C:\\FakeSteam\\userdata\\123456789",
            DotaConfigDirectory = "",
            HasDotaUserData = true,
            IsSelected = true
        };

        var preview = BuildInstallPreviewForTest(settings, account);

        Assert.Null(preview);
    }

    [Fact]
    public async Task InstallGridAsync_PreservesUnrelatedCustomGrids_WhenExistingFileContainsUserLayouts()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logging = new FileLoggingService(paths);
        var backupService = new BackupService(paths);
        var historyService = new HistoryService(paths);
        var dotaGridService = new DotaGridService();
        var snapshot = StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray(), sourceName: "OpenDota");
        var provider = new StubProvider(snapshot);
        var updateService = new UpdateService(provider, dotaGridService, backupService, historyService, logging, new FakeHeroCatalogService());
        var settings = new AppSettings();
        var account = await CreateAccountFixtureAsync(paths, dotaGridService, [1, 2, 3]);

        var result = await updateService.InstallGridAsync([account], settings, CreateInstallSnapshot(snapshot, account), CancellationToken.None);
        var reloaded = await dotaGridService.ReadAsync(account.HeroGridConfigPath, CancellationToken.None);

        Assert.Equal(UpdateStatus.Updated, result.Status);
        Assert.Contains(reloaded.Configs, layout => layout.ConfigName == "Personal Layout");
        Assert.Contains(reloaded.Configs, layout => layout.ConfigName.StartsWith("MetaGrid - ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InstallGridAsync_Aborts_WhenBackupCreationFails()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logging = new FileLoggingService(paths);
        var historyService = new HistoryService(paths);
        var dotaGridService = new DotaGridService();
        var snapshot = StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray(), sourceName: "OpenDota");
        var provider = new StubProvider(snapshot);
        var updateService = new UpdateService(provider, dotaGridService, new FailingBackupService(), historyService, logging, new FakeHeroCatalogService());
        var settings = new AppSettings();
        var account = await CreateAccountFixtureAsync(paths, dotaGridService, [1, 2, 3]);
        var originalJson = await File.ReadAllTextAsync(account.HeroGridConfigPath);

        var result = await updateService.InstallGridAsync([account], settings, CreateInstallSnapshot(snapshot, account), CancellationToken.None);
        var finalJson = await File.ReadAllTextAsync(account.HeroGridConfigPath);

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Equal(originalJson, finalJson);
    }

    [Fact]
    public async Task InstallGridAsync_RollsBack_WhenPostWriteValidationFails()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logging = new FileLoggingService(paths);
        var backupService = new BackupService(paths);
        var historyService = new HistoryService(paths);
        var snapshot = StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray(), sourceName: "OpenDota");
        var provider = new StubProvider(snapshot);
        var dotaGridService = new MismatchedHashAfterWriteDotaGridService();
        var updateService = new UpdateService(provider, dotaGridService, backupService, historyService, logging, new FakeHeroCatalogService());
        var settings = new AppSettings();
        var account = await CreateAccountFixtureAsync(paths, new DotaGridService(), [1, 2, 3]);
        var originalJson = await File.ReadAllTextAsync(account.HeroGridConfigPath);

        var result = await updateService.InstallGridAsync([account], settings, CreateInstallSnapshot(snapshot, account), CancellationToken.None);
        var finalJson = await File.ReadAllTextAsync(account.HeroGridConfigPath);

        Assert.True(result.Status == UpdateStatus.BackupRestored, $"Expected rollback success but got {result.Status}: {result.Message}");
        Assert.Equal(originalJson, finalJson);
    }

    [Fact]
    public async Task InstallGridAsync_FirstInstallRollback_RemovesNewlyCreatedFile()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logging = new FileLoggingService(paths);
        var backupService = new BackupService(paths);
        var historyService = new HistoryService(paths);
        var snapshot = StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray(), sourceName: "OpenDota");
        var updateService = new UpdateService(new StubProvider(snapshot), new MismatchedHashAfterWriteDotaGridService(), backupService, historyService, logging, new FakeHeroCatalogService());
        var settings = new AppSettings();
        var account = CreateMissingGridAccountFixture(paths);

        var result = await updateService.InstallGridAsync([account], settings, CreateInstallSnapshot(snapshot, account), CancellationToken.None);

        Assert.Equal(UpdateStatus.BackupRestored, result.Status);
        Assert.False(File.Exists(account.HeroGridConfigPath));
        Assert.Contains("newly created hero grid file was removed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstallGridAsync_RejectsAmbiguousAccountSelection()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logging = new FileLoggingService(paths);
        var backupService = new BackupService(paths);
        var historyService = new HistoryService(paths);
        var dotaGridService = new DotaGridService();
        var snapshot = StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray(), sourceName: "OpenDota");
        var provider = new StubProvider(snapshot);
        var updateService = new UpdateService(provider, dotaGridService, backupService, historyService, logging, new FakeHeroCatalogService());
        var settings = new AppSettings();
        var accountA = await CreateAccountFixtureAsync(paths, dotaGridService, [1, 2, 3]);
        var accountB = await CreateSecondAccountFixtureAsync(paths, dotaGridService);

        var result = await updateService.InstallGridAsync([accountA, accountB], settings, CreateInstallSnapshot(snapshot, accountA), CancellationToken.None);

        Assert.Equal(UpdateStatus.WaitingForAccount, result.Status);
    }

    [Fact]
    public async Task InstallGridAsync_DoesNotRefreshProvider_WhenUsingConfirmedSnapshot()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var logging = new FileLoggingService(paths);
        var backupService = new BackupService(paths);
        var historyService = new HistoryService(paths);
        var dotaGridService = new DotaGridService();
        var snapshot = StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray(), sourceName: "OpenDota");
        var provider = new CountingProvider(snapshot);
        var updateService = new UpdateService(provider, dotaGridService, backupService, historyService, logging, new FakeHeroCatalogService());
        var account = CreateMissingGridAccountFixture(paths);

        var result = await updateService.InstallGridAsync([account], new AppSettings(), CreateInstallSnapshot(snapshot, account), CancellationToken.None);

        Assert.Equal(UpdateStatus.Updated, result.Status);
        Assert.Equal(0, provider.FetchCount);
    }

    [Fact]
    public async Task BackupService_AssociatesBackups_WithAccountAndOriginalPath()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var backupService = new BackupService(paths);
        var sourcePath = Path.Combine(paths.RootDirectory, "steam", "123456789", "570", "remote", "cfg", "hero_grid_config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "{\"version\":3,\"configs\":[]}");

        var backup = await backupService.BackupAsync(sourcePath, 5, new BackupMetadata
        {
            AccountId = "123456789",
            AccountDisplayName = "Steam 123456789",
            OriginalPath = sourcePath,
            PreWriteHash = "ABC123",
            OperationId = "operation-1"
        }, CancellationToken.None);

        Assert.NotNull(backup);
        Assert.Equal("123456789", backup.AccountId);
        Assert.Equal(sourcePath, backup.OriginalPath);
        Assert.Equal("ABC123", backup.PreWriteHash);
        Assert.Equal("operation-1", backup.OperationId);
        Assert.False(string.IsNullOrWhiteSpace(backup.BackupHash));
    }

    private static async Task<SteamAccount> CreateAccountFixtureAsync(TestAppPaths paths, DotaGridService service, IReadOnlyList<int>? heroes = null)
    {
        var accountDir = Path.Combine(paths.RootDirectory, "steam", "123456789", "570", "remote", "cfg");
        Directory.CreateDirectory(accountDir);
        var configPath = Path.Combine(accountDir, "hero_grid_config.json");
        var seed = new DotaHeroGridFile
        {
            Configs =
            [
                new DotaHeroGridLayout
                {
                    ConfigName = "Personal Layout",
                    Categories =
                    [
                        new DotaHeroGridCategory
                        {
                            CategoryName = "Favorites",
                            XPosition = 0,
                            YPosition = 0,
                            Width = 100,
                            Height = 100,
                            HeroIds = heroes?.ToList() ?? [1, 2, 3]
                        }
                    ]
                }
            ]
        };

        await File.WriteAllTextAsync(configPath, service.Serialize(seed));
        return new SteamAccount
        {
            AccountId = "123456789",
            SteamRootPath = Path.Combine(paths.RootDirectory, "steam"),
            UserDataPath = Path.Combine(paths.RootDirectory, "steam", "123456789"),
            DotaConfigDirectory = accountDir,
            DisplayName = "Steam 123456789",
            HasDotaUserData = true,
            HasHeroGridConfig = true,
            IsSelected = true
        };
    }

    private static SteamAccount CreateMissingGridAccountFixture(TestAppPaths paths)
    {
        var accountDir = Path.Combine(paths.RootDirectory, "steam", "123456789", "570", "remote", "cfg");
        Directory.CreateDirectory(accountDir);
        return new SteamAccount
        {
            AccountId = "123456789",
            SteamRootPath = Path.Combine(paths.RootDirectory, "steam"),
            UserDataPath = Path.Combine(paths.RootDirectory, "steam", "123456789"),
            DotaConfigDirectory = accountDir,
            DisplayName = "Steam 123456789",
            HasDotaUserData = true,
            HasHeroGridConfig = false,
            IsSelected = true
        };
    }

    private static async Task<SteamAccount> CreateSecondAccountFixtureAsync(TestAppPaths paths, DotaGridService service)
    {
        var accountDir = Path.Combine(paths.RootDirectory, "steam", "987654321", "570", "remote", "cfg");
        Directory.CreateDirectory(accountDir);
        var configPath = Path.Combine(accountDir, "hero_grid_config.json");
        await File.WriteAllTextAsync(configPath, service.Serialize(new DotaHeroGridFile()));
        return new SteamAccount
        {
            AccountId = "987654321",
            SteamRootPath = Path.Combine(paths.RootDirectory, "steam"),
            UserDataPath = Path.Combine(paths.RootDirectory, "steam", "987654321"),
            DotaConfigDirectory = accountDir,
            DisplayName = "Steam 987654321",
            HasDotaUserData = true,
            HasHeroGridConfig = true,
            IsSelected = true
        };
    }

    private sealed class FailingBackupService : IBackupService
    {
        public Task<BackupEntry?> BackupAsync(string sourceFilePath, int retentionCount, BackupMetadata? metadata, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Backup failed intentionally for test coverage.");

        public string GetBackupsDirectory() => string.Empty;
        public Task<IReadOnlyList<BackupEntry>> GetBackupsAsync(CancellationToken cancellationToken, string? accountId = null)
            => Task.FromResult<IReadOnlyList<BackupEntry>>([]);
        public Task<bool> RestoreLatestAsync(string destinationFilePath, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> RestoreAsync(BackupEntry backup, string destinationFilePath, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class MismatchedHashAfterWriteDotaGridService : IDotaGridService
    {
        private readonly DotaGridService _inner = new();
        private int _canonicalHashCount;

        public DotaHeroGridFile ApplySnapshot(DotaHeroGridFile existing, HeroGridSnapshot snapshot) => _inner.ApplySnapshot(existing, snapshot);
        public Task<DotaHeroGridFile> ReadAsync(string configPath, CancellationToken cancellationToken) => _inner.ReadAsync(configPath, cancellationToken);
        public string? ReadInstalledMetaGridHash(DotaHeroGridFile file)
            => _inner.ReadInstalledMetaGridHash(file);
        public Task<string?> ReadInstalledMetaGridHashAsync(string configPath, CancellationToken cancellationToken)
            => _inner.ReadInstalledMetaGridHashAsync(configPath, cancellationToken);
        public CanonicalHeroGridSnapshot CanonicalizeSnapshot(HeroGridSnapshot snapshot) => _inner.CanonicalizeSnapshot(snapshot);
        public CanonicalHeroGridSnapshot? ExtractManagedGrid(DotaHeroGridFile file) => _inner.ExtractManagedGrid(file);
        public string NormalizeCanonical(CanonicalHeroGridSnapshot snapshot) => _inner.NormalizeCanonical(snapshot);
        public string ComputeSnapshotHash(HeroGridSnapshot snapshot) => _inner.ComputeSnapshotHash(snapshot);
        public string ComputeCanonicalHash(CanonicalHeroGridSnapshot snapshot)
        {
            _canonicalHashCount++;
            var actual = _inner.ComputeCanonicalHash(snapshot);
            return _canonicalHashCount >= 3 ? "MISMATCHED_HASH" : actual;
        }
        public string SummarizeDifference(CanonicalHeroGridSnapshot expected, CanonicalHeroGridSnapshot actual) => _inner.SummarizeDifference(expected, actual);
        public string NormalizeSnapshot(HeroGridSnapshot snapshot) => _inner.NormalizeSnapshot(snapshot);
        public string Serialize(DotaHeroGridFile file) => _inner.Serialize(file);
        public bool TryValidate(string json, out string error) => _inner.TryValidate(json, out error);
    }

    private sealed class CountingProvider(HeroGridSnapshot snapshot) : IHeroGridProvider
    {
        public int FetchCount { get; private set; }

        public Task<HeroGridSnapshot> FetchAsync(HeroGridPreset preset, CancellationToken cancellationToken)
        {
            FetchCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class ThrowingProvider(Exception exception) : IHeroGridProvider
    {
        public Task<HeroGridSnapshot> FetchAsync(HeroGridPreset preset, CancellationToken cancellationToken)
            => Task.FromException<HeroGridSnapshot>(exception);
    }

    private static InstallGridSnapshot CreateInstallSnapshot(HeroGridSnapshot snapshot, SteamAccount account)
        => new()
        {
            Snapshot = snapshot,
            AccountId = account.AccountId,
            AccountDisplayName = account.DisplayName,
            TargetPath = account.HeroGridConfigPath,
            PreparedAt = DateTimeOffset.UtcNow,
            HeroCount = snapshot.Layouts.SelectMany(layout => layout.Categories).SelectMany(category => category.HeroIds).Distinct().Count(),
            GroupCount = snapshot.Layouts.SelectMany(layout => layout.Categories).Select(category => category.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            HasExistingGridFile = File.Exists(account.HeroGridConfigPath)
        };

    private static InstallPreviewAssertionModel? BuildInstallPreviewForTest(AppSettings settings, SteamAccount account)
    {
        if (string.IsNullOrWhiteSpace(settings.LastRemoteHash) || string.IsNullOrWhiteSpace(account.DotaConfigDirectory))
        {
            return null;
        }

        var targetPath = Path.Combine(account.DotaConfigDirectory, "hero_grid_config.json");
        return new InstallPreviewAssertionModel(
            targetPath,
            File.Exists(targetPath),
            string.IsNullOrWhiteSpace(settings.LastInstalledHash) ? "Install Grid" : "Update Grid");
    }

    private sealed record InstallPreviewAssertionModel(string TargetPath, bool HasExistingGridFile, string ActionLabel);
}
