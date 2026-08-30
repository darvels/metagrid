using MetaGrid.Core.Abstractions;

namespace MetaGrid.Tests.TestSupport;

internal sealed class TestAppPaths(string root) : IAppPaths
{
    private readonly string _root = root;

    public string RootDirectory => _root;
    public string SettingsFilePath => Path.Combine(_root, "Settings", "settings.json");
    public string HistoryFilePath => Path.Combine(_root, "History", "updates.json");
    public string LogsDirectory => Path.Combine(_root, "Logs");
    public string BackupsDirectory => Path.Combine(_root, "Backups");
    public string CacheDirectory => Path.Combine(_root, "Cache");
    public string CachedGridSnapshotPath => Path.Combine(CacheDirectory, "last-good-grid.json");
    public string CachedGridPayloadPath => Path.Combine(CacheDirectory, "last-good-grid.raw.json");
    public string PersonalizationCacheDirectory => Path.Combine(CacheDirectory, "Personalization");
    public string D2ptCacheDirectory => Path.Combine(CacheDirectory, "D2PT");
    public string D2ptTempDirectory => Path.Combine(D2ptCacheDirectory, "Temp");
    public string D2ptWebView2ProfileDirectory => Path.Combine(_root, "WebView2", "D2PT");
    public string AppUpdateDirectory => Path.Combine(_root, "AppUpdate");
    public string AppUpdateSessionDirectory => Path.Combine(AppUpdateDirectory, "Sessions");
}
