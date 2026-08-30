using System.Text;
using System.Text.Json;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Utilities;

namespace MetaGrid.Infrastructure.Services;

public sealed class SettingsService(IAppPaths appPaths, ILoggingService loggingService) : ISettingsService
{
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(appPaths.SettingsFilePath))
        {
            return Normalize(new AppSettings());
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(appPaths.SettingsFilePath, cancellationToken);
            if (bytes.Length == 0 || string.IsNullOrWhiteSpace(Encoding.UTF8.GetString(bytes)))
            {
                return await RecoverCorruptSettingsAsync("The settings file was empty.", cancellationToken);
            }

            await using var stream = new MemoryStream(bytes, writable: false);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonDefaults.Storage, cancellationToken);
            return Normalize(settings ?? new AppSettings());
        }
        catch (JsonException ex)
        {
            return await RecoverCorruptSettingsAsync(ex.Message, cancellationToken);
        }
        catch (NotSupportedException ex)
        {
            return await RecoverCorruptSettingsAsync(ex.Message, cancellationToken);
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(appPaths.SettingsFilePath)!);
        var normalized = Normalize(settings);
        var tempPath = appPaths.SettingsFilePath + ".tmp";

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, JsonDefaults.Storage, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(appPaths.SettingsFilePath))
            {
                var backupPath = appPaths.SettingsFilePath + ".bak";
                File.Replace(tempPath, appPaths.SettingsFilePath, backupPath, ignoreMetadataErrors: true);
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
            else
            {
                File.Move(tempPath, appPaths.SettingsFilePath, overwrite: true);
            }
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private async Task<AppSettings> RecoverCorruptSettingsAsync(string reason, CancellationToken cancellationToken)
    {
        var corruptPath = appPaths.SettingsFilePath + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
        Directory.CreateDirectory(Path.GetDirectoryName(appPaths.SettingsFilePath)!);

        try
        {
            if (File.Exists(appPaths.SettingsFilePath))
            {
                File.Move(appPaths.SettingsFilePath, corruptPath, overwrite: true);
            }
        }
        catch (Exception moveException)
        {
            await loggingService.LogAsync(LogLevelKind.Warning, "MetaGrid could not move the corrupt settings file aside.", new
            {
                appPaths.SettingsFilePath,
                corruptPath,
                MoveError = moveException.Message
            }, cancellationToken);
        }

        await loggingService.LogAsync(LogLevelKind.Warning, "MetaGrid recovered from a corrupt settings file and loaded defaults.", new
        {
            appPaths.SettingsFilePath,
            corruptPath,
            Reason = reason
        }, cancellationToken);

        var defaults = Normalize(new AppSettings());
        await SaveAsync(defaults, cancellationToken);
        return defaults;
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.SelectedAccountIds ??= [];
        settings.BackupRetentionCount = Math.Clamp(settings.BackupRetentionCount, 1, 50);
        settings.LastRoleSummary ??= string.Empty;
        settings.LastAppUpdateState ??= AppUpdateCheckState.Unknown.ToString();
        settings.LastAppUpdateMessage ??= string.Empty;
        settings.LastAvailableAppVersion = string.IsNullOrWhiteSpace(settings.LastAvailableAppVersion)
            ? null
            : settings.LastAvailableAppVersion.Trim();
        settings.DeferredAppUpdateVersion = string.IsNullOrWhiteSpace(settings.DeferredAppUpdateVersion)
            ? null
            : settings.DeferredAppUpdateVersion.Trim();
        if (settings.DeferredAppUpdateUntil <= DateTimeOffset.MinValue)
        {
            settings.DeferredAppUpdateUntil = null;
        }

        settings.PersonalizationAccountId = string.IsNullOrWhiteSpace(settings.PersonalizationAccountId)
            ? null
            : settings.PersonalizationAccountId.Trim();
        settings.PersonalizationManualAccountId = string.IsNullOrWhiteSpace(settings.PersonalizationManualAccountId)
            ? null
            : settings.PersonalizationManualAccountId.Trim();
        if (settings.PersonalizationAccountSourceMode == PersonalizationAccountSourceMode.ManualAccount
            && string.IsNullOrWhiteSpace(settings.PersonalizationManualAccountId)
            && !string.IsNullOrWhiteSpace(settings.PersonalizationAccountId))
        {
            settings.PersonalizationManualAccountId = settings.PersonalizationAccountId;
        }

        if (settings.PersonalizationAccountSourceMode == PersonalizationAccountSourceMode.SelectedSteamAccount)
        {
            settings.PersonalizationManualAccountId = null;
        }

        settings.LastPersonalizationMessage ??= string.Empty;
        settings.LastPersonalizationStatus ??= settings.PersonalizationEnabled && !string.IsNullOrWhiteSpace(settings.PersonalizationAccountId)
            ? PersonalizationStatus.ProfileUnavailable.ToString()
            : PersonalizationStatus.Disabled.ToString();
        settings.LastBaseSourceHash ??= settings.LastRemoteHash;
        settings.LastEffectiveGridHash ??= settings.LastRemoteHash;
        return settings;
    }
}
