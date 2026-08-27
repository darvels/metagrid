using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Win32;
using MetaGrid.Core.Abstractions;

namespace MetaGrid.Infrastructure.Services;

[SupportedOSPlatform("windows")]
public sealed class StartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MetaGrid";

    public void ApplyLaunchAtStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
        {
            key.SetValue(AppName, BuildStartupCommand());
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    private static string BuildStartupCommand()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            !processPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return $"\"{processPath}\" --minimized";
        }

        var entryAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
        if (!string.IsNullOrWhiteSpace(entryAssemblyName))
        {
            var baseDirectory = AppContext.BaseDirectory;
            var executablePath = Path.Combine(baseDirectory, $"{entryAssemblyName}.exe");
            if (File.Exists(executablePath))
            {
                return $"\"{executablePath}\" --minimized";
            }

            var dllPath = Path.Combine(baseDirectory, $"{entryAssemblyName}.dll");
            if (File.Exists(dllPath))
            {
                return $"\"dotnet\" \"{dllPath}\" --minimized";
            }
        }

        return "\"MetaGrid.exe\" --minimized";
    }
}
