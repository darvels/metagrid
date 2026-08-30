using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Resources;
using MetaGrid.Core.Models;
using MetaGrid.UI.Services;
using MetaGrid.UI.ViewModels;

namespace MetaGrid.UI;

public partial class MainWindow : Window
{
    private readonly NotifyIcon? _notifyIcon;
    private readonly Stream? _trayIconStream;
    private readonly System.Drawing.Icon? _trayIcon;
    private readonly MainViewModel _viewModel;
    private readonly UiTextService _text;
    private bool _closeHintShownThisSession;
    private readonly ToolStripMenuItem _statusMenuItem;

    public MainWindow()
    {
        StartupDiagnostics.LogDebug("MainWindow constructor started.");
        InitializeComponent();
        StartupDiagnostics.LogDebug("MainWindow constructor completed InitializeComponent.");

        _viewModel = (MainViewModel)App.Services.GetService(typeof(MainViewModel))!;
        _text = (UiTextService)App.Services.GetService(typeof(UiTextService))!;
        DataContext = _viewModel;
        StartupDiagnostics.LogDebug("MainWindow constructor assigned DataContext.");

        _statusMenuItem = new ToolStripMenuItem("Status: Starting")
        {
            Enabled = false
        };

        (_trayIcon, _trayIconStream) = LoadTrayIcon();
        _notifyIcon = new NotifyIcon
        {
            Text = "MetaGrid",
            Icon = _trayIcon ?? System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowFromTray();
        _notifyIcon.ContextMenuStrip!.Opening += (_, _) => _statusMenuItem.Text = _text.Language == AppLanguage.Russian
            ? $"Статус: {_viewModel.StatusChipText}"
            : $"Status: {_viewModel.StatusChipText}";

        ((NotificationService)App.Services.GetService(typeof(NotificationService))!).NotifyIcon = _notifyIcon;
        StartupDiagnostics.LogDebug($"MainWindow initialized. Tray icon loaded={_trayIcon is not null}.");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_notifyIcon is not null && _viewModel.Settings.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        _trayIcon?.Dispose();
        _trayIconStream?.Dispose();
        base.OnClosed(e);
    }

    private static (System.Drawing.Icon? icon, Stream? stream) LoadTrayIcon()
    {
        var resourceStream = System.Windows.Application.GetResourceStream(new Uri("Assets/metagrid.ico", UriKind.Relative));
        if (resourceStream?.Stream is { } assemblyIconStream)
        {
            var memoryStream = new MemoryStream();
            assemblyIconStream.CopyTo(memoryStream);
            memoryStream.Position = 0;
            return (new System.Drawing.Icon(memoryStream), memoryStream);
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "metagrid.ico");
        if (File.Exists(iconPath))
        {
            var fileStream = File.OpenRead(iconPath);
            return (new System.Drawing.Icon(fileStream), fileStream);
        }

        return (null, null);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("MetaGrid");
        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(_text.Translate("Open MetaGrid"), null, (_, _) => ShowFromTray());
        menu.Items.Add(_text.CheckForUpdates, null, (_, _) => _viewModel.CheckNowCommand.Execute(null));
        menu.Items.Add(_text.Settings, null, (_, _) =>
        {
            ShowFromTray();
            _viewModel.SelectedPage = AppPage.Settings;
        });
        menu.Items.Add(_text.T("Exit", "Выход"), null, (_, _) =>
        {
            if (_notifyIcon is not null)
            {
                _notifyIcon.Visible = false;
            }

            _viewModel.Settings.CloseToTray = false;
            Close();
        });
        return menu;
    }

    private void HideToTray()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        ShowInTaskbar = false;
        Hide();
        StartupDiagnostics.LogDebug("MainWindow hidden to tray.");

        if (!_closeHintShownThisSession && !_viewModel.Settings.HasSeenCloseToTrayNotification)
        {
            _closeHintShownThisSession = true;
            _viewModel.Settings.HasSeenCloseToTrayNotification = true;
            ((NotificationService)App.Services.GetService(typeof(NotificationService))!)
                .ShowInfo("MetaGrid is still running", "Background hero-grid updates continue. Use the tray icon to reopen or exit MetaGrid.");
        }
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Visibility = Visibility.Visible;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
        StartupDiagnostics.LogDebug("MainWindow restored from tray.");
    }

    private async void RestoreBackupClick(object sender, RoutedEventArgs e)
    {
        StartupDiagnostics.LogDebug($"RestoreBackupClick entered. SelectedAccount={_viewModel.SelectedAccountText}; Path={_viewModel.SelectedAccountPathText}");

        try
        {
            var backups = await _viewModel.GetSelectedAccountBackupsAsync();
            StartupDiagnostics.LogDebug($"RestoreBackupClick loaded backups. Count={backups.Count}");
            if (backups.Count == 0)
            {
                _viewModel.StatusTitle = "No backups available";
                _viewModel.StatusText = "MetaGrid has not created any account-specific backups for the selected profile yet.";
                StartupDiagnostics.LogDebug("RestoreBackupClick found zero backups for the selected account.");
                return;
            }

            var dialog = new BackupRestoreWindow(backups)
            {
                Owner = this
            };
            StartupDiagnostics.LogDebug("BackupRestoreWindow created and owner assigned.");

            var restoreDialogResult = dialog.ShowDialog();
            StartupDiagnostics.LogDebug($"BackupRestoreWindow closed. DialogResult={restoreDialogResult}; SelectedBackup={dialog.SelectedBackup?.FilePath}");
            if (restoreDialogResult == true && dialog.SelectedBackup is { } selectedBackup)
            {
                var confirmation = new RestoreConfirmationWindow(selectedBackup, _viewModel.SelectedAccountText, _viewModel.SelectedAccountPathText)
                {
                    Owner = this
                };
                StartupDiagnostics.LogDebug("RestoreConfirmationWindow created and owner assigned.");

                var confirmationResult = confirmation.ShowDialog();
                StartupDiagnostics.LogDebug($"RestoreConfirmationWindow closed. DialogResult={confirmationResult}");
                if (confirmationResult == true)
                {
                    await _viewModel.RestoreBackupAsync(selectedBackup);
                }
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.LogException("RestoreBackupClick failed.", ex);
            _viewModel.StatusTitle = "Restore dialog failed";
            _viewModel.StatusText = "MetaGrid could not open the backup restore flow. No Dota files were modified. Check the logs for the exact exception.";
            ((NotificationService)App.Services.GetService(typeof(NotificationService))!)
                .ShowError("MetaGrid restore dialog failed", "The backup restore flow could not be opened. No Dota files were modified.");
        }
    }

    private async void InstallGridClick(object sender, RoutedEventArgs e)
    {
        StartupDiagnostics.LogDebug("InstallGridClick entered.");

        try
        {
            var preview = _viewModel.BuildInstallPreview();
            StartupDiagnostics.LogDebug(preview is null
                ? "InstallGridClick preview creation returned null."
                : $"InstallGridClick preview created. AccountId={preview.AccountId}; Source={preview.Source}; TargetPath={preview.TargetPath}; HasExistingGridFile={preview.HasExistingGridFile}");

            if (preview is null)
            {
                _viewModel.StatusTitle = "Install unavailable";
                _viewModel.StatusText = "Run a successful live update check and select a Dota account before installing the MetaGrid-managed hero grid.";
                return;
            }

            var dialog = new InstallConfirmationWindow(preview)
            {
                Owner = this
            };

            StartupDiagnostics.LogDebug($"InstallConfirmationWindow owner assigned. OwnerIsVisible={IsVisible}; OwnerHandle=0x{new System.Windows.Interop.WindowInteropHelper(this).Handle.ToInt64():X}");
            var result = dialog.ShowDialog();
            StartupDiagnostics.LogDebug($"InstallConfirmationWindow closed. DialogResult={result}");

            if (result == true)
            {
                StartupDiagnostics.LogDebug("InstallGridClick proceeding to final confirmed install.");
                await _viewModel.InstallGridAsync(preview.ConfirmedSnapshot);
            }
            else
            {
                StartupDiagnostics.LogDebug("InstallGridClick canceled before any install work.");
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.LogException("InstallGridClick failed before installation.", ex);
            _viewModel.StatusTitle = "Install dialog failed";
            _viewModel.StatusText = "MetaGrid could not open the install confirmation dialog. No Dota files were modified. Check the logs for the exact exception.";
            ((NotificationService)App.Services.GetService(typeof(NotificationService))!)
                .ShowError("MetaGrid install dialog failed", "The confirmation dialog could not be opened. No Dota files were modified.");
        }
    }

    private void DashboardClick(object sender, RoutedEventArgs e) => _viewModel.SelectedPage = AppPage.Dashboard;
    private void HeroGridClick(object sender, RoutedEventArgs e) => _viewModel.SelectedPage = AppPage.HeroGrid;
    private void AccountsClick(object sender, RoutedEventArgs e) => _viewModel.SelectedPage = AppPage.Accounts;
    private void HistoryClick(object sender, RoutedEventArgs e) => _viewModel.SelectedPage = AppPage.UpdateHistory;
    private void SettingsClick(object sender, RoutedEventArgs e) => _viewModel.SelectedPage = AppPage.Settings;
    private void AboutClick(object sender, RoutedEventArgs e) => _viewModel.SelectedPage = AppPage.About;
}
