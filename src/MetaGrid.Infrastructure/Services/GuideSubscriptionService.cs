using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class GuideSubscriptionService(
    ID2ptGuideProvider provider,
    IDotaGuideMappingResolver mappingResolver,
    IValveGuideSerializer serializer,
    ISteamRemoteStorageGuideService remoteStorage,
    ILoggingService loggingService) : IGuideSubscriptionService
{
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    public async Task<GuideRemovalResult> RemoveAsync(SteamAccount account, GuideSubscriptionRecord subscription, CancellationToken cancellationToken)
    {
        await _syncGate.WaitAsync(cancellationToken);
        try
        {
            var result = await RemoveCoreAsync(account, subscription, cancellationToken);
            await loggingService.LogAsync(result.VerifiedAbsent ? LogLevelKind.Information : LogLevelKind.Warning,
                "Auto Guide removal completed.", new { account.AccountId, subscription.HeroId, subscription.Role, result }, CancellationToken.None);
            return result;
        }
        finally { _syncGate.Release(); }
    }

    private async Task<GuideRemovalResult> RemoveCoreAsync(SteamAccount account, GuideSubscriptionRecord subscription, CancellationToken token)
    {
        var remoteFile = subscription.RemoteFile;
        GuideRemovalResult Failure(string message, bool pending = false)
            => new(false, remoteFile, pending ? GuideSubscriptionStatus.RemovalPending : GuideSubscriptionStatus.RemovalFailed, message);
        if (subscription.IsEnabled || !subscription.NeedsRemoval)
            return Failure("Deletion requires an explicit disabled-role removal request.");
        if (subscription.InstalledAccountId is not null && subscription.InstalledAccountId != account.AccountId)
            return Failure("Installed guide account does not match the selected Steam account.");
        if (!GuideDeletionOwnership.IsKnownSubscription(subscription))
        {
            return remoteFile is null && subscription.InstalledHash is null
                ? new(true, null, GuideSubscriptionStatus.Disabled, "No installed owned guide to remove.")
                : Failure("This hero/role has no verified deletion ownership contract.");
        }
        if (remoteFile is not null && !GuideDeletionOwnership.IsSafeFile(remoteFile))
            return Failure("Persisted guide path does not match the safe MetaGrid filename contract.");
        try
        {
            if (!await remoteStorage.IsAvailableAsync(account, token))
                return Failure("Steam RemoteStorage is unavailable; removal remains pending.", true);
            var files = await remoteStorage.ListFilesAsync(account, token);
            if (remoteFile is null)
            {
                var recovered = new List<string>();
                foreach (var entry in files.Where(file => GuideDeletionOwnership.IsSafeFile(file.Name)))
                {
                    var bytes = await remoteStorage.ReadFileAsync(account, entry.Name, token);
                    if (bytes is null) continue;
                    try
                    {
                        if (GuideDeletionOwnership.IsOwned(account, subscription, entry.Name, serializer.Parse(System.Text.Encoding.UTF8.GetString(bytes))))
                            recovered.Add(entry.Name);
                    }
                    catch (FormatException) { }
                    catch (InvalidOperationException) { }
                }
                if (recovered.Count > 1) return Failure("Ambiguous owned-guide recovery; nothing was deleted.");
                remoteFile = recovered.SingleOrDefault();
                if (remoteFile is null) return new(true, null, GuideSubscriptionStatus.Disabled, "No owned guide found to remove.");
            }
            var existing = files.Where(file => file.Name == remoteFile).ToArray();
            if (existing.Length > 1) return Failure("Ambiguous remote file enumeration.");
            var content = await remoteStorage.ReadFileAsync(account, remoteFile, token);
            if (existing.Length == 0 && content is null)
                return new(true, remoteFile, GuideSubscriptionStatus.Disabled, "Owned guide is already absent.");
            if (content is null || existing.Length != 1 || !existing[0].Exists || !existing[0].Persisted)
                return Failure("Remote ownership/readback is not confirmed; removal remains pending.", true);
            var document = serializer.Parse(System.Text.Encoding.UTF8.GetString(content));
            if (!GuideDeletionOwnership.IsOwned(account, subscription, remoteFile, document))
                return Failure("Strict guide ownership preflight failed; nothing was deleted.");
            var expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
            await loggingService.LogAsync(LogLevelKind.Information, "Owned guide deletion preflight passed.", new { account.AccountId, remoteFile, subscription.HeroId, subscription.Role, expectedHash }, token);
            var delete = await remoteStorage.DeleteOwnedFileAsync(account, remoteFile, expectedHash, token);
            if (!delete.Succeeded || delete.ExistsAfterDelete || delete.PersistedAfterDelete)
                return Failure(delete.Error ?? "RemoteStorage delete was not verified.");
            var after = await remoteStorage.ListFilesAsync(account, token);
            var readback = await remoteStorage.ReadFileAsync(account, remoteFile, token);
            if (after.Any(file => file.Name == remoteFile && (file.Exists || file.Persisted)) || readback is not null)
                return Failure("Guide is still visible through RemoteStorage after deletion.");
            await loggingService.LogAsync(LogLevelKind.Information, "Owned guide deletion verified absent.", new { account.AccountId, remoteFile }, token);
            return new(true, remoteFile, GuideSubscriptionStatus.Disabled, "Owned Auto Guide deletion verified.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            await loggingService.LogAsync(LogLevelKind.Warning, "Owned guide removal failed; recovery metadata retained.", new { account.AccountId, remoteFile, ex.Message }, CancellationToken.None);
            return Failure(ex.Message);
        }
    }

    public async Task<GuideSubscriptionSyncResult> SyncAsync(SteamAccount account, GuideSubscriptionRecord subscription, CancellationToken cancellationToken)
    {
        await _syncGate.WaitAsync(cancellationToken);
        try { return await SyncCoreAsync(account, subscription, cancellationToken); }
        finally { _syncGate.Release(); }
    }

    private async Task<GuideSubscriptionSyncResult> SyncCoreAsync(SteamAccount account, GuideSubscriptionRecord subscription, CancellationToken cancellationToken)
    {
        if (!subscription.IsEnabled || subscription.NeedsRemoval)
        {
            return new GuideSubscriptionSyncResult
            {
                Status = GuideSubscriptionStatus.Disabled,
                Message = "Auto Guide is disabled."
            };
        }

        NormalizedHeroGuideBuild build;
        try
        {
            build = await provider.FetchAsync(subscription, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (D2ptNoGuideBuildException ex)
        {
            await loggingService.LogAsync(LogLevelKind.Information, "Public D2PT index contains no build for the requested hero/role.",
                new { subscription.HeroId, subscription.HeroInternalName, subscription.Role, ex.Message }, cancellationToken);
            return new GuideSubscriptionSyncResult { Status = GuideSubscriptionStatus.NoBuild, Message = ex.Message };
        }
        catch (GuideSourceDataException ex)
        {
            await loggingService.LogAsync(LogLevelKind.Warning, "D2PT source validation blocked the guide before Steam access.",
                new { subscription.HeroId, subscription.Role, ex.Kind, ex.Message }, cancellationToken);
            return new GuideSubscriptionSyncResult { Status = GuideSubscriptionStatus.SourceIncomplete, Message = "Source data incomplete." };
        }
        catch (Exception ex)
        {
            await loggingService.LogAsync(LogLevelKind.Warning, "Guide provider failed.", new
            {
                account.AccountId,
                subscription.HeroInternalName,
                subscription.Role,
                ex.Message
            }, cancellationToken);

            return new GuideSubscriptionSyncResult
            {
                Status = GuideSubscriptionStatus.SourceUnavailable,
                Message = ex.Message
            };
        }

        var mapping = await mappingResolver.ResolveAsync(account, subscription, build, cancellationToken);
        if (!mapping.Succeeded || mapping.Build is null)
        {
            await loggingService.LogAsync(LogLevelKind.Warning, "Auto Guide mapping preflight failed before Steam access.",
                new { subscription.HeroId, subscription.Role, mapping.FailureKind, mapping.Error }, cancellationToken);
            return new GuideSubscriptionSyncResult
            {
                Status = mapping.FailureKind switch
                {
                    GuideFailureKind.SourcePatchIncompatible => GuideSubscriptionStatus.SourcePatchIncompatible,
                    GuideFailureKind.SourceIncomplete => GuideSubscriptionStatus.SourceIncomplete,
                    _ => GuideSubscriptionStatus.MappingFailed
                },
                Message = mapping.FailureKind == GuideFailureKind.SourcePatchIncompatible ? "Source incompatible with current Dota patch."
                    : mapping.FailureKind == GuideFailureKind.SourceIncomplete ? "Source data incomplete."
                    : mapping.Error ?? "Current Dota mapping could not resolve the guide payload.",
                CanonicalSourceHash = build.CanonicalSourceHash,
                ProviderName = build.Source,
                SourceStrategy = build.SourceClassification,
                BuildTitle = build.SourceTitle,
                PatchLabel = build.GameplayVersion,
                RetrievedAtUtc = build.RetrievedAtUtc
            };
        }
        build.CanonicalSourceHash = serializer.ComputeCanonicalSourceHash(build);

        if (!await remoteStorage.IsAvailableAsync(account, cancellationToken))
            return new GuideSubscriptionSyncResult
            {
                Status = GuideSubscriptionStatus.SteamUnavailable,
                Message = "Steam RemoteStorage for Dota 2 is not available right now."
            };

        var resolvedBuild = new ResolvedHeroGuideBuild
        {
            ExpectedStartingItems = mapping.Build.ExpectedStartingItems,
            SourceBuild = mapping.Build.SourceBuild,
            HeroToken = mapping.Build.HeroToken,
            RoleToken = mapping.Build.RoleToken,
            GuideTitle = mapping.Build.GuideTitle,
            Overview = mapping.Build.Overview,
            OriginalCreatorIdHex = mapping.Build.OriginalCreatorIdHex,
            ItemGroups = mapping.Build.ItemGroups,
            SkillOrder = mapping.Build.SkillOrder,
            TalentChoices = mapping.Build.TalentChoices,
            EffectiveGuideHash = serializer.ComputeEffectiveGuideHash(mapping.Build)
        };

        var recoveredRemoteFile = subscription.RemoteFile;
        {
            var recovery = await TryRecoverRemoteFileAsync(account, subscription, build.SourceClassification is "Live" or "Cached live", cancellationToken);
            if (recovery.Ambiguous)
            {
                return new GuideSubscriptionSyncResult
                {
                    Status = GuideSubscriptionStatus.Conflict,
                    Message = recovery.Error ?? "Multiple possible owned Auto Guides were found in Steam RemoteStorage.",
                    CanonicalSourceHash = build.CanonicalSourceHash,
                    EffectiveGuideHash = resolvedBuild.EffectiveGuideHash,
                    ProviderName = build.Source,
                    SourceStrategy = build.SourceClassification,
                    BuildTitle = build.SourceTitle,
                    PatchLabel = build.GameplayVersion,
                    RetrievedAtUtc = build.RetrievedAtUtc
                };
            }

            recoveredRemoteFile = recovery.RemoteFile;
            if (!string.IsNullOrWhiteSpace(subscription.RemoteFile) && recoveredRemoteFile is not null
                && !string.Equals(subscription.RemoteFile, recoveredRemoteFile, StringComparison.OrdinalIgnoreCase))
                return new GuideSubscriptionSyncResult { Status = GuideSubscriptionStatus.Conflict, Message = "Persisted guide path differs from the owned remote guide. No write performed." };
        }

        // Verify real remote semantics, not only persisted local hashes.
        ParsedValveGuideDocument? installed = null;
        if (recoveredRemoteFile is not null)
        {
            var bytes = await remoteStorage.ReadFileAsync(account, recoveredRemoteFile, cancellationToken);
            if (bytes is null) return new GuideSubscriptionSyncResult { Status = GuideSubscriptionStatus.Conflict, Message = "Recovered guide disappeared before validation." };
            installed = serializer.Parse(System.Text.Encoding.UTF8.GetString(bytes));
        }
        var installedSemanticHash = installed is null ? null : serializer.ComputeEffectiveGuideHash(new ResolvedHeroGuideBuild
        {
            SourceBuild = new NormalizedHeroGuideBuild { GameplayVersion = installed.GameplayVersion },
            HeroToken = installed.HeroToken, RoleToken = installed.RoleToken, GuideTitle = installed.Title,
            Overview = installed.Overview, OriginalCreatorIdHex = installed.OriginalCreatorIdHex,
            ItemGroups = installed.ItemGroups, SkillOrder = installed.SkillOrder, TalentChoices = installed.TalentChoices
        });

        if (!string.IsNullOrWhiteSpace(recoveredRemoteFile)
            && string.Equals(installedSemanticHash, resolvedBuild.EffectiveGuideHash, StringComparison.OrdinalIgnoreCase))
        {
            return new GuideSubscriptionSyncResult
            {
                Status = GuideSubscriptionStatus.UpToDate,
                GuideRevision = installed?.GuideRevision ?? subscription.GuideRevision,
                Message = "The Auto Guide already matches the current D2PT source.",
                RemoteFile = recoveredRemoteFile,
                CanonicalSourceHash = build.CanonicalSourceHash,
                EffectiveGuideHash = resolvedBuild.EffectiveGuideHash,
                ProviderName = build.Source,
                SourceStrategy = build.SourceClassification,
                BuildTitle = build.SourceTitle,
                PatchLabel = build.GameplayVersion,
                RetrievedAtUtc = build.RetrievedAtUtc
            };
        }

        var remoteFile = recoveredRemoteFile ?? await CreateRemoteFileNameAsync(account, subscription, cancellationToken);
        var revision = string.IsNullOrWhiteSpace(subscription.RemoteFile) && string.IsNullOrWhiteSpace(recoveredRemoteFile)
            ? Math.Max(1, subscription.GuideRevision)
            : Math.Max(Math.Max(1, subscription.GuideRevision + 1), (installed?.GuideRevision ?? 0) + 1);
        GuideSerializationResult serialized;
        try
        {
            serialized = serializer.Serialize(resolvedBuild, revision, DateTimeOffset.UtcNow);
            if (resolvedBuild.ExpectedStartingItems is not null)
                StartingItemsIntegrity.RequireMatch(resolvedBuild.ExpectedStartingItems,
                    StartingItemsIntegrity.GroupItems(serializer.Parse(serialized.Text).ItemGroups), "pre-write serialized payload");
        }
        catch (InvalidDataException ex)
        {
            await loggingService.LogAsync(LogLevelKind.Warning, "Starting Items mapping blocker before Steam write.", new { ex.Message }, cancellationToken);
            return new GuideSubscriptionSyncResult { Status = GuideSubscriptionStatus.MappingFailed, Message = ex.Message };
        }
        var write = await remoteStorage.WriteFileAsync(account, remoteFile, serialized.Bytes, cancellationToken);
        var verifiedBytes = write.Succeeded ? await remoteStorage.ReadFileAsync(account, remoteFile, cancellationToken) : null;
        var remoteEntries = write.Succeeded ? await remoteStorage.ListFilesAsync(account, cancellationToken) : [];
        var entry = remoteEntries.SingleOrDefault(f => f.Name == remoteFile);
        if (!write.Succeeded || !write.ExistsAfterWrite || !write.PersistedAfterWrite
            || entry is null || !entry.Exists || !entry.Persisted || entry.Size != serialized.Bytes.Length
            || verifiedBytes is null || !verifiedBytes.AsSpan().SequenceEqual(serialized.Bytes))
        {
            return new GuideSubscriptionSyncResult
            {
                Status = GuideSubscriptionStatus.Error,
                Message = write.Error ?? "Steam RemoteStorage write/read-back verification failed.",
                RemoteFile = remoteFile,
                CanonicalSourceHash = build.CanonicalSourceHash,
                EffectiveGuideHash = resolvedBuild.EffectiveGuideHash,
                ProviderName = build.Source,
                SourceStrategy = build.SourceClassification,
                BuildTitle = build.SourceTitle,
                PatchLabel = build.GameplayVersion,
                RetrievedAtUtc = build.RetrievedAtUtc
            };
        }

        if (resolvedBuild.ExpectedStartingItems is not null)
        {
            try
            {
                StartingItemsIntegrity.RequireMatch(resolvedBuild.ExpectedStartingItems,
                    StartingItemsIntegrity.GroupItems(serializer.Parse(System.Text.Encoding.UTF8.GetString(verifiedBytes!)).ItemGroups), "Steam readback");
            }
            catch (InvalidDataException ex)
            {
                await loggingService.LogAsync(LogLevelKind.Warning, "Starting Items Steam readback mismatch.", new { ex.Message }, cancellationToken);
                return new GuideSubscriptionSyncResult { Status = GuideSubscriptionStatus.Error, Message = ex.Message, RemoteFile = remoteFile };
            }
        }

        return new GuideSubscriptionSyncResult
        {
            Status = string.IsNullOrWhiteSpace(recoveredRemoteFile) ? GuideSubscriptionStatus.Installed : GuideSubscriptionStatus.Installed,
            GuideRevision = revision,
            Message = string.IsNullOrWhiteSpace(recoveredRemoteFile)
                ? "The Auto Guide was created and registered in Steam RemoteStorage."
                : "The Auto Guide was updated in Steam RemoteStorage without creating duplicates.",
            RemoteFile = remoteFile,
            CanonicalSourceHash = build.CanonicalSourceHash,
            EffectiveGuideHash = serialized.EffectiveGuideHash,
            ProviderName = build.Source,
            SourceStrategy = build.SourceClassification,
            BuildTitle = build.SourceTitle,
            PatchLabel = build.GameplayVersion,
            RetrievedAtUtc = build.RetrievedAtUtc
        };
    }

    private async Task<RemoteGuideRecoveryResult> TryRecoverRemoteFileAsync(SteamAccount account, GuideSubscriptionRecord subscription, bool requireCreator, CancellationToken cancellationToken)
    {
        var files = await remoteStorage.ListFilesAsync(account, cancellationToken);
        var candidates = new List<string>();

        foreach (var file in files.Where(file => file.Name.StartsWith("guides/", StringComparison.OrdinalIgnoreCase)
                                                 && file.Name.EndsWith(".build", StringComparison.OrdinalIgnoreCase)))
        {
            var bytes = await remoteStorage.ReadFileAsync(account, file.Name, cancellationToken);
            if (bytes is null || bytes.Length == 0)
            {
                continue;
            }

            try
            {
                var parsed = serializer.Parse(System.Text.Encoding.UTF8.GetString(bytes));
                if (GuideDeletionOwnership.MatchesIdentity(subscription, file.Name, parsed))
                {
                    if (requireCreator && !GuideDeletionOwnership.IsOwned(account, subscription, file.Name, parsed))
                        return new RemoteGuideRecoveryResult { Ambiguous = true, Error = "Owned guide creator/path mismatch; no overwrite permitted." };
                    if (!file.Exists || !file.Persisted)
                        return new RemoteGuideRecoveryResult { Ambiguous = true, Error = "Owned remote guide is not confirmed persisted; no overwrite permitted." };
                    candidates.Add(file.Name);
                }
            }
            catch
            {
                // Ignore unrelated guide files.
            }
        }

        return candidates.Count switch
        {
            1 => new RemoteGuideRecoveryResult { Found = true, RemoteFile = candidates[0] },
            > 1 => new RemoteGuideRecoveryResult { Ambiguous = true, Error = $"Found {candidates.Count} possible owned Auto Guides." },
            _ => new RemoteGuideRecoveryResult()
        };
    }

    private async Task<string> CreateRemoteFileNameAsync(SteamAccount account, GuideSubscriptionRecord subscription, CancellationToken cancellationToken)
    {
        var prefix = GuideDeletionOwnership.FilePrefix(subscription);
        var baseName = prefix + ".build";
        var files = await remoteStorage.ListFilesAsync(account, cancellationToken);
        if (files.All(file => !string.Equals(file.Name, baseName, StringComparison.OrdinalIgnoreCase)))
        {
            return baseName;
        }

        var index = 2;
        while (index < 10000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = $"{prefix}{index}.build";
            if (files.All(file => !string.Equals(file.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }

            index++;
        }
        throw new InvalidOperationException("No free owned guide filename within the bounded collision range.");
    }
}
