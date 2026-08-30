using MetaGrid.Core.Abstractions;

namespace MetaGrid.Infrastructure.Services;

public sealed class AppPaths : IAppPaths
{
    public AppPaths()
    {
        RootDirectory = ResolveWritableRootDirectory();
        SettingsFilePath = Path.Combine(RootDirectory, "Settings", "settings.json");
        HistoryFilePath = Path.Combine(RootDirectory, "History", "updates.json");
        LogsDirectory = Path.Combine(RootDirectory, "Logs");
        BackupsDirectory = Path.Combine(RootDirectory, "Backups");
        CacheDirectory = Path.Combine(RootDirectory, "Cache");
        CachedGridSnapshotPath = Path.Combine(CacheDirectory, "last-good-grid.json");
        CachedGridPayloadPath = Path.Combine(CacheDirectory, "last-good-grid.raw.json");
        PersonalizationCacheDirectory = Path.Combine(CacheDirectory, "Personalization");
        D2ptCacheDirectory = Path.Combine(CacheDirectory, "D2PT");
        D2ptTempDirectory = Path.Combine(D2ptCacheDirectory, "Temp");
        D2ptWebView2ProfileDirectory = Path.Combine(RootDirectory, "WebView2", "D2PT");
        AppUpdateDirectory = Path.Combine(RootDirectory, "AppUpdate");
        AppUpdateSessionDirectory = Path.Combine(AppUpdateDirectory, "Sessions");

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(HistoryFilePath)!);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(PersonalizationCacheDirectory);
        Directory.CreateDirectory(D2ptCacheDirectory);
        Directory.CreateDirectory(D2ptTempDirectory);
        Directory.CreateDirectory(D2ptWebView2ProfileDirectory);
        Directory.CreateDirectory(AppUpdateDirectory);
        Directory.CreateDirectory(AppUpdateSessionDirectory);
    }

    public string RootDirectory { get; }
    public string SettingsFilePath { get; }
    public string HistoryFilePath { get; }
    public string LogsDirectory { get; }
    public string BackupsDirectory { get; }
    public string CacheDirectory { get; }
    public string CachedGridSnapshotPath { get; }
    public string CachedGridPayloadPath { get; }
    public string PersonalizationCacheDirectory { get; }
    public string D2ptCacheDirectory { get; }
    public string D2ptTempDirectory { get; }
    public string D2ptWebView2ProfileDirectory { get; }
    public string AppUpdateDirectory { get; }
    public string AppUpdateSessionDirectory { get; }

    private static string ResolveWritableRootDirectory()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("METAGRID_APPDATA_ROOT");
        var candidates = new[]
        {
            overrideRoot,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MetaGrid"),
            Path.Combine(AppContext.BaseDirectory, "AppData"),
            Path.Combine(Path.GetTempPath(), "MetaGrid")
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(candidate);
                var probePath = Path.Combine(candidate, ".write-test");
                File.WriteAllText(probePath, "ok");
                File.Delete(probePath);
                return candidate;
            }
            catch
            {
                // Try the next candidate.
            }
        }

        throw new InvalidOperationException("MetaGrid could not find a writable application data directory.");
    }
}
