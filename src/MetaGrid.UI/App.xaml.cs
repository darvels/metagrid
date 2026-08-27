using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;
using MetaGrid.UI.Services;
using MetaGrid.UI.ViewModels;

namespace MetaGrid.UI;

public partial class App : System.Windows.Application
{
    public static IServiceProvider Services { get; private set; } = default!;
    private bool _initializationStarted;

    public App()
    {
        StartupDiagnostics.LogDebug("MetaGrid application constructor started.");
        RegisterGlobalExceptionHandlers();

        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IAppPaths, AppPaths>();
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IHistoryService, HistoryService>();
            services.AddSingleton<ILoggingService, FileLoggingService>();
            services.AddSingleton<ISteamLocatorService, SteamLocatorService>();
            services.AddSingleton<IDotaGridService, DotaGridService>();
            services.AddSingleton<ISteamAccountService, SteamAccountService>();
            services.AddSingleton<IHeroCatalogService, HeroCatalogService>();
            services.AddSingleton<ID2ptOfficialGridRetrievalService, D2ptOfficialGridRetrievalService>();
            services.AddSingleton<GridSnapshotCacheService>();
            services.AddSingleton<IHeroGridProvider, OfficialD2ptHeroGridProvider>();
            services.AddSingleton<IBackupService, BackupService>();
            services.AddSingleton<IGridComparisonService, GridComparisonService>();
            services.AddSingleton<IUpdateService, UpdateService>();
            services.AddSingleton<IStartupService, StartupService>();
            services.AddSingleton<IAppClock, AppClock>();
            services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
            services.AddSingleton<NotificationService>();
            services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<NotificationService>());
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
            Services = services.BuildServiceProvider();
            StartupDiagnostics.LogDebug("Service provider built successfully.");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.LogException("Failed during dependency injection setup.", ex);
            throw;
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupDiagnostics.LogDebug("OnStartup entered.");
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        try
        {
            var window = Services.GetRequiredService<MainWindow>();
            StartupDiagnostics.LogDebug("MainWindow resolved from ServiceProvider.");
            MainWindow = window;
            StartupDiagnostics.LogDebug("MainWindow assigned to Application.Current.MainWindow.");

            PrepareInitialWindow(window);

            window.SourceInitialized += (_, _) =>
            {
                var handle = new WindowInteropHelper(window).Handle;
                StartupDiagnostics.LogDebug($"MainWindow SourceInitialized. HWND=0x{handle.ToInt64():X}");
            };

            window.Loaded += (_, _) =>
            {
                var source = PresentationSource.FromVisual(window);
                var handle = new WindowInteropHelper(window).Handle;
                StartupDiagnostics.LogDebug($"MainWindow.Loaded. IsVisible={window.IsVisible}; IsLoaded={window.IsLoaded}; HWND=0x{handle.ToInt64():X}; PresentationSourceNull={source is null}");
            };

            window.ContentRendered += async (_, _) =>
            {
                StartupDiagnostics.LogDebug($"MainWindow.ContentRendered. Left={window.Left}; Top={window.Top}; Width={window.Width}; Height={window.Height}; WindowState={window.WindowState}; ShowInTaskbar={window.ShowInTaskbar}; Visibility={window.Visibility}");
                StartupDiagnostics.LogDebug($"Owned top-level windows for process: {CountTopLevelWindowsForCurrentProcess()}");

                if (_initializationStarted)
                {
                    return;
                }

                _initializationStarted = true;
                await InitializeAfterShowAsync(window);
            };

            window.Closed += (_, _) =>
            {
                StartupDiagnostics.LogDebug("MainWindow closed.");
                if (ShutdownMode != ShutdownMode.OnExplicitShutdown)
                {
                    Shutdown(0);
                }
            };

            window.Show();
            StartupDiagnostics.LogDebug($"MainWindow.Show called. IsVisible={window.IsVisible}; IsLoaded={window.IsLoaded}");
            window.Activate();
            StartupDiagnostics.LogDebug("MainWindow.Activate called.");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.LogException("Fatal startup failure.", ex);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            StartupDiagnostics.LogDebug("Application exit started.");
            if (Services.GetService<MainViewModel>() is { } viewModel)
            {
                await viewModel.ShutdownAsync();
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.LogException("Failure during application exit.", ex);
        }

        base.OnExit(e);
    }

    private async Task InitializeAfterShowAsync(Window window)
    {
        try
        {
            var viewModel = Services.GetRequiredService<MainViewModel>();
            await viewModel.InitializeAsync();
            StartupDiagnostics.LogDebug("MainViewModel initialized successfully after MainWindow was shown.");

            if (viewModel.ShouldStartMinimized)
            {
                window.WindowState = WindowState.Minimized;
                StartupDiagnostics.LogDebug("Start minimized setting applied after initial window render.");
            }

            ShutdownMode = ShutdownMode.OnMainWindowClose;
            StartupDiagnostics.LogDebug("ShutdownMode switched to OnMainWindowClose after successful initialization.");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.LogException("Non-fatal initialization failure after MainWindow show.", ex);

            if (Services.GetService<MainViewModel>() is { } viewModel)
            {
                viewModel.ReportNonFatalInitializationFailure(ex);
            }

            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
    }

    private static void PrepareInitialWindow(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.WindowState = WindowState.Normal;
        window.ShowInTaskbar = true;
        window.Visibility = Visibility.Visible;
        window.Width = 1100;
        window.Height = 720;
        window.MinWidth = 960;
        window.MinHeight = 640;
        window.Opacity = 1;
        window.ShowActivated = true;
        StartupDiagnostics.LogDebug($"Initial window geometry prepared. Left={window.Left}; Top={window.Top}; Width={window.Width}; Height={window.Height}");
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        StartupDiagnostics.LogException("Dispatcher unhandled exception.", e.Exception);
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            StartupDiagnostics.LogException("AppDomain unhandled exception.", exception);
        }
        else
        {
            StartupDiagnostics.LogDebug($"AppDomain unhandled non-exception object: {e.ExceptionObject}");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        StartupDiagnostics.LogException("Unobserved task exception.", e.Exception);
    }

    private static int CountTopLevelWindowsForCurrentProcess()
    {
        var count = 0;
        var currentPid = Environment.ProcessId;
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out var processId);
            if (processId == currentPid)
            {
                count++;
            }

            return true;
        }, IntPtr.Zero);
        return count;
    }
}

internal static class StartupDiagnostics
{
    private static readonly string LogDirectory = ResolveStartupLogDirectory();

    public static void LogDebug(string message) => Write("DEBUG", message);
    public static void LogInfo(string message) => Write("INFO", message);
    public static void LogException(string message, Exception exception) => Write("ERROR", $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {level} {message}";

        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(Path.Combine(LogDirectory, "startup.log"), line + Environment.NewLine + Environment.NewLine);
        }
        catch
        {
            // Best effort logging only.
        }

        try
        {
            Debug.WriteLine(line);
            if (string.Equals(level, "ERROR", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(line);
            }
            else
            {
                Console.WriteLine(line);
            }
        }
        catch
        {
            // No console available is acceptable for WinExe startup.
        }
    }

    private static string ResolveStartupLogDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MetaGrid", "Logs"),
            Path.Combine(AppContext.BaseDirectory, "AppData", "Logs"),
            Path.Combine(Path.GetTempPath(), "MetaGrid", "Logs")
        };

        foreach (var candidate in candidates)
        {
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
                // Try next path.
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "Logs");
    }
}

internal static class NativeMethods
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
}
