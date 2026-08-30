using System.Diagnostics;
using System.Reflection;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class AppRuntimeInfoService : IAppRuntimeInfo
{
    private readonly Lazy<string> _version = new(ResolveVersion);

    public string GetCurrentVersion() => _version.Value;

    public string GetInstallDirectory() => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public string GetExecutablePath()
    {
        using var currentProcess = Process.GetCurrentProcess();
        return currentProcess.MainModule?.FileName
            ?? Path.Combine(GetInstallDirectory(), "MetaGrid.exe");
    }

    public string GetExecutableName() => Path.GetFileName(GetExecutablePath());

    public string GetUpdaterExecutablePath() => Path.Combine(GetInstallDirectory(), "MetaGrid.Updater.exe");

    public int GetProcessId() => Environment.ProcessId;

    public bool CanInstallUpdate(out string reason)
    {
        var installDirectory = GetInstallDirectory();
        try
        {
            if (!Directory.Exists(installDirectory))
            {
                reason = $"MetaGrid install directory does not exist: {installDirectory}";
                return false;
            }

            var probePath = Path.Combine(installDirectory, $".metagrid-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    private static string ResolveVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (AppVersionParser.TryParse(informational, out _, out var normalized) && !string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        if (AppVersionParser.TryParse(fileVersion, out _, out normalized) && !string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        var version = assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }
}
