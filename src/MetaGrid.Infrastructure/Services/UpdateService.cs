using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Utilities;

namespace MetaGrid.Infrastructure.Services;

public sealed class UpdateService(
    IHeroGridProvider provider,
    IDotaGridService dotaGridService,
    IBackupService backupService,
    IHistoryService historyService,
    ILoggingService loggingService,
    IHeroCatalogService heroCatalogService,
    IPersonalizationService personalizationService,
    IPersonalizedGridComposer personalizedGridComposer) : IUpdateService
{
    private static readonly string[] PreferredRoleGroups = ["Carry", "Mid", "Offlane", "Support", "Hard Support", "All Roles"];
    private readonly SemaphoreSlim _updateGate = new(1, 1);

    public UpdateService(
        IHeroGridProvider provider,
        IDotaGridService dotaGridService,
        IBackupService backupService,
        IHistoryService historyService,
        ILoggingService loggingService,
        IHeroCatalogService heroCatalogService)
        : this(
            provider,
            dotaGridService,
            backupService,
            historyService,
            loggingService,
            heroCatalogService,
            new NullPersonalizationService(),
            new PassThroughPersonalizedGridComposer())
    {
    }

    public async Task<UpdateRunResult> CheckForUpdatesAsync(
        IReadOnlyList<SteamAccount> accounts,
        AppSettings settings,
        bool forceWrite,
        CancellationToken cancellationToken)
    {
        await _updateGate.WaitAsync(cancellationToken);
        try
        {
            var selectedAccount = ResolveSelectedAccount(accounts);
            if (selectedAccount is null)
            {
                return NoSelectedAccountResult(UpdateTriggerKind.ManualCheck);
            }

            var fetch = await TryFetchBaseSnapshotAsync(settings, forceWrite, UpdateTriggerKind.ManualCheck, cancellationToken);
            if (!fetch.Succeeded || fetch.BaseSnapshot is null)
            {
                return fetch.FailureResult!;
            }

            var snapshot = fetch.BaseSnapshot;

            await loggingService.LogAsync(LogLevelKind.Information, "Provider snapshot received for update check.", new
            {
                snapshot.SourceName,
                snapshot.SourceStrategy,
                snapshot.ProviderStatus,
                snapshot.OriginKind,
                snapshot.Hash,
                snapshot.RawSha256,
                RawPayloadBytes = snapshot.RawPayloadBytes?.Length ?? 0,
                ParsedGridPresent = snapshot.ParsedGrid is not null,
                ParsedGridVersion = snapshot.ParsedGrid?.Version,
                ParsedGridConfigCount = snapshot.ParsedGrid?.Configs.Count,
                snapshot.PatchLabel,
                LayoutCount = snapshot.Layouts.Count,
                CategoryCount = snapshot.Layouts.Sum(layout => layout.Categories.Count)
            }, cancellationToken);

            var snapshotValidation = await ValidateSnapshotAsync(snapshot, cancellationToken);
            if (!snapshotValidation.IsValid)
            {
                await loggingService.LogAsync(LogLevelKind.Warning, "Provider snapshot canonical validation failed.", new
                {
                    snapshot.SourceName,
                    snapshot.SourceStrategy,
                    snapshot.Hash,
                    snapshot.RawSha256,
                    RawPayloadBytes = snapshot.RawPayloadBytes?.Length ?? 0,
                    ParsedGridPresent = snapshot.ParsedGrid is not null,
                    ParsedGridVersion = snapshot.ParsedGrid?.Version,
                    ParsedGridConfigCount = snapshot.ParsedGrid?.Configs.Count,
                    ValidationError = snapshotValidation.Error
                }, cancellationToken);

                return FailureResult(
                    "The provider snapshot failed canonical validation and cannot be used for installation.",
                    snapshot,
                    await dotaGridService.ReadInstalledMetaGridHashAsync(selectedAccount.HeroGridConfigPath, cancellationToken),
                    CountChangedHeroes(snapshot),
                    selectedAccount.HeroGridConfigPath,
                    snapshotValidation.Error);
            }

            var composition = await ComposeEffectiveSnapshotAsync(snapshot, settings, selectedAccount, forceWrite, cancellationToken);
            var changedHeroCount = CountChangedHeroes(composition.EffectiveSnapshot);
            var currentHash = await dotaGridService.ReadInstalledMetaGridHashAsync(selectedAccount.HeroGridConfigPath, cancellationToken);
            var isLiveSnapshot = snapshot.OriginKind != GridOriginKind.Cached;
            var hashesMatch = string.Equals(currentHash, composition.EffectiveGridHash, StringComparison.OrdinalIgnoreCase);
            var installSnapshot = CreateInstallSnapshot(selectedAccount, composition);

            return CreateCheckResult(
                composition,
                currentHash,
                selectedAccount.HeroGridConfigPath,
                changedHeroCount,
                hashesMatch,
                isLiveSnapshot,
                installSnapshot,
                UpdateTriggerKind.ManualCheck);
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public async Task<UpdateRunResult> RunAutomaticUpdateCycleAsync(
        IReadOnlyList<SteamAccount> accounts,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        await _updateGate.WaitAsync(cancellationToken);
        try
        {
            await loggingService.LogAsync(LogLevelKind.Information, "Automatic update cycle started.", new
            {
                settings.AutoUpdateEnabled,
                settings.UpdateInterval,
                settings.PreferredPreset
            }, cancellationToken);

            var selectedAccount = ResolveSelectedAccount(accounts);
            if (selectedAccount is null)
            {
                await loggingService.LogAsync(LogLevelKind.Warning, "Automatic update cycle skipped because no valid selected Steam account was available.", null, cancellationToken);
                return NoSelectedAccountResult(UpdateTriggerKind.Automatic);
            }

            var fetch = await TryFetchBaseSnapshotAsync(settings, forceRefresh: false, UpdateTriggerKind.Automatic, cancellationToken);
            if (!fetch.Succeeded || fetch.BaseSnapshot is null)
            {
                return fetch.FailureResult!;
            }

            var snapshot = fetch.BaseSnapshot;

            await loggingService.LogAsync(LogLevelKind.Information, "Automatic update provider snapshot received.", new
            {
                snapshot.SourceName,
                snapshot.SourceStrategy,
                snapshot.ProviderStatus,
                snapshot.OriginKind,
                snapshot.Hash,
                snapshot.RawSha256
            }, cancellationToken);

            var snapshotValidation = await ValidateSnapshotAsync(snapshot, cancellationToken);
            if (!snapshotValidation.IsValid)
            {
                await loggingService.LogAsync(LogLevelKind.Warning, "Automatic update cycle rejected a snapshot during validation.", new
                {
                    snapshot.SourceName,
                    snapshot.SourceStrategy,
                    snapshot.Hash,
                    ValidationError = snapshotValidation.Error
                }, cancellationToken);

                return FailureResult(
                    "The automatic update payload failed canonical validation and was not installed.",
                    snapshot,
                    await dotaGridService.ReadInstalledMetaGridHashAsync(selectedAccount.HeroGridConfigPath, cancellationToken),
                    CountChangedHeroes(snapshot),
                    selectedAccount.HeroGridConfigPath,
                    snapshotValidation.Error,
                    trigger: UpdateTriggerKind.Automatic);
            }

            var composition = await ComposeEffectiveSnapshotAsync(snapshot, settings, selectedAccount, forceRefresh: false, cancellationToken);
            var changedHeroCount = CountChangedHeroes(composition.EffectiveSnapshot);
            var currentHash = await dotaGridService.ReadInstalledMetaGridHashAsync(selectedAccount.HeroGridConfigPath, cancellationToken);
            var installSnapshot = CreateInstallSnapshot(selectedAccount, composition);
            var hashesMatch = string.Equals(currentHash, composition.EffectiveGridHash, StringComparison.OrdinalIgnoreCase);

            if (snapshot.OriginKind == GridOriginKind.Cached)
            {
                await loggingService.LogAsync(LogLevelKind.Warning, "Automatic update cycle skipped installation because only cached data was available.", new
                {
                    composition.EffectiveGridHash,
                    currentHash,
                    snapshot.CapturedAt
                }, cancellationToken);

                return new UpdateRunResult
                {
                    Status = UpdateStatus.SourceUnavailable,
                    Message = hashesMatch
                        ? "Live D2PT retrieval failed, but the installed grid already matches the last valid cached snapshot. No automatic write was performed."
                        : "Live D2PT retrieval failed. MetaGrid preserved the last installed grid and did not automatically install cached-only data.",
                    GridHash = composition.EffectiveGridHash,
                    ChangedHeroCount = changedHeroCount,
                    ProviderStatus = snapshot.ProviderStatus,
                    OriginKind = snapshot.OriginKind,
                    SourceName = snapshot.SourceName,
                    SourceStrategy = snapshot.SourceStrategy,
                    RetrievedAt = snapshot.CapturedAt,
                    ProviderMessage = BuildProviderMessage(snapshot.SourceDetails, composition.Personalization),
                    InstalledHash = currentHash,
                    AvailableHash = composition.EffectiveGridHash,
                    BaseSourceHash = composition.BaseSourceHash,
                    EffectiveGridHash = composition.EffectiveGridHash,
                    PersonalizationStatus = composition.Personalization.Status.ToString(),
                    PersonalizationMessage = composition.Personalization.Message,
                    PersonalizationAccountId = composition.Personalization.AccountId,
                    PersonalizationAccountDisplayName = composition.Personalization.DisplayName,
                    PersonalizationUsedCache = composition.Personalization.UsedCache,
                    PersonalHeroCount = composition.Personalization.Selection?.SelectedHeroes.Count ?? 0,
                    TargetPath = selectedAccount.HeroGridConfigPath,
                    ConfirmedInstallSnapshot = hashesMatch ? installSnapshot : null,
                    Trigger = UpdateTriggerKind.Automatic
                };
            }

            if (hashesMatch)
            {
                await loggingService.LogAsync(LogLevelKind.Information, "Automatic update cycle detected no semantic change.", new
                {
                    snapshot.Hash,
                    currentHash,
                    selectedAccount.AccountId
                }, cancellationToken);

                return new UpdateRunResult
                {
                    Status = UpdateStatus.AlreadyUpToDate,
                    Message = "No semantic grid change was detected. The installed MetaGrid-managed hero grid is already current.",
                    GridHash = composition.EffectiveGridHash,
                    ChangedHeroCount = changedHeroCount,
                    ProviderStatus = snapshot.ProviderStatus,
                    OriginKind = snapshot.OriginKind,
                    SourceName = snapshot.SourceName,
                    SourceStrategy = snapshot.SourceStrategy,
                    RetrievedAt = snapshot.CapturedAt,
                    ProviderMessage = BuildProviderMessage(snapshot.SourceDetails, composition.Personalization),
                    InstalledHash = currentHash,
                    AvailableHash = composition.EffectiveGridHash,
                    BaseSourceHash = composition.BaseSourceHash,
                    EffectiveGridHash = composition.EffectiveGridHash,
                    PersonalizationStatus = composition.Personalization.Status.ToString(),
                    PersonalizationMessage = composition.Personalization.Message,
                    PersonalizationAccountId = composition.Personalization.AccountId,
                    PersonalizationAccountDisplayName = composition.Personalization.DisplayName,
                    PersonalizationUsedCache = composition.Personalization.UsedCache,
                    PersonalHeroCount = composition.Personalization.Selection?.SelectedHeroes.Count ?? 0,
                    TargetPath = selectedAccount.HeroGridConfigPath,
                    ConfirmedInstallSnapshot = installSnapshot,
                    Trigger = UpdateTriggerKind.Automatic
                };
            }

            await loggingService.LogAsync(LogLevelKind.Information, "Automatic update cycle detected a semantic change and is proceeding to installation.", new
            {
                selectedAccount.AccountId,
                PreviousHash = currentHash,
                NewHash = composition.EffectiveGridHash,
                composition.BaseSourceHash,
                PersonalizationStatus = composition.Personalization.Status.ToString()
            }, cancellationToken);

            return await InstallSnapshotAsync(selectedAccount, composition.EffectiveSnapshot, installSnapshot, settings, UpdateTriggerKind.Automatic, cancellationToken);
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public async Task<UpdateRunResult> InstallGridAsync(
        IReadOnlyList<SteamAccount> accounts,
        AppSettings settings,
        InstallGridSnapshot installSnapshot,
        CancellationToken cancellationToken)
    {
        await _updateGate.WaitAsync(cancellationToken);
        try
        {
            var selectedAccount = ResolveSelectedAccount(accounts);
            if (selectedAccount is null)
            {
                return NoSelectedAccountResult(UpdateTriggerKind.ManualInstall);
            }

            var snapshot = installSnapshot.Snapshot;
            if (snapshot.OriginKind == GridOriginKind.Cached)
            {
                return new UpdateRunResult
                {
                    Status = UpdateStatus.SourceUnavailable,
                    Message = "MetaGrid requires a validated live snapshot before installation. Run Check for Updates again before installing cached-only data.",
                    GridHash = snapshot.Hash,
                    ChangedHeroCount = CountChangedHeroes(snapshot),
                    ProviderStatus = snapshot.ProviderStatus,
                    OriginKind = snapshot.OriginKind,
                    SourceName = snapshot.SourceName,
                    SourceStrategy = snapshot.SourceStrategy,
                    RetrievedAt = snapshot.CapturedAt,
                    ProviderMessage = snapshot.SourceDetails,
                    TargetPath = selectedAccount.HeroGridConfigPath,
                    ConfirmedInstallSnapshot = installSnapshot
                };
            }

            if (!string.Equals(selectedAccount.AccountId, installSnapshot.AccountId, StringComparison.OrdinalIgnoreCase))
            {
                return FailureResult(
                    "The selected Steam account changed after confirmation. Run Check for Updates again before installing.",
                    snapshot,
                    selectedAccount.CurrentMetaGridHash,
                    CountChangedHeroes(snapshot),
                    selectedAccount.HeroGridConfigPath,
                    "Selected account mismatch.",
                    installSnapshot);
            }

            if (!string.Equals(Path.GetFullPath(selectedAccount.HeroGridConfigPath), Path.GetFullPath(installSnapshot.TargetPath), StringComparison.OrdinalIgnoreCase))
            {
                return FailureResult(
                    "The confirmed install target no longer matches the selected Steam account. Run Check for Updates again before installing.",
                    snapshot,
                    selectedAccount.CurrentMetaGridHash,
                    CountChangedHeroes(snapshot),
                    selectedAccount.HeroGridConfigPath,
                    "Confirmed target path mismatch.",
                    installSnapshot);
            }

            await loggingService.LogAsync(LogLevelKind.Information, "Manual install uses confirmed snapshot without provider refresh.", new
            {
                installSnapshot.AccountId,
                installSnapshot.TargetPath,
                SnapshotHash = snapshot.Hash,
                snapshot.SourceName,
                snapshot.SourceStrategy,
                RefreshRequested = false,
                ProviderFetchInvoked = false,
                BrowserFallbackInvoked = false
            }, cancellationToken);

            return await InstallSnapshotAsync(selectedAccount, snapshot, installSnapshot, settings, UpdateTriggerKind.ManualInstall, cancellationToken);
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public async Task<UpdateRunResult> ForceInstallLatestAsync(
        IReadOnlyList<SteamAccount> accounts,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        await _updateGate.WaitAsync(cancellationToken);
        try
        {
            var selectedAccount = ResolveSelectedAccount(accounts);
            if (selectedAccount is null)
            {
                return NoSelectedAccountResult(UpdateTriggerKind.ManualInstall);
            }

            var fetch = await TryFetchBaseSnapshotAsync(settings, forceRefresh: true, UpdateTriggerKind.ManualInstall, cancellationToken);
            if (!fetch.Succeeded || fetch.BaseSnapshot is null)
            {
                return fetch.FailureResult!;
            }

            var snapshot = fetch.BaseSnapshot;
            var snapshotValidation = await ValidateSnapshotAsync(snapshot, cancellationToken);
            if (!snapshotValidation.IsValid)
            {
                return FailureResult(
                    "The provider snapshot failed canonical validation and cannot be used for installation.",
                    snapshot,
                    await dotaGridService.ReadInstalledMetaGridHashAsync(selectedAccount.HeroGridConfigPath, cancellationToken),
                    CountChangedHeroes(snapshot),
                    selectedAccount.HeroGridConfigPath,
                    snapshotValidation.Error,
                    trigger: UpdateTriggerKind.ManualInstall);
            }

            if (snapshot.OriginKind == GridOriginKind.Cached)
            {
                return new UpdateRunResult
                {
                    Status = UpdateStatus.SourceUnavailable,
                    Message = "MetaGrid requires a validated live snapshot before force-installing the latest grid.",
                    GridHash = snapshot.Hash,
                    ChangedHeroCount = CountChangedHeroes(snapshot),
                    ProviderStatus = snapshot.ProviderStatus,
                    OriginKind = snapshot.OriginKind,
                    SourceName = snapshot.SourceName,
                    SourceStrategy = snapshot.SourceStrategy,
                    RetrievedAt = snapshot.CapturedAt,
                    ProviderMessage = snapshot.SourceDetails,
                    TargetPath = selectedAccount.HeroGridConfigPath,
                    Trigger = UpdateTriggerKind.ManualInstall
                };
            }

            var composition = await ComposeEffectiveSnapshotAsync(snapshot, settings, selectedAccount, forceRefresh: true, cancellationToken);
            var installSnapshot = CreateInstallSnapshot(selectedAccount, composition);
            return await InstallSnapshotAsync(selectedAccount, composition.EffectiveSnapshot, installSnapshot, settings, UpdateTriggerKind.ManualInstall, cancellationToken, forceReinstall: true);
        }
        finally
        {
            _updateGate.Release();
        }
    }

    private async Task<UpdateRunResult> InstallSnapshotAsync(
        SteamAccount account,
        HeroGridSnapshot snapshot,
        InstallGridSnapshot installSnapshot,
        AppSettings settings,
        UpdateTriggerKind trigger,
        CancellationToken cancellationToken,
        bool forceReinstall = false)
    {
        var changedHeroCount = CountChangedHeroes(snapshot);
        var expectedCanonical = dotaGridService.CanonicalizeSnapshot(snapshot);
        var expectedHash = dotaGridService.ComputeCanonicalHash(expectedCanonical);
        var currentHash = await dotaGridService.ReadInstalledMetaGridHashAsync(account.HeroGridConfigPath, cancellationToken);

        if (!string.Equals(expectedHash, snapshot.Hash, StringComparison.OrdinalIgnoreCase))
        {
            return FailureResult(
                "The confirmed install snapshot is internally inconsistent. Run Check for Updates again before installing.",
                snapshot,
                currentHash,
                changedHeroCount,
                account.HeroGridConfigPath,
                $"Snapshot hash mismatch. Confirmed={snapshot.Hash}, Canonical={expectedHash}",
                installSnapshot);
        }

        if (!forceReinstall && string.Equals(currentHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            return new UpdateRunResult
            {
                Status = UpdateStatus.AlreadyUpToDate,
                Message = "The selected Dota account already has this MetaGrid-managed hero grid installed.",
                GridHash = snapshot.Hash,
                ChangedHeroCount = changedHeroCount,
                ProviderStatus = snapshot.ProviderStatus,
                OriginKind = snapshot.OriginKind,
                SourceName = snapshot.SourceName,
                SourceStrategy = snapshot.SourceStrategy,
                RetrievedAt = snapshot.CapturedAt,
                ProviderMessage = snapshot.SourceDetails,
                InstalledHash = currentHash,
                AvailableHash = snapshot.Hash,
                BaseSourceHash = installSnapshot.BaseSourceHash,
                EffectiveGridHash = installSnapshot.EffectiveGridHash ?? snapshot.Hash,
                PersonalizationStatus = installSnapshot.PersonalizationStatus,
                PersonalizationMessage = installSnapshot.PersonalizationMessage,
                PersonalizationAccountId = installSnapshot.PersonalizationAccountId,
                PersonalizationUsedCache = installSnapshot.PersonalizationUsedCache,
                TargetPath = account.HeroGridConfigPath,
                ConfirmedInstallSnapshot = installSnapshot
                ,
                Trigger = trigger
            };
        }

        var targetValidation = ValidateTargetAccount(account);
        if (!targetValidation.IsValid)
        {
            await loggingService.LogAsync(LogLevelKind.Error, "Manual install target validation failed.", new
            {
                account.AccountId,
                account.HeroGridConfigPath,
                targetValidation.Error
            }, cancellationToken);

                return FailureResult(targetValidation.Error!, snapshot, currentHash, changedHeroCount, account.HeroGridConfigPath, targetValidation.Error, installSnapshot, trigger);
            }

        var snapshotValidation = await ValidateSnapshotAsync(snapshot, cancellationToken);
        if (!snapshotValidation.IsValid)
        {
            return FailureResult("Generated grid validation failed before touching the live Dota file.", snapshot, currentHash, changedHeroCount, account.HeroGridConfigPath, snapshotValidation.Error, installSnapshot, trigger);
        }

        var operationId = Guid.NewGuid().ToString("N");
        var targetPath = account.HeroGridConfigPath;
        var targetExisted = File.Exists(targetPath);
        var originalBytes = targetExisted ? await File.ReadAllBytesAsync(targetPath, cancellationToken) : null;
        var originalJson = targetExisted ? await File.ReadAllTextAsync(targetPath, cancellationToken) : null;
        var originalHash = currentHash;
        var backupEntry = default(BackupEntry);
        var liveFileTouched = false;
        var diagnostics = new InstallValidationDiagnostics
        {
            OperationId = operationId,
            AccountId = account.AccountId,
            SourceName = snapshot.SourceName,
            SourceStrategy = snapshot.SourceStrategy,
            ConfirmedAvailableHash = snapshot.Hash,
            InstallSnapshotHash = expectedHash
        };

        try
        {
            DotaHeroGridFile existing;
            if (targetExisted)
            {
                if (!dotaGridService.TryValidate(originalJson!, out var originalValidationError))
                {
                    return FailureResult("The existing Dota hero-grid file is not valid JSON. MetaGrid aborted before changing anything.", snapshot, currentHash, changedHeroCount, targetPath, originalValidationError, installSnapshot);
                }

                existing = await dotaGridService.ReadAsync(targetPath, cancellationToken);
                backupEntry = await backupService.BackupAsync(targetPath, settings.BackupRetentionCount, new BackupMetadata
                {
                    AccountId = account.AccountId,
                    AccountDisplayName = account.DisplayName,
                    OriginalPath = targetPath,
                    PreWriteHash = originalHash,
                    OperationId = operationId
                }, cancellationToken);

                if (backupEntry is null)
                {
                    return FailureResult("Backup creation failed. MetaGrid aborted before changing the live Dota file.", snapshot, currentHash, changedHeroCount, targetPath, "Expected an on-disk backup for the existing hero_grid_config.json, but none was created.", installSnapshot);
                }
            }
            else
            {
                Directory.CreateDirectory(account.DotaConfigDirectory);
                existing = new DotaHeroGridFile();
            }

            var merged = dotaGridService.ApplySnapshot(existing, snapshot);
            var serialized = dotaGridService.Serialize(merged);
            if (!dotaGridService.TryValidate(serialized, out var mergedValidationError))
            {
                return FailureResult("Merged hero-grid JSON failed validation before writing.", snapshot, currentHash, changedHeroCount, targetPath, mergedValidationError, installSnapshot);
            }

            var reparsed = await ParseJsonAsync(serialized, cancellationToken);
            var generatedValidation = ValidateInstalledDocument(reparsed, expectedCanonical, expectedHash);
            diagnostics.GeneratedMetaGridHash = generatedValidation.ActualHash;
            if (!generatedValidation.IsValid)
            {
                diagnostics.ValidationStage = "GeneratedDocument";
                diagnostics.SemanticDiffSummary = generatedValidation.SemanticDiffSummary;
                await LogInstallValidationFailureAsync(diagnostics, cancellationToken);
                return FailureResult("Merged hero-grid document failed structural validation before writing.", snapshot, currentHash, changedHeroCount, targetPath, generatedValidation.Error, installSnapshot);
            }

            await WriteAtomicallyAsync(targetPath, serialized, cancellationToken);
            liveFileTouched = true;

            var liveValidation = await ValidateLiveFileAsync(targetPath, expectedCanonical, expectedHash, cancellationToken);
            diagnostics.FinalFileRereadHash = liveValidation.ActualHash;
            if (!liveValidation.IsValid)
            {
                diagnostics.ValidationStage = "FinalFileReread";
                diagnostics.SemanticDiffSummary = liveValidation.SemanticDiffSummary;
                await LogInstallValidationFailureAsync(diagnostics, cancellationToken);
                throw new InvalidOperationException($"Post-write validation failed: {liveValidation.Error}");
            }

            account.CurrentMetaGridHash = snapshot.Hash;
            var installedAt = DateTimeOffset.Now;
            var operationName = trigger == UpdateTriggerKind.Automatic
                ? "Automatic update"
                : string.IsNullOrWhiteSpace(originalHash) ? "Install grid" : "Update grid";
            var successEntry = new UpdateHistoryEntry
            {
                Timestamp = installedAt,
                Status = UpdateStatus.Updated,
                GridHash = snapshot.Hash,
                Source = snapshot.SourceName,
                SourceStrategy = snapshot.SourceStrategy,
                AccountId = account.AccountId,
                AccountDisplayName = account.DisplayName,
                Operation = operationName,
                Message = string.IsNullOrWhiteSpace(originalHash)
                    ? "Installed the first MetaGrid-managed Dota hero grid successfully."
                    : "Updated the MetaGrid-managed Dota hero grid successfully.",
                PreviousHash = originalHash,
                NewHash = snapshot.Hash,
                AvailableHash = snapshot.Hash,
                InstalledHash = snapshot.Hash,
                BaseSourceHash = installSnapshot.BaseSourceHash,
                EffectiveGridHash = installSnapshot.EffectiveGridHash ?? snapshot.Hash,
                PersonalizationStatus = installSnapshot.PersonalizationStatus,
                PersonalizationMessage = installSnapshot.PersonalizationMessage,
                PersonalizationAccountId = installSnapshot.PersonalizationAccountId,
                PersonalizationUsedCache = installSnapshot.PersonalizationUsedCache,
                BackupFilePath = backupEntry?.FilePath,
                BackupOperationId = backupEntry?.OperationId,
                BackupHash = backupEntry?.BackupHash,
                TargetPath = targetPath,
                ChangedHeroCount = changedHeroCount,
                Trigger = trigger
            };
            await historyService.AppendAsync([successEntry], cancellationToken);

            return new UpdateRunResult
            {
                Status = UpdateStatus.Updated,
                Message = trigger == UpdateTriggerKind.Automatic
                    ? string.IsNullOrWhiteSpace(originalHash)
                        ? "MetaGrid automatically installed the first managed hero grid into the selected Dota account."
                        : "MetaGrid automatically installed the latest Dota2ProTracker hero grid into the selected Dota account."
                    : string.IsNullOrWhiteSpace(originalHash)
                        ? "MetaGrid installed the first managed hero grid into the selected Dota account."
                        : "MetaGrid updated the managed hero grid for the selected Dota account.",
                GridHash = snapshot.Hash,
                ChangedHeroCount = changedHeroCount,
                ProviderStatus = snapshot.ProviderStatus,
                OriginKind = snapshot.OriginKind,
                SourceName = snapshot.SourceName,
                SourceStrategy = snapshot.SourceStrategy,
                RetrievedAt = snapshot.CapturedAt,
                ProviderMessage = snapshot.SourceDetails,
                InstalledHash = snapshot.Hash,
                AvailableHash = snapshot.Hash,
                BaseSourceHash = installSnapshot.BaseSourceHash,
                EffectiveGridHash = installSnapshot.EffectiveGridHash ?? snapshot.Hash,
                PersonalizationStatus = installSnapshot.PersonalizationStatus,
                PersonalizationMessage = installSnapshot.PersonalizationMessage,
                PersonalizationAccountId = installSnapshot.PersonalizationAccountId,
                PersonalizationUsedCache = installSnapshot.PersonalizationUsedCache,
                PersonalHeroCount = installSnapshot.Snapshot.Layouts
                    .FirstOrDefault(layout => layout.Name.Contains("All Roles", StringComparison.OrdinalIgnoreCase))?
                    .Categories.FirstOrDefault(category => string.Equals(category.Name, "MY BEST HEROES", StringComparison.OrdinalIgnoreCase))?
                    .HeroIds.Count ?? 0,
                BackupEntry = backupEntry,
                OperationId = operationId,
                TargetPath = targetPath,
                ConfirmedInstallSnapshot = installSnapshot,
                Trigger = trigger
            };
        }
        catch (Exception ex)
        {
            var rollback = liveFileTouched
                ? await TryRollbackAsync(targetPath, originalBytes, backupEntry, cancellationToken)
                : (Succeeded: true, Error: (string?)null);
            var failureMessage = !liveFileTouched
                ? $"MetaGrid aborted before touching the live Dota configuration. Reason: {ex.Message}"
                : rollback.Succeeded
                    ? targetExisted
                        ? $"MetaGrid restored the original configuration after a failed write. Reason: {ex.Message}"
                        : $"Installation was rolled back. The newly created hero grid file was removed because validation failed. Reason: {ex.Message}"
                    : $"CRITICAL: MetaGrid could not restore the original configuration after a failed write. Reason: {ex.Message}. Rollback error: {rollback.Error}";

            await loggingService.LogAsync(rollback.Succeeded ? LogLevelKind.Error : LogLevelKind.Critical, trigger == UpdateTriggerKind.Automatic ? "Automatic install failed." : "Manual install failed.", new
            {
                account.AccountId,
                TargetPath = targetPath,
                InstallOperationId = operationId,
                LiveFileTouched = liveFileTouched,
                BackupFilePath = backupEntry?.FilePath,
                BackupOperationId = backupEntry?.OperationId,
                RollbackSucceeded = rollback.Succeeded,
                RollbackError = rollback.Error,
                ErrorMessage = ex.Message,
                diagnostics.SourceName,
                diagnostics.SourceStrategy,
                diagnostics.ConfirmedAvailableHash,
                diagnostics.InstallSnapshotHash,
                diagnostics.GeneratedMetaGridHash,
                diagnostics.FinalFileRereadHash,
                diagnostics.ValidationStage,
                diagnostics.SemanticDiffSummary
            }, cancellationToken);

            var failureEntry = new UpdateHistoryEntry
            {
                Timestamp = DateTimeOffset.Now,
                Status = liveFileTouched && rollback.Succeeded ? UpdateStatus.BackupRestored : UpdateStatus.Failed,
                GridHash = snapshot.Hash,
                Source = snapshot.SourceName,
                SourceStrategy = snapshot.SourceStrategy,
                AccountId = account.AccountId,
                AccountDisplayName = account.DisplayName,
                Operation = string.IsNullOrWhiteSpace(originalHash) ? "Install grid" : "Update grid",
                Message = failureMessage,
                PreviousHash = originalHash,
                NewHash = liveFileTouched && rollback.Succeeded ? originalHash : null,
                AvailableHash = snapshot.Hash,
                InstalledHash = liveFileTouched && rollback.Succeeded ? originalHash : null,
                BaseSourceHash = installSnapshot.BaseSourceHash,
                EffectiveGridHash = installSnapshot.EffectiveGridHash ?? snapshot.Hash,
                PersonalizationStatus = installSnapshot.PersonalizationStatus,
                PersonalizationMessage = installSnapshot.PersonalizationMessage,
                PersonalizationAccountId = installSnapshot.PersonalizationAccountId,
                PersonalizationUsedCache = installSnapshot.PersonalizationUsedCache,
                BackupFilePath = backupEntry?.FilePath,
                BackupOperationId = backupEntry?.OperationId ?? operationId,
                BackupHash = backupEntry?.BackupHash,
                TargetPath = targetPath,
                ChangedHeroCount = 0,
                Trigger = trigger
            };
            await historyService.AppendAsync([failureEntry], cancellationToken);

            return new UpdateRunResult
            {
                Status = liveFileTouched && rollback.Succeeded ? UpdateStatus.BackupRestored : UpdateStatus.Failed,
                Message = failureMessage,
                GridHash = snapshot.Hash,
                ChangedHeroCount = changedHeroCount,
                ProviderStatus = snapshot.ProviderStatus,
                OriginKind = snapshot.OriginKind,
                SourceName = snapshot.SourceName,
                SourceStrategy = snapshot.SourceStrategy,
                RetrievedAt = snapshot.CapturedAt,
                ProviderMessage = ex.Message,
                InstalledHash = liveFileTouched && rollback.Succeeded ? originalHash : null,
                AvailableHash = snapshot.Hash,
                BaseSourceHash = installSnapshot.BaseSourceHash,
                EffectiveGridHash = installSnapshot.EffectiveGridHash ?? snapshot.Hash,
                PersonalizationStatus = installSnapshot.PersonalizationStatus,
                PersonalizationMessage = installSnapshot.PersonalizationMessage,
                PersonalizationAccountId = installSnapshot.PersonalizationAccountId,
                PersonalizationUsedCache = installSnapshot.PersonalizationUsedCache,
                BackupEntry = backupEntry,
                OperationId = operationId,
                TargetPath = targetPath,
                ConfirmedInstallSnapshot = installSnapshot,
                Trigger = trigger
            };
        }
    }

    private InstallGridSnapshot CreateInstallSnapshot(SteamAccount account, EffectiveGridCompositionResult composition)
        => new()
        {
            Snapshot = composition.EffectiveSnapshot,
            AccountId = account.AccountId,
            AccountDisplayName = account.DisplayName,
            TargetPath = account.HeroGridConfigPath,
            PreparedAt = DateTimeOffset.UtcNow,
            BaseSourceHash = composition.BaseSourceHash,
            EffectiveGridHash = composition.EffectiveGridHash,
            PersonalizationStatus = composition.Personalization.Status.ToString(),
            PersonalizationMessage = composition.Personalization.Message,
            PersonalizationAccountId = composition.Personalization.AccountId,
            PersonalizationUsedCache = composition.Personalization.UsedCache,
            HeroCount = CountChangedHeroes(composition.EffectiveSnapshot),
            GroupCount = composition.EffectiveSnapshot.Layouts.SelectMany(layout => layout.Categories).Select(category => category.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            HasExistingGridFile = File.Exists(account.HeroGridConfigPath)
        };

    private async Task<(bool Succeeded, HeroGridSnapshot? BaseSnapshot, UpdateRunResult? FailureResult)> TryFetchBaseSnapshotAsync(
        AppSettings settings,
        bool forceRefresh,
        UpdateTriggerKind trigger,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await provider.FetchAsync(settings.PreferredPreset, cancellationToken);
            return (true, snapshot, null);
        }
        catch (HeroGridProviderParseException ex)
        {
            await loggingService.LogAsync(LogLevelKind.Warning, "Hero grid parsing failed", new { ex.Message, forceRefresh, trigger }, cancellationToken);
            return (false, null, new UpdateRunResult
            {
                Status = UpdateStatus.ParsingFailed,
                Message = trigger == UpdateTriggerKind.Automatic
                    ? "MetaGrid reached Dota2ProTracker, but the downloaded hero-grid payload could not be parsed."
                    : "MetaGrid reached the active data source, but the returned hero-grid data could not be parsed.",
                GridHash = settings.LastInstalledHash ?? string.Empty,
                ProviderStatus = ProviderStatus.ParsingFailed,
                ProviderMessage = ex.Message,
                Trigger = trigger
            });
        }
        catch (HeroGridProviderUnavailableException ex)
        {
            await loggingService.LogAsync(LogLevelKind.Warning, "Hero grid unavailable", new { ex.Message, ex.Status, forceRefresh, trigger }, cancellationToken);
            return (false, null, new UpdateRunResult
            {
                Status = UpdateStatus.SourceUnavailable,
                Message = trigger == UpdateTriggerKind.Automatic
                    ? "MetaGrid could not retrieve the official Dota2ProTracker High Winrate grid during the automatic update cycle."
                    : "The active hero-grid data source is currently unavailable.",
                GridHash = settings.LastInstalledHash ?? string.Empty,
                ProviderStatus = ex.Status,
                ProviderMessage = ex.Message,
                Trigger = trigger
            });
        }
        catch (Exception ex)
        {
            await loggingService.LogAsync(LogLevelKind.Error, "Hero grid source unavailable", new { ex.Message, forceRefresh, trigger }, cancellationToken);
            return (false, null, new UpdateRunResult
            {
                Status = UpdateStatus.UnexpectedResponse,
                Message = trigger == UpdateTriggerKind.Automatic
                    ? "MetaGrid received an unexpected response while running the automatic D2PT update cycle."
                    : "MetaGrid received an unexpected response while checking the active hero-grid data source.",
                GridHash = settings.LastInstalledHash ?? string.Empty,
                ProviderStatus = ProviderStatus.UnexpectedResponse,
                ProviderMessage = ex.Message,
                Trigger = trigger
            });
        }
    }

    private async Task<EffectiveGridCompositionResult> ComposeEffectiveSnapshotAsync(
        HeroGridSnapshot baseSnapshot,
        AppSettings settings,
        SteamAccount? selectedAccount,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var personalization = await personalizationService.ResolveAsync(
            settings,
            BuildPersonalizationAccountContext(settings, selectedAccount),
            forceRefresh,
            cancellationToken);
        var effectiveSnapshot = personalizedGridComposer.Compose(baseSnapshot, personalization);
        return new EffectiveGridCompositionResult
        {
            BaseSnapshot = baseSnapshot,
            EffectiveSnapshot = effectiveSnapshot,
            BaseSourceHash = baseSnapshot.Hash,
            EffectiveGridHash = effectiveSnapshot.Hash,
            Personalization = personalization
        };
    }

    private static PersonalizationAccountContext BuildPersonalizationAccountContext(AppSettings settings, SteamAccount? selectedAccount)
        => settings.PersonalizationAccountSourceMode == PersonalizationAccountSourceMode.ManualAccount
            ? new PersonalizationAccountContext
            {
                SourceMode = PersonalizationAccountSourceMode.ManualAccount,
                AccountId = settings.PersonalizationManualAccountId ?? settings.PersonalizationAccountId
            }
            : new PersonalizationAccountContext
            {
                SourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount,
                AccountId = selectedAccount?.AccountId,
                DisplayName = selectedAccount?.DisplayName
            };

    private UpdateRunResult CreateCheckResult(
        EffectiveGridCompositionResult composition,
        string? currentHash,
        string targetPath,
        int changedHeroCount,
        bool hashesMatch,
        bool isLiveSnapshot,
        InstallGridSnapshot installSnapshot,
        UpdateTriggerKind trigger)
    {
        var snapshot = composition.EffectiveSnapshot;
        return new UpdateRunResult
        {
            Status = hashesMatch && isLiveSnapshot ? UpdateStatus.AlreadyUpToDate : UpdateStatus.UpdateAvailable,
            Message = snapshot.OriginKind == GridOriginKind.Cached
                ? $"Using cached grid from {snapshot.CapturedAt.LocalDateTime:g}. Live providers were unavailable during the most recent refresh."
                : hashesMatch
                    ? "Your installed MetaGrid hash already matches the latest available live grid."
                    : $"Live grid preview ready from {snapshot.SourceName}. Install Grid will use this exact validated snapshot without refreshing providers again.",
            GridHash = composition.EffectiveGridHash,
            ChangedHeroCount = changedHeroCount,
            ProviderStatus = snapshot.ProviderStatus,
            OriginKind = snapshot.OriginKind,
            SourceName = snapshot.SourceName,
            SourceStrategy = snapshot.SourceStrategy,
            RetrievedAt = snapshot.CapturedAt,
            ProviderMessage = BuildProviderMessage(snapshot.SourceDetails, composition.Personalization),
            InstalledHash = currentHash,
            AvailableHash = composition.EffectiveGridHash,
            BaseSourceHash = composition.BaseSourceHash,
            EffectiveGridHash = composition.EffectiveGridHash,
            PersonalizationStatus = composition.Personalization.Status.ToString(),
            PersonalizationMessage = composition.Personalization.Message,
            PersonalizationAccountId = composition.Personalization.AccountId,
            PersonalizationAccountDisplayName = composition.Personalization.DisplayName,
            PersonalizationUsedCache = composition.Personalization.UsedCache,
            PersonalHeroCount = composition.Personalization.Selection?.SelectedHeroes.Count ?? 0,
            TargetPath = targetPath,
            ConfirmedInstallSnapshot = installSnapshot,
            Trigger = trigger
        };
    }

    private static string BuildProviderMessage(string? sourceDetails, PersonalizationResolution personalization)
    {
        var personalMessage = personalization.Status switch
        {
            PersonalizationStatus.Disabled => "Personal heroes off.",
            PersonalizationStatus.Ready => "Personal heroes ready.",
            PersonalizationStatus.NoQualifyingHeroes => "No qualifying heroes in last 90 days.",
            PersonalizationStatus.ProfileUnavailable => "Profile private or match data unavailable.",
            PersonalizationStatus.Cached => "Using cached personal heroes.",
            PersonalizationStatus.OpenDotaUnavailable => "OpenDota unavailable.",
            PersonalizationStatus.InvalidInput => "Personal profile input invalid.",
            _ => string.Empty
        };

        return string.IsNullOrWhiteSpace(sourceDetails)
            ? personalMessage
            : $"{sourceDetails} {personalMessage}".Trim();
    }

    private async Task<(bool IsValid, string? Error)> ValidateSnapshotAsync(HeroGridSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.Layouts.Count == 0)
        {
            return (false, "CanonicalValidationFailed:NoLayouts");
        }

        var knownHeroIds = (await heroCatalogService.LoadByNameAsync(cancellationToken))
            .Values
            .Select(hero => hero.Id)
            .ToHashSet();

        foreach (var layout in snapshot.Layouts)
        {
            if (layout.Categories.Count == 0)
            {
                return (false, $"CanonicalValidationFailed:LayoutWithoutCategories:{layout.Name}");
            }

            foreach (var category in layout.Categories)
            {
                if (category.HeroIds.Count == 0)
                {
                    return (false, $"CanonicalValidationFailed:EmptyCategory:{layout.Name}:{category.Name}");
                }

                foreach (var heroId in category.HeroIds)
                {
                    if (heroId <= 0 || !knownHeroIds.Contains(heroId))
                    {
                        return (false, $"CanonicalValidationFailed:InvalidHeroId:{layout.Name}:{category.Name}:{heroId}");
                    }
                }
            }
        }

        if (string.Equals(snapshot.SourceStrategy, "OfficialDownloadPayload", StringComparison.OrdinalIgnoreCase))
        {
            var missingGroups = PreferredRoleGroups
                .Where(role => !snapshot.Layouts.Any(layout =>
                    layout.Name.Contains(role, StringComparison.OrdinalIgnoreCase) ||
                    layout.Categories.Any(category => category.Name.Contains(role, StringComparison.OrdinalIgnoreCase))))
                .ToArray();
            if (missingGroups.Length > 0)
            {
                return (false, $"CanonicalValidationFailed:MissingOfficialGroups:{string.Join(",", missingGroups)}");
            }
        }

        var heroCount = CountChangedHeroes(snapshot);
        if (heroCount < 15)
        {
            return (false, $"CanonicalValidationFailed:ImplausibleHeroCount:{heroCount}");
        }

        var canonicalHash = dotaGridService.ComputeSnapshotHash(snapshot);
        if (!string.Equals(canonicalHash, snapshot.Hash, StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"CanonicalValidationFailed:HashMismatch:Snapshot={snapshot.Hash};Canonical={canonicalHash}");
        }

        return (true, null);
    }

    private (bool IsValid, string? Error, string? ActualHash, string? SemanticDiffSummary) ValidateInstalledDocument(
        DotaHeroGridFile file,
        CanonicalHeroGridSnapshot expectedCanonical,
        string expectedHash)
    {
        var nativeStructure = dotaGridService.ValidateNativeStructure(file);
        if (!nativeStructure.IsValid)
        {
            return (false, $"The generated Dota hero-grid file does not match required native structure: {nativeStructure.Error}", null, null);
        }

        if (file.Version < 3)
        {
            return (false, "The generated Dota hero-grid file uses an invalid version.", null, null);
        }

        var managedSnapshot = dotaGridService.ExtractManagedGrid(file);
        if (managedSnapshot is null)
        {
            return (false, "The generated document does not contain the MetaGrid-managed grid.", null, null);
        }

        var managedLayouts = file.Configs
            .Where(layout => layout.ConfigName.StartsWith("MetaGrid - ", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var layout in managedLayouts)
        {
            foreach (var category in layout.Categories)
            {
                if (category.Width <= 0 || category.Height <= 0)
                {
                    return (false, $"The category '{category.CategoryName}' has malformed layout dimensions.", null, null);
                }
            }
        }

        if (managedLayouts.Count == 0)
        {
            return (false, "The generated document does not contain any MetaGrid-managed layouts.", null, null);
        }

        var actualHash = dotaGridService.ComputeCanonicalHash(managedSnapshot);
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "The generated document does not normalize to the expected MetaGrid hash.", actualHash, dotaGridService.SummarizeDifference(expectedCanonical, managedSnapshot));
        }

        return (true, null, actualHash, null);
    }

    private async Task<(bool IsValid, string? Error, string? ActualHash, string? SemanticDiffSummary)> ValidateLiveFileAsync(
        string targetPath,
        CanonicalHeroGridSnapshot expectedCanonical,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        var liveJson = await File.ReadAllTextAsync(targetPath, cancellationToken);
        if (!dotaGridService.TryValidate(liveJson, out var jsonError))
        {
            return (false, $"The live file is no longer valid JSON after writing: {jsonError}", null, null);
        }

        var reparsed = await ParseJsonAsync(liveJson, cancellationToken);
        return ValidateInstalledDocument(reparsed, expectedCanonical, expectedHash);
    }

    private static async Task<DotaHeroGridFile> ParseJsonAsync(string json, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var file = await System.Text.Json.JsonSerializer.DeserializeAsync<DotaHeroGridFile>(stream, cancellationToken: cancellationToken);
        return file ?? new DotaHeroGridFile();
    }

    private static async Task WriteAtomicallyAsync(string destinationPath, string contents, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var tempPath = destinationPath + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        await using (var writer = new StreamWriter(stream))
        {
            await writer.WriteAsync(contents.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        var tempJson = await File.ReadAllTextAsync(tempPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(tempJson))
        {
            throw new InvalidOperationException("The temporary Dota hero-grid file is empty.");
        }

        if (File.Exists(destinationPath))
        {
            var backupPath = destinationPath + ".metagrid-write-backup";
            File.Replace(tempPath, destinationPath, backupPath, ignoreMetadataErrors: true);
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }
        else
        {
            File.Move(tempPath, destinationPath, overwrite: true);
        }

        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }
    }

    private async Task<(bool Succeeded, string? Error)> TryRollbackAsync(
        string targetPath,
        byte[]? originalBytes,
        BackupEntry? backupEntry,
        CancellationToken cancellationToken)
    {
        try
        {
            if (backupEntry is not null)
            {
                var restored = await backupService.RestoreAsync(backupEntry, targetPath, cancellationToken);
                if (!restored)
                {
                    return (false, "MetaGrid could not restore the verified backup file.");
                }

                return (true, null);
            }

            if (originalBytes is not null)
            {
                await File.WriteAllBytesAsync(targetPath, originalBytes, cancellationToken);
                var restored = await File.ReadAllBytesAsync(targetPath, cancellationToken);
                if (!restored.AsSpan().SequenceEqual(originalBytes))
                {
                    return (false, "The restored file bytes do not match the original bytes.");
                }

                return (true, null);
            }

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static int CountChangedHeroes(HeroGridSnapshot snapshot)
        => snapshot.Layouts
            .SelectMany(layout => layout.Categories)
            .SelectMany(category => category.HeroIds)
            .Distinct()
            .Count();

    private static SteamAccount? ResolveSelectedAccount(IReadOnlyList<SteamAccount> accounts)
    {
        var selected = accounts.Where(account => account.IsSelected).ToList();
        return selected.Count == 1 ? selected[0] : null;
    }

    private static UpdateRunResult NoSelectedAccountResult(UpdateTriggerKind trigger)
        => new()
        {
            Status = UpdateStatus.WaitingForAccount,
            Message = "MetaGrid could not resolve exactly one selected Dota 2 account.",
            GridHash = string.Empty,
            ProviderStatus = ProviderStatus.Unknown,
            Trigger = trigger
        };

    private static (bool IsValid, string? Error) ValidateTargetAccount(SteamAccount account)
    {
        if (!Directory.Exists(account.SteamRootPath))
        {
            return (false, "The selected Steam installation no longer exists.");
        }

        if (!Directory.Exists(account.UserDataPath))
        {
            return (false, "The selected Steam userdata account no longer exists.");
        }

        var dotaAppPath = Path.Combine(account.UserDataPath, "570");
        if (!Directory.Exists(dotaAppPath))
        {
            return (false, "The selected Steam account does not contain Dota AppID 570.");
        }

        var cfgPath = Path.GetFullPath(account.DotaConfigDirectory);
        var expectedCfgPath = Path.GetFullPath(Path.Combine(account.UserDataPath, "570", "remote", "cfg"));
        if (!string.Equals(cfgPath, expectedCfgPath, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "The resolved Dota configuration target does not belong to the currently selected Steam account.");
        }

        if (!cfgPath.EndsWith(Path.Combine("570", "remote", "cfg"), StringComparison.OrdinalIgnoreCase))
        {
            return (false, "The resolved Dota configuration target is malformed.");
        }

        return (true, null);
    }

    private static UpdateRunResult FailureResult(
        string message,
        HeroGridSnapshot snapshot,
        string? currentHash,
        int changedHeroCount,
        string targetPath,
        string? providerMessage,
        InstallGridSnapshot? installSnapshot = null,
        UpdateTriggerKind trigger = UpdateTriggerKind.ManualCheck)
        => new()
        {
            Status = UpdateStatus.Failed,
            Message = message,
            GridHash = snapshot.Hash,
            ChangedHeroCount = changedHeroCount,
            ProviderStatus = snapshot.ProviderStatus,
            OriginKind = snapshot.OriginKind,
            SourceName = snapshot.SourceName,
            SourceStrategy = snapshot.SourceStrategy,
            RetrievedAt = snapshot.CapturedAt,
            ProviderMessage = providerMessage,
            InstalledHash = currentHash,
            AvailableHash = snapshot.Hash,
            BaseSourceHash = installSnapshot?.BaseSourceHash,
            EffectiveGridHash = installSnapshot?.EffectiveGridHash ?? snapshot.Hash,
            PersonalizationStatus = installSnapshot?.PersonalizationStatus,
            PersonalizationMessage = installSnapshot?.PersonalizationMessage,
            PersonalizationAccountId = installSnapshot?.PersonalizationAccountId,
            PersonalizationUsedCache = installSnapshot?.PersonalizationUsedCache ?? false,
            TargetPath = targetPath,
            ConfirmedInstallSnapshot = installSnapshot
            ,
            Trigger = trigger
        };

    private Task LogInstallValidationFailureAsync(InstallValidationDiagnostics diagnostics, CancellationToken cancellationToken)
        => loggingService.LogAsync(LogLevelKind.Error, "Install validation mismatch detected.", new
        {
            diagnostics.OperationId,
            diagnostics.AccountId,
            diagnostics.SourceName,
            diagnostics.SourceStrategy,
            diagnostics.ConfirmedAvailableHash,
            diagnostics.InstallSnapshotHash,
            diagnostics.GeneratedMetaGridHash,
            diagnostics.FinalFileRereadHash,
            diagnostics.ValidationStage,
            diagnostics.SemanticDiffSummary
        }, cancellationToken);

    private sealed class InstallValidationDiagnostics
    {
        public string? OperationId { get; set; }
        public string? AccountId { get; set; }
        public string? SourceName { get; set; }
        public string? SourceStrategy { get; set; }
        public string? ConfirmedAvailableHash { get; set; }
        public string? InstallSnapshotHash { get; set; }
        public string? GeneratedMetaGridHash { get; set; }
        public string? FinalFileRereadHash { get; set; }
        public string? ValidationStage { get; set; }
        public string? SemanticDiffSummary { get; set; }
    }

    private sealed class NullPersonalizationService : IPersonalizationService
    {
        public Task<PersonalizationResolution> ConnectAsync(string profileInput, CancellationToken cancellationToken)
            => Task.FromResult(new PersonalizationResolution
            {
                Status = PersonalizationStatus.Disabled,
                SourceMode = PersonalizationAccountSourceMode.ManualAccount,
                Message = "Personal heroes are disabled."
            });

        public Task DisconnectAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;

        public ProfileInputParseResult ParseProfileInput(string? input) => ProfileInputParseResult.Failure("Personalization disabled.");

        public Task<PersonalizationResolution> RefreshAsync(AppSettings settings, PersonalizationAccountContext accountContext, CancellationToken cancellationToken)
            => Task.FromResult(new PersonalizationResolution
            {
                Status = PersonalizationStatus.Disabled,
                SourceMode = accountContext.SourceMode,
                Message = "Personal heroes are disabled."
            });

        public Task<PersonalizationResolution> ResolveAsync(AppSettings settings, PersonalizationAccountContext accountContext, bool forceRefresh, CancellationToken cancellationToken)
            => Task.FromResult(new PersonalizationResolution
            {
                Status = PersonalizationStatus.Disabled,
                SourceMode = accountContext.SourceMode,
                Message = "Personal heroes are disabled."
            });
    }

    private sealed class PassThroughPersonalizedGridComposer : IPersonalizedGridComposer
    {
        public HeroGridSnapshot Compose(HeroGridSnapshot baseSnapshot, PersonalizationResolution personalization) => baseSnapshot;
    }
}
