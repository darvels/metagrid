using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;
using MetaGrid.UI.Services;

namespace MetaGrid.UI.ViewModels;

public enum AppPage
{
    Dashboard,
    HeroGrid,
    Accounts,
    UpdateHistory,
    Settings,
    About
}

public sealed class MainViewModel : ObservableObject
{
    private static readonly string[] KnownRoles =
    [
        "All Roles",
        "Carry",
        "Mid",
        "Offlane",
        "Support",
        "Hard Support"
    ];

    private readonly ISettingsService _settingsService;
    private readonly ISteamAccountService _steamAccountService;
    private readonly IUpdateService _updateService;
    private readonly IHistoryService _historyService;
    private readonly IBackupService _backupService;
    private readonly IDotaGridService _dotaGridService;
    private readonly IHeroCatalogService _heroCatalogService;
    private readonly ILoggingService _loggingService;
    private readonly INotificationService _notificationService;
    private readonly IStartupService _startupService;
    private readonly IAppPaths _appPaths;
    private readonly IAppClock _appClock;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly GridSnapshotCacheService _gridSnapshotCacheService;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private HeroGridSnapshot? _availableInstallSnapshot;
    private AppSettings _settings = new();
    private AppSettings _editableSettings = new();
    private string _settingsBaselineSnapshot = string.Empty;
    private AppPage _selectedPage = AppPage.Dashboard;
    private bool _isBusy;
    private bool _isInitializing = true;
    private bool _isSettingsDirty;
    private string _statusTitle = "Starting MetaGrid";
    private string _statusText = "Rendering the desktop shell and preparing your Dota 2 workspace.";
    private string _lastCheckedText = "Never";
    private string _lastUpdatedText = "Never";
    private string _selectedAccountText = "No account selected";
    private string _selectedAccountPathText = "Choose a Dota 2 account to see its configuration path.";
    private string _availableGridHashText = "Unknown";
    private string _installedGridHashText = "Not installed";
    private string _cachedGridHashText = "Not cached";
    private string _nextCheckText = "Not scheduled";
    private bool _onboardingVisible;
    private int _onboardingStep = 1;
    private string _providerStatusText = "Waiting for initialization";
    private string _providerCardTitle = "Data Source";
    private string _steamDetectionStatusText = "Steam not checked yet";
    private string _steamDetectionPathText = "No Steam installation has been detected yet.";
    private string _initializationWarning = string.Empty;
    private string _currentSourceText = "Dota2ProTracker not checked yet";
    private string _heroCountText = "Unknown";
    private string _roleSummaryText = string.Join(", ", KnownRoles);
    private string _gridCriteriaText = "Official Dota2ProTracker High Winrate grid";
    private string _topFivePreviewText = "Run Check for Updates to load the latest official Dota2ProTracker High Winrate layouts.";
    private string _statusChipText = "Not checked";
    private string _autoUpdateChipText = "Auto Update On";
    private string _providerStatusChipText = "Source not checked";
    private HeroGridLayoutPreview? _selectedGridPreviewLayout;
    private IReadOnlyDictionary<int, string>? _heroNamesById;
    private bool _steamAutoDetectionStarted;
    private CancellationTokenSource? _scheduleCts;
    private Task? _schedulerTask;
    private Task? _startupAutomaticUpdateTask;

    public MainViewModel(
        ISettingsService settingsService,
        ISteamAccountService steamAccountService,
        IUpdateService updateService,
        IHistoryService historyService,
        IBackupService backupService,
        IDotaGridService dotaGridService,
        IHeroCatalogService heroCatalogService,
        ILoggingService loggingService,
        INotificationService notificationService,
        IStartupService startupService,
        IAppPaths appPaths,
        IAppClock appClock,
        IUiDispatcher uiDispatcher,
        GridSnapshotCacheService gridSnapshotCacheService)
    {
        _settingsService = settingsService;
        _steamAccountService = steamAccountService;
        _updateService = updateService;
        _historyService = historyService;
        _backupService = backupService;
        _dotaGridService = dotaGridService;
        _heroCatalogService = heroCatalogService;
        _loggingService = loggingService;
        _notificationService = notificationService;
        _startupService = startupService;
        _appPaths = appPaths;
        _appClock = appClock;
        _uiDispatcher = uiDispatcher;
        _gridSnapshotCacheService = gridSnapshotCacheService;

        Accounts = [];
        History = [];
        GridPreviewLayouts = [];
        Presets = Enum.GetValues<HeroGridPreset>();
        UpdateIntervalOptions =
        [
            new UpdateIntervalOption(UpdateInterval.FifteenMinutes, "15 minutes"),
            new UpdateIntervalOption(UpdateInterval.ThirtyMinutes, "30 minutes"),
            new UpdateIntervalOption(UpdateInterval.OneHour, "1 hour"),
            new UpdateIntervalOption(UpdateInterval.ThreeHours, "3 hours"),
            new UpdateIntervalOption(UpdateInterval.SixHours, "6 hours"),
            new UpdateIntervalOption(UpdateInterval.TwelveHours, "12 hours"),
            new UpdateIntervalOption(UpdateInterval.TwentyFourHours, "24 hours")
        ];

        CheckNowCommand = new AsyncRelayCommand(() => CheckForUpdatesAsync(forceWrite: false), CanRunInteractiveCommand);
        ForceRefreshCommand = new AsyncRelayCommand(() => CheckForUpdatesAsync(forceWrite: true), CanRunInteractiveCommand);
        InstallGridCommand = new AsyncRelayCommand(InstallGridAsync, CanRunInstallCommand);
        DetectSteamCommand = new AsyncRelayCommand(RefreshAccountsAsync, CanRunInteractiveCommand);
        SaveSettingsCommand = new AsyncRelayCommand(SaveEditableSettingsAsync, CanSaveSettings);
        ClearHistoryCommand = new AsyncRelayCommand(ClearHistoryAsync, CanRunInteractiveCommand);
        RestoreBackupCommand = new AsyncRelayCommand(RestoreBackupAsync, CanRunInteractiveCommand);
        OpenLogsCommand = new RelayCommand(() => OpenFolder(_loggingService.GetLogsDirectory()));
        OpenBackupsCommand = new RelayCommand(() => OpenFolder(_backupService.GetBackupsDirectory()));
        OpenConfigCommand = new RelayCommand(() => OpenFolder(_appPaths.RootDirectory));
        NextOnboardingCommand = new RelayCommand(AdvanceOnboarding);
        FinishOnboardingCommand = new AsyncRelayCommand(FinishOnboardingAsync);
    }

    public ObservableCollection<AccountViewModel> Accounts { get; }
    public ObservableCollection<UpdateHistoryEntry> History { get; }
    public ObservableCollection<HeroGridLayoutPreview> GridPreviewLayouts { get; }
    public Array Presets { get; }
    public IReadOnlyList<UpdateIntervalOption> UpdateIntervalOptions { get; }

    public AsyncRelayCommand CheckNowCommand { get; }
    public AsyncRelayCommand ForceRefreshCommand { get; }
    public AsyncRelayCommand InstallGridCommand { get; }
    public AsyncRelayCommand DetectSteamCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public AsyncRelayCommand ClearHistoryCommand { get; }
    public AsyncRelayCommand RestoreBackupCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public RelayCommand OpenBackupsCommand { get; }
    public RelayCommand OpenConfigCommand { get; }
    public RelayCommand NextOnboardingCommand { get; }
    public AsyncRelayCommand FinishOnboardingCommand { get; }

    public AppPage SelectedPage
    {
        get => _selectedPage;
        set => SetProperty(ref _selectedPage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommandStates();
            }
        }
    }

    public bool IsInitializing
    {
        get => _isInitializing;
        private set
        {
            if (SetProperty(ref _isInitializing, value))
            {
                NotifyCommandStates();
                RaisePropertyChanged(nameof(IsInteractionEnabled));
            }
        }
    }

    public bool IsInteractionEnabled => !IsInitializing && !IsBusy;
    public bool ShouldStartMinimized => !OnboardingVisible && _settings.StartMinimized;

    public string StatusTitle
    {
        get => _statusTitle;
        set => SetProperty(ref _statusTitle, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string LastCheckedText
    {
        get => _lastCheckedText;
        set => SetProperty(ref _lastCheckedText, value);
    }

    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        set => SetProperty(ref _lastUpdatedText, value);
    }

    public string SelectedAccountText
    {
        get => _selectedAccountText;
        set => SetProperty(ref _selectedAccountText, value);
    }

    public string SelectedAccountPathText
    {
        get => _selectedAccountPathText;
        set => SetProperty(ref _selectedAccountPathText, value);
    }

    public string AvailableGridHashText
    {
        get => _availableGridHashText;
        set => SetProperty(ref _availableGridHashText, value);
    }

    public string InstalledGridHashText
    {
        get => _installedGridHashText;
        set => SetProperty(ref _installedGridHashText, value);
    }

    public string CachedGridHashText
    {
        get => _cachedGridHashText;
        set => SetProperty(ref _cachedGridHashText, value);
    }

    public string NextCheckText
    {
        get => _nextCheckText;
        set => SetProperty(ref _nextCheckText, value);
    }

    public bool OnboardingVisible
    {
        get => _onboardingVisible;
        set => SetProperty(ref _onboardingVisible, value);
    }

    public int OnboardingStep
    {
        get => _onboardingStep;
        set
        {
            if (SetProperty(ref _onboardingStep, value))
            {
                RaisePropertyChanged(nameof(OnboardingTitle));
                RaisePropertyChanged(nameof(OnboardingDescription));
                RaisePropertyChanged(nameof(OnboardingPrimaryLabel));
                RaisePropertyChanged(nameof(IsOnboardingWelcomeStep));
                RaisePropertyChanged(nameof(IsOnboardingSteamStep));
                RaisePropertyChanged(nameof(IsOnboardingAccountsStep));
                RaisePropertyChanged(nameof(IsOnboardingAutoUpdateStep));
                RaisePropertyChanged(nameof(IsOnboardingReadyStep));
                RaisePropertyChanged(nameof(ShowFinishOnboardingButton));
                RaisePropertyChanged(nameof(OnboardingSummaryText));
            }
        }
    }

    public string ProviderStatusText
    {
        get => _providerStatusText;
        set => SetProperty(ref _providerStatusText, value);
    }

    public string ProviderCardTitle
    {
        get => _providerCardTitle;
        set => SetProperty(ref _providerCardTitle, value);
    }

    public string SteamDetectionStatusText
    {
        get => _steamDetectionStatusText;
        set => SetProperty(ref _steamDetectionStatusText, value);
    }

    public string SteamDetectionPathText
    {
        get => _steamDetectionPathText;
        set => SetProperty(ref _steamDetectionPathText, value);
    }

    public string InitializationWarning
    {
        get => _initializationWarning;
        set
        {
            if (SetProperty(ref _initializationWarning, value))
            {
                RaisePropertyChanged(nameof(HasInitializationWarning));
            }
        }
    }

    public string CurrentSourceText
    {
        get => _currentSourceText;
        set => SetProperty(ref _currentSourceText, value);
    }

    public string HeroCountText
    {
        get => _heroCountText;
        set => SetProperty(ref _heroCountText, value);
    }

    public string RoleSummaryText
    {
        get => _roleSummaryText;
        set => SetProperty(ref _roleSummaryText, value);
    }

    public string GridCriteriaText
    {
        get => _gridCriteriaText;
        set => SetProperty(ref _gridCriteriaText, value);
    }

    public string TopFivePreviewText
    {
        get => _topFivePreviewText;
        set => SetProperty(ref _topFivePreviewText, value);
    }

    public string StatusChipText
    {
        get => _statusChipText;
        set => SetProperty(ref _statusChipText, value);
    }

    public string AutoUpdateChipText
    {
        get => _autoUpdateChipText;
        set => SetProperty(ref _autoUpdateChipText, value);
    }

    public string ProviderStatusChipText
    {
        get => _providerStatusChipText;
        set => SetProperty(ref _providerStatusChipText, value);
    }

    public HeroGridLayoutPreview? SelectedGridPreviewLayout
    {
        get => _selectedGridPreviewLayout;
        set
        {
            if (SetProperty(ref _selectedGridPreviewLayout, value))
            {
                RaisePropertyChanged(nameof(HasGridPreviewLayouts));
                RaisePropertyChanged(nameof(CurrentGridPreviewTitle));
                RaisePropertyChanged(nameof(CurrentGridPreviewSubtitle));
            }
        }
    }

    public AppSettings Settings => _settings;
    public AppSettings EditableSettings => _editableSettings;
    public bool HasLiveAvailableGrid => !string.IsNullOrWhiteSpace(_settings.LastRemoteHash) && ParseOrigin(_settings.LastGridOrigin) != GridOriginKind.Cached;
    public bool HasInstalledGrid => !string.IsNullOrWhiteSpace(_settings.LastInstalledHash);
    public bool CanOfferInstallAction => GetInstallActionState().CanOfferAction;
    public string InstallActionText => GetInstallActionState().ActionLabel;
    public string InstallActionSummary => HasInstalledGrid
        ? "Apply the latest verified MetaGrid hero grid to the selected Dota account."
        : "Install the first MetaGrid-managed hero grid into the selected Dota account.";
    public AccountViewModel? SelectedAccount => Accounts.FirstOrDefault(account => account.IsSelected);
    public bool HasInitializationWarning => !string.IsNullOrWhiteSpace(InitializationWarning);
    public bool IsOnboardingWelcomeStep => OnboardingStep == 1;
    public bool IsOnboardingSteamStep => OnboardingStep == 2;
    public bool IsOnboardingAccountsStep => OnboardingStep == 3;
    public bool IsOnboardingAutoUpdateStep => OnboardingStep == 4;
    public bool IsOnboardingReadyStep => OnboardingStep >= 5;
    public bool ShowFinishOnboardingButton => IsOnboardingReadyStep;
    public bool HasGridPreviewLayouts => GridPreviewLayouts.Count > 0;
    public bool IsSettingsDirty
    {
        get => _isSettingsDirty;
        private set
        {
            if (SetProperty(ref _isSettingsDirty, value))
            {
                RaisePropertyChanged(nameof(SettingsSaveStateText));
                SaveSettingsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string SettingsSaveStateText => IsSettingsDirty
        ? "Unsaved changes are ready to save."
        : "All displayed settings are currently saved.";
    public string HeroCountLabel => "Unique Heroes";
    public string CurrentGridPreviewTitle => SelectedGridPreviewLayout is null
        ? "Current Grid Preview"
        : $"{SelectedGridPreviewLayout.Name} Layout";
    public string CurrentGridPreviewSubtitle => SelectedGridPreviewLayout is null
        ? "Run Check for Updates to load the latest official Dota2ProTracker High Winrate layouts."
        : $"{SelectedGridPreviewLayout.Categories.Count} category blocks in official order.";

    public string OnboardingTitle => OnboardingStep switch
    {
        1 => "Welcome to MetaGrid",
        2 => "Steam Detection",
        3 => "Dota Accounts",
        4 => "Automatic Updates",
        _ => "You're ready"
    };

    public string OnboardingDescription => OnboardingStep switch
    {
        1 => "Keep a dedicated MetaGrid hero layout current using the official Dota2ProTracker High Winrate grid while preserving your own custom Dota layouts.",
        2 => SteamDetectionStatusText,
        3 => Accounts.Count == 0
            ? "No Dota accounts are selected yet. MetaGrid stays usable even if Steam or Dota is not detected immediately."
            : $"Detected {Accounts.Count} Dota-ready account(s). Select the account MetaGrid should update.",
        4 => $"Automatic updates are {(Settings.AutoUpdateEnabled ? "enabled" : "disabled")} with a {FormatInterval(Settings.UpdateInterval)} interval for checking the official Dota2ProTracker High Winrate grid.",
        _ => "MetaGrid can now open the main workspace, continue background checks, and keep your Dota 2 hero grid in sync."
    };

    public string OnboardingPrimaryLabel => IsOnboardingReadyStep ? "Open MetaGrid" : "Continue";

    public string OnboardingSummaryText =>
        $"Source: {CurrentSourceText}\nSelected account: {SelectedAccountText}\nNext check: {NextCheckText}";

    public async Task InitializeAsync()
    {
        IsInitializing = true;
        InitializationWarning = string.Empty;
        StatusTitle = "Initializing";
        StatusText = "Loading MetaGrid settings, accounts, and update history without blocking the visible window.";
        ProviderStatusText = "Initializing services";

        await TryLoadSettingsAsync();
        await TryRefreshAccountsAsync();
        await TryLoadHistoryAsync();
        ApplySettingsToUi();
        await TryStartAutomaticUpdatesAsync();

        IsInitializing = false;

        if (string.IsNullOrWhiteSpace(InitializationWarning))
        {
            RefreshOverviewState();
        }
        else
        {
            StatusTitle = "Attention required";
            StatusText = "MetaGrid opened successfully with a degraded subsystem state. Review the notice below and continue setup.";
        }
    }

    public async Task ShutdownAsync()
    {
        await StopSchedulerAsync();

        try
        {
            await PersistSettingsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await _loggingService.LogAsync(LogLevelKind.Warning, "Settings save failed during shutdown.", new { ex.Message }, CancellationToken.None);
        }

        _lifetimeCts.Cancel();
    }

    public void ReportNonFatalInitializationFailure(Exception exception)
    {
        InitializationWarning = exception.Message;
        StatusTitle = "Startup degraded";
        StatusText = "MetaGrid opened successfully, but one or more subsystems failed to initialize. Check logs for details.";
        ProviderStatusText = "Initialization degraded";
        IsInitializing = false;
    }

    private bool CanRunInteractiveCommand() => !IsInitializing && !IsBusy;
    private bool CanSaveSettings() => CanRunInteractiveCommand() && IsSettingsDirty;
    private bool CanRunInstallCommand() => CanRunInteractiveCommand() && CanOfferInstallAction;

    private async Task TryLoadSettingsAsync()
    {
        try
        {
            await _loggingService.LogAsync(LogLevelKind.Information, "Application startup");
            _settings = await _settingsService.LoadAsync(_lifetimeCts.Token);
            if (await TryHydrateProviderMetadataFromCacheAsync())
            {
                await _settingsService.SaveAsync(_settings, _lifetimeCts.Token);
            }
            RaisePropertyChanged(nameof(Settings));
            ResetEditableSettingsBaseline();
        }
        catch (Exception ex)
        {
            _settings = new AppSettings();
            InitializationWarning = "Settings could not be loaded. Defaults are being used.";
            await _loggingService.LogAsync(LogLevelKind.Warning, "Settings loading failed.", new { ex.Message }, _lifetimeCts.Token);
            ResetEditableSettingsBaseline();
        }
    }

    private async Task TryRefreshAccountsAsync()
    {
        try
        {
            await RefreshAccountsAsync();
        }
        catch (Exception ex)
        {
            InitializationWarning = "Steam or Dota detection failed. You can still open settings and configure MetaGrid manually.";
            SteamDetectionStatusText = "Steam detection failed.";
            SteamDetectionPathText = "MetaGrid could not validate a Steam installation during startup.";
            await _loggingService.LogAsync(LogLevelKind.Warning, "Steam/Dota detection failed during startup.", new { ex.Message }, _lifetimeCts.Token);
        }
    }

    private async Task TryLoadHistoryAsync()
    {
        try
        {
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            InitializationWarning = string.IsNullOrWhiteSpace(InitializationWarning)
                ? "Update history could not be loaded."
                : InitializationWarning;
            await _loggingService.LogAsync(LogLevelKind.Warning, "History loading failed during startup.", new { ex.Message }, _lifetimeCts.Token);
        }
    }

    private void ApplySettingsToUi()
    {
        LastCheckedText = _settings.LastCheckAt?.ToLocalTime().ToString("g") ?? "Never";
        LastUpdatedText = _settings.LastSuccessfulUpdateAt?.ToLocalTime().ToString("g") ?? "Never";
        AvailableGridHashText = ToShortHash(_settings.LastRemoteHash) ?? "Unknown";
        InstalledGridHashText = ToShortHash(_settings.LastInstalledHash) ?? "Not installed";
        CachedGridHashText = ToShortHash(_settings.LastCachedHash) ?? "Not cached";
        CurrentSourceText = BuildSourceStatusText();
        RoleSummaryText = string.IsNullOrWhiteSpace(_settings.LastRoleSummary) ? string.Join(", ", KnownRoles) : _settings.LastRoleSummary;
        GridCriteriaText = "Official Dota2ProTracker High Winrate grid";
        AutoUpdateChipText = _settings.AutoUpdateEnabled ? "Auto Update On" : "Auto Update Off";
        ProviderCardTitle = "Data Source";
        ProviderStatusText = ToProviderStatusText(_settings.LastProviderStatus, ParseOrigin(_settings.LastGridOrigin), _settings.LastSourceName);
        ProviderStatusChipText = ToProviderChipText(_settings.LastProviderStatus, ParseOrigin(_settings.LastGridOrigin), _settings.LastSourceName);
        if (_settings.LastHeroCount is int heroCount && heroCount > 0)
        {
            HeroCountText = heroCount.ToString();
        }
        else if (History.FirstOrDefault() is { ChangedHeroCount: > 0 } latest)
        {
            HeroCountText = latest.ChangedHeroCount.ToString();
        }
        else
        {
            HeroCountText = "Unknown";
        }

        _ = TryApplyCachedPreviewAsync();

        OnboardingVisible = !_settings.OnboardingCompleted;
        UpdateSelectedAccountUi();
        RaisePropertyChanged(nameof(OnboardingDescription));
        RaisePropertyChanged(nameof(OnboardingSummaryText));

        _ = _loggingService.LogAsync(LogLevelKind.Information, "MainViewModel initialization state", new
        {
            _settings.OnboardingCompleted,
            OnboardingVisible,
            SelectedAccounts = _settings.SelectedAccountIds.ToArray()
        }, _lifetimeCts.Token);
        RaiseInstallActionState();
    }

    private async Task<bool> TryHydrateProviderMetadataFromCacheAsync()
    {
        var cachedSnapshot = await _gridSnapshotCacheService.LoadAsync(_lifetimeCts.Token);
        if (cachedSnapshot is null)
        {
            return false;
        }

        if (!NeedsProviderMetadataHydration())
        {
            TryHydrateInstallSnapshotFromCache(cachedSnapshot);
            return false;
        }

        var changed = false;

        if (string.IsNullOrWhiteSpace(_settings.LastRemoteHash))
        {
            _settings.LastRemoteHash = cachedSnapshot.Hash;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(_settings.LastCachedHash))
        {
            _settings.LastCachedHash = cachedSnapshot.Hash;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(_settings.LastSourceName))
        {
            _settings.LastSourceName = cachedSnapshot.SourceName;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(_settings.LastSourceStrategy))
        {
            _settings.LastSourceStrategy = cachedSnapshot.SourceStrategy;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(_settings.LastProviderStatus))
        {
            _settings.LastProviderStatus = cachedSnapshot.ProviderStatus.ToString();
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(_settings.LastGridOrigin))
        {
            _settings.LastGridOrigin = cachedSnapshot.OriginKind.ToString();
            changed = true;
        }

        if (!_settings.LastGridCapturedAt.HasValue)
        {
            _settings.LastGridCapturedAt = cachedSnapshot.CapturedAt;
            changed = true;
        }

        if (!_settings.LastHeroCount.HasValue)
        {
            _settings.LastHeroCount = cachedSnapshot.Layouts
                .SelectMany(layout => layout.Categories)
                .SelectMany(category => category.HeroIds)
                .Distinct()
                .Count();
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(_settings.LastRoleSummary))
        {
            _settings.LastRoleSummary = BuildRoleSummary(cachedSnapshot);
            changed = true;
        }

        if (!changed)
        {
            TryHydrateInstallSnapshotFromCache(cachedSnapshot);
            return false;
        }

        await _loggingService.LogAsync(LogLevelKind.Information, "MetaGrid hydrated provider metadata from the last-known-good cache.", new
        {
            cachedSnapshot.SourceName,
            cachedSnapshot.SourceStrategy,
            cachedSnapshot.OriginKind,
            cachedSnapshot.Hash,
            cachedSnapshot.CapturedAt
        }, _lifetimeCts.Token);

        TryHydrateInstallSnapshotFromCache(cachedSnapshot);
        return true;
    }

    private void TryHydrateInstallSnapshotFromCache(HeroGridSnapshot cachedSnapshot)
    {
        var originKind = ParseOrigin(_settings.LastGridOrigin);
        if (originKind == GridOriginKind.Cached)
        {
            _availableInstallSnapshot = null;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_settings.LastRemoteHash)
            && string.Equals(_settings.LastRemoteHash, cachedSnapshot.Hash, StringComparison.OrdinalIgnoreCase))
        {
            _availableInstallSnapshot = cachedSnapshot;
        }
    }

    private async Task EnsureAvailableInstallSnapshotAsync()
    {
        if (_availableInstallSnapshot is not null
            && !string.IsNullOrWhiteSpace(_settings.LastRemoteHash)
            && string.Equals(_availableInstallSnapshot.Hash, _settings.LastRemoteHash, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var cachedSnapshot = await _gridSnapshotCacheService.LoadAsync(_lifetimeCts.Token);
        if (cachedSnapshot is null)
        {
            return;
        }

        TryHydrateInstallSnapshotFromCache(cachedSnapshot);
    }

    private bool NeedsProviderMetadataHydration()
        => string.IsNullOrWhiteSpace(_settings.LastRemoteHash)
           || string.IsNullOrWhiteSpace(_settings.LastCachedHash)
           || string.IsNullOrWhiteSpace(_settings.LastSourceName)
           || !_settings.LastHeroCount.HasValue
           || string.IsNullOrWhiteSpace(_settings.LastRoleSummary);

    private async Task TryStartAutomaticUpdatesAsync()
    {
        try
        {
            await StartAutomaticUpdatesAsync();
        }
        catch (Exception ex)
        {
            InitializationWarning = string.IsNullOrWhiteSpace(InitializationWarning)
                ? "Automatic update startup orchestration failed."
                : InitializationWarning;
            await _loggingService.LogAsync(LogLevelKind.Warning, "Automatic update startup orchestration failed during startup.", new { ex.Message }, _lifetimeCts.Token);
        }
    }

    private async Task StartAutomaticUpdatesAsync()
    {
        var startupDecision = AutomaticUpdateStartupPolicy.CreateDecision(_settings, _appClock.Now);
        await _loggingService.LogAsync(LogLevelKind.Information, "Automatic update startup decision computed.", new
        {
            startupDecision.ShouldRunStartupCycle,
            startupDecision.StartupDelay,
            startupDecision.NextScheduledCheckAt,
            startupDecision.Reason
        }, _lifetimeCts.Token);

        if (!startupDecision.ShouldRunStartupCycle)
        {
            await ScheduleAsync(startupDecision.NextScheduledCheckAt);
            return;
        }

        NextCheckText = "Startup check pending";
        AutoUpdateChipText = "Auto Update On";
        RaisePropertyChanged(nameof(OnboardingSummaryText));

        _startupAutomaticUpdateTask = Task.Run(() => RunStartupAutomaticUpdateAsync(startupDecision.StartupDelay ?? TimeSpan.Zero), CancellationToken.None);
    }

    private Task ScheduleAsync(DateTimeOffset? nextCheckAtOverride = null)
    {
        _scheduleCts?.Cancel();
        _scheduleCts?.Dispose();
        _scheduleCts = null;
        _schedulerTask = null;

        if (!_settings.AutoUpdateEnabled)
        {
            NextCheckText = "Automatic updates disabled";
            AutoUpdateChipText = "Auto Update Off";
            RaisePropertyChanged(nameof(OnboardingSummaryText));
            return Task.CompletedTask;
        }

        var interval = TimeSpan.FromMinutes((int)_settings.UpdateInterval);
        var nextRun = nextCheckAtOverride ?? AutomaticUpdateStartupPolicy.ComputeNextScheduledCheckAt(_appClock.Now, _settings.UpdateInterval);
        NextCheckText = nextRun.LocalDateTime.ToString("g");
        AutoUpdateChipText = "Auto Update On";
        RaisePropertyChanged(nameof(OnboardingSummaryText));

        _scheduleCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var schedulerToken = _scheduleCts.Token;
        _schedulerTask = Task.Run(() => RunSchedulerLoopAsync(interval, nextRun, schedulerToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task RunSchedulerLoopAsync(TimeSpan interval, DateTimeOffset nextCheckAt, CancellationToken schedulerToken)
    {
        try
        {
            while (!schedulerToken.IsCancellationRequested)
            {
                await _uiDispatcher.InvokeAsync(() =>
                {
                    NextCheckText = nextCheckAt.LocalDateTime.ToString("g");
                    RaisePropertyChanged(nameof(OnboardingSummaryText));
                }, schedulerToken);

                var delay = nextCheckAt - _appClock.Now;
                if (delay < TimeSpan.Zero)
                {
                    delay = TimeSpan.Zero;
                }

                await _appClock.DelayAsync(delay, schedulerToken);
                if (schedulerToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await RunAutomaticUpdateCycleAsync(rescheduleAfterCompletion: false);
                    nextCheckAt = AutomaticUpdateStartupPolicy.ComputeNextScheduledCheckAt(_appClock.Now, _settings.UpdateInterval);
                }
                catch (OperationCanceledException) when (schedulerToken.IsCancellationRequested || _lifetimeCts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await _loggingService.LogAsync(LogLevelKind.Error, "Automatic scheduler cycle threw an unhandled exception but the scheduler loop will continue.", new
                    {
                        ExceptionType = ex.GetType().FullName,
                        ex.Message,
                        StackTrace = ex.ToString()
                    }, CancellationToken.None);

                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        StatusTitle = "Automatic update failed";
                        StatusText = "MetaGrid hit a recoverable automatic-update error. The installed Dota grid was left unchanged and the next check is still scheduled.";
                        StatusChipText = "Auto update failed";
                        LastCheckedText = _appClock.Now.LocalDateTime.ToString("g");
                        RaisePropertyChanged(nameof(OnboardingSummaryText));
                    }, CancellationToken.None);

                    nextCheckAt = AutomaticUpdateStartupPolicy.ComputeNextScheduledCheckAt(_appClock.Now, _settings.UpdateInterval);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown or scheduler reconfiguration.
        }
        catch (Exception ex)
        {
            await _loggingService.LogAsync(LogLevelKind.Error, "Background update scheduler crashed.", new { ex.Message }, CancellationToken.None);
            await _uiDispatcher.InvokeAsync(() =>
            {
                ProviderStatusText = "Background updates paused";
                ProviderStatusChipText = "Scheduler paused";
            }, CancellationToken.None);
        }
    }

    private async Task StopSchedulerAsync()
    {
        if (_scheduleCts is null)
        {
            return;
        }

        _scheduleCts.Cancel();
        var schedulerTask = _schedulerTask;
        _scheduleCts.Dispose();
        _scheduleCts = null;
        _schedulerTask = null;

        if (schedulerTask is not null)
        {
            try
            {
                await schedulerTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }
    }

    private async Task RunStartupAutomaticUpdateAsync(TimeSpan startupDelay)
    {
        try
        {
            if (startupDelay > TimeSpan.Zero)
            {
                await _appClock.DelayAsync(startupDelay, _lifetimeCts.Token);
            }

            var autoUpdateEnabled = await _uiDispatcher.InvokeAsync(() => _settings.AutoUpdateEnabled, _lifetimeCts.Token);
            if (!autoUpdateEnabled)
            {
                await ScheduleAsync();
                return;
            }

            await _loggingService.LogAsync(LogLevelKind.Information, "Startup automatic update cycle is beginning.", new
            {
                DelaySeconds = startupDelay.TotalSeconds
            }, _lifetimeCts.Token);

            await RunAutomaticUpdateCycleAsync();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            await _loggingService.LogAsync(LogLevelKind.Error, "Startup automatic update wrapper failed unexpectedly.", new
            {
                ExceptionType = ex.GetType().FullName,
                ex.Message,
                StackTrace = ex.ToString()
            }, CancellationToken.None);
        }
        finally
        {
            if (!_lifetimeCts.IsCancellationRequested)
            {
                var autoUpdateEnabled = await _uiDispatcher.InvokeAsync(() => _settings.AutoUpdateEnabled, CancellationToken.None);
                if (autoUpdateEnabled && _scheduleCts is null)
                {
                    await ScheduleAsync();
                }
            }
        }
    }

    private async Task RefreshAccountsAsync()
    {
        var detectionMode = _settings.AutomaticallyDetectSteam ? "automatic" : "manual";
        SteamDetectionStatusText = _settings.AutomaticallyDetectSteam
            ? "Detecting Steam automatically..."
            : "Checking the configured Steam directory...";
        SteamDetectionPathText = string.IsNullOrWhiteSpace(_settings.SteamDirectoryOverride)
            ? "No manual Steam directory is configured."
            : _settings.SteamDirectoryOverride;

        await _loggingService.LogAsync(LogLevelKind.Information, "Steam detection started", new
        {
            Mode = detectionMode,
            _settings.SteamDirectoryOverride
        }, _lifetimeCts.Token);

        var accounts = await _steamAccountService.DetectAccountsAsync(_settings, _lifetimeCts.Token);
        Accounts.Clear();
        foreach (var account in accounts)
        {
            Accounts.Add(new AccountViewModel(account, HandleAccountSelected));
        }

        if (Accounts.Count > 0 && Accounts.All(x => !x.IsSelected))
        {
            Accounts[0].IsSelected = true;
        }

        var detectedRoot = Accounts.FirstOrDefault()?.Model.SteamRootPath;
        if (_settings.AutomaticallyDetectSteam && !string.IsNullOrWhiteSpace(detectedRoot))
        {
            _settings.SteamDirectoryOverride = detectedRoot;
            RaisePropertyChanged(nameof(Settings));
        }

        ApplySteamDetectionState();
        UpdateSelectedAccountUi();
        RaisePropertyChanged(nameof(OnboardingDescription));
        RaisePropertyChanged(nameof(OnboardingSummaryText));
    }

    private async Task LoadHistoryAsync()
    {
        var entries = await _historyService.LoadAsync(_lifetimeCts.Token);
        History.Clear();
        foreach (var entry in entries)
        {
            History.Add(entry);
        }

        HeroCountText = entries.FirstOrDefault(x => x.ChangedHeroCount > 0) is { } latest
            ? latest.ChangedHeroCount.ToString()
            : HeroCountText;
    }

    private async Task CheckForUpdatesAsync(bool forceWrite)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusTitle = "Checking for Updates";
            StatusText = "Checking Dota2ProTracker for the latest High Winrate grid.";
            StatusChipText = "Checking";
            ProviderStatusText = "Checking Dota2ProTracker";

            var result = await _updateService.CheckForUpdatesAsync(
                Accounts.Select(x => x.Model).ToList(),
                _settings,
                forceWrite,
                _lifetimeCts.Token);
            await ApplyUpdateResultAsync(result, rescheduleAfterUpdate: true);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown or app closure.
        }
        catch (Exception ex)
        {
            StatusTitle = "Update Check Failed";
            StatusText = "MetaGrid could not check Dota2ProTracker right now. Your installed grid was left unchanged.";
            ProviderStatusText = "Update check failed";
            ProviderStatusChipText = "Source error";
            StatusChipText = "Failed";
            InitializationWarning = string.IsNullOrWhiteSpace(InitializationWarning)
                ? "A manual or automatic update check failed."
                : InitializationWarning;
            await _loggingService.LogAsync(LogLevelKind.Error, "Update check failed unexpectedly.", new { ex.Message }, CancellationToken.None);
            _notificationService.ShowError("MetaGrid update failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunAutomaticUpdateCycleAsync(bool rescheduleAfterCompletion = true)
    {
        string stage = "CaptureInputs";
        AutomaticCycleInputs? inputs = null;
        try
        {
            inputs = await _uiDispatcher.InvokeAsync(() => CaptureAutomaticCycleInputs(), _lifetimeCts.Token);
            if (inputs.IsBusy)
            {
                await _loggingService.LogAsync(LogLevelKind.Information, "Automatic update cycle skipped because MetaGrid is already busy with a manual operation.", null, CancellationToken.None);
                return;
            }

            await _uiDispatcher.InvokeAsync(() =>
            {
                IsBusy = true;
                StatusTitle = "Checking for Updates";
                StatusText = "Checking Dota2ProTracker for the latest High Winrate grid.";
                StatusChipText = "Checking";
                ProviderStatusText = "Checking D2PT";
            }, _lifetimeCts.Token);

            stage = "RunAutomaticUpdateCycle";
            var result = await _updateService.RunAutomaticUpdateCycleAsync(
                inputs.Accounts,
                inputs.Settings,
                _lifetimeCts.Token);

            stage = "ApplyResultOnUi";
            await _uiDispatcher.InvokeAsync(() => ApplyUpdateResultAsync(result, rescheduleAfterUpdate: rescheduleAfterCompletion), _lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            // Expected during shutdown or scheduler reconfiguration.
        }
        catch (Exception ex)
        {
            await _loggingService.LogAsync(LogLevelKind.Error, "Automatic update cycle failed unexpectedly.", new
            {
                Stage = stage,
                Trigger = UpdateTriggerKind.Automatic.ToString(),
                SelectedAccountId = inputs?.SelectedAccountId,
                ExceptionType = ex.GetType().FullName,
                ex.Message,
                StackTrace = ex.ToString()
            }, CancellationToken.None);

            if (!_lifetimeCts.IsCancellationRequested)
            {
                await _uiDispatcher.InvokeAsync(() =>
                {
                    StatusTitle = "Automatic Update Failed";
                    StatusText = "MetaGrid could not complete this update attempt. Your installed grid was left unchanged, and automatic updates will try again later.";
                    StatusChipText = "Auto update failed";
                    LastCheckedText = _appClock.Now.LocalDateTime.ToString("g");
                    RaisePropertyChanged(nameof(OnboardingSummaryText));
                }, CancellationToken.None);
            }
        }
        finally
        {
            if (!_lifetimeCts.IsCancellationRequested)
            {
                await _uiDispatcher.InvokeAsync(() =>
                {
                    IsBusy = false;
                }, CancellationToken.None);
            }
        }
    }

    private async Task SaveEditableSettingsAsync()
    {
        var candidateSettings = _settings.CreateCopy();
        candidateSettings.CopyFrom(_editableSettings);
        candidateSettings.BackupRetentionCount = Math.Clamp(candidateSettings.BackupRetentionCount, 1, 50);
        candidateSettings.SelectedAccountIds = Accounts.Where(x => x.IsSelected).Select(x => x.AccountId).ToList();
        candidateSettings.PreferredAccountId = candidateSettings.SelectedAccountIds.FirstOrDefault();
        candidateSettings.LastRoleSummary = RoleSummaryText;

        var steamSettingsChanged =
            _settings.AutomaticallyDetectSteam != candidateSettings.AutomaticallyDetectSteam ||
            !string.Equals(_settings.SteamDirectoryOverride, candidateSettings.SteamDirectoryOverride, StringComparison.Ordinal);

        try
        {
            await _settingsService.SaveAsync(candidateSettings, _lifetimeCts.Token);
            _startupService.ApplyLaunchAtStartup(candidateSettings.LaunchWithWindows);
            _settings = candidateSettings;
            RaisePropertyChanged(nameof(Settings));
            ResetEditableSettingsBaseline();
            AutoUpdateChipText = _settings.AutoUpdateEnabled ? "Auto Update On" : "Auto Update Off";
            await ScheduleAsync();
            if (steamSettingsChanged)
            {
                try
                {
                    await RefreshAccountsAsync();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await _loggingService.LogAsync(LogLevelKind.Warning, "Steam refresh failed after settings save.", new { ex.Message }, CancellationToken.None);
                }
            }

            RaiseInstallActionState();
            RaisePropertyChanged(nameof(OnboardingDescription));
            RaisePropertyChanged(nameof(OnboardingSummaryText));
            _notificationService.ShowSuccess("Settings saved", "MetaGrid saved your updated settings.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _loggingService.LogAsync(LogLevelKind.Error, "Settings save failed.", new { ex.Message }, CancellationToken.None);
            StatusTitle = "Settings save failed";
            StatusText = "MetaGrid could not save your settings. Review the value you changed and try again.";
            StatusChipText = "Save failed";
            _notificationService.ShowError("Settings save failed", ex.Message);
        }
    }

    private async Task ClearHistoryAsync()
    {
        await _historyService.ClearAsync(_lifetimeCts.Token);
        History.Clear();
        HeroCountText = "Unknown";
        _settings.LastHeroCount = null;
    }

    private async Task RestoreBackupAsync()
    {
        var selected = Accounts.FirstOrDefault(x => x.IsSelected);
        if (selected is null)
        {
            StatusTitle = "Choose a Dota account";
            StatusText = "Select an account before restoring a backup.";
            return;
        }

        if (await _backupService.RestoreLatestAsync(selected.Model.HeroGridConfigPath, _lifetimeCts.Token))
        {
            StatusTitle = "Backup restored";
            StatusText = "The latest hero grid backup was restored for the selected account.";
            StatusChipText = "Backup restored";
            _notificationService.ShowInfo("Backup restored", "The latest hero grid backup was restored.");
            await LoadHistoryAsync();
        }
    }

    public Task<IReadOnlyList<BackupEntry>> GetSelectedAccountBackupsAsync()
    {
        var selected = Accounts.FirstOrDefault(x => x.IsSelected);
        return _backupService.GetBackupsAsync(_lifetimeCts.Token, selected?.AccountId);
    }

    public async Task<bool> RestoreBackupAsync(BackupEntry backup)
    {
        var selected = Accounts.FirstOrDefault(x => x.IsSelected);
        if (selected is null)
        {
            StatusTitle = "Choose a Dota account";
            StatusText = "Select an account before restoring a backup.";
            StatusChipText = "Action required";
            return false;
        }

        var targetPath = selected.Model.HeroGridConfigPath;
        var currentContents = File.Exists(targetPath) ? await File.ReadAllTextAsync(targetPath, _lifetimeCts.Token) : null;
        var previousHash = await _dotaGridService.ReadInstalledMetaGridHashAsync(targetPath, _lifetimeCts.Token);

        try
        {
            var backupJson = await File.ReadAllTextAsync(backup.FilePath, _lifetimeCts.Token);
            if (!_dotaGridService.TryValidate(backupJson, out var backupValidationError))
            {
                throw new InvalidOperationException($"Selected backup is not valid JSON: {backupValidationError}");
            }

            if (!string.IsNullOrWhiteSpace(backup.AccountId) && !string.Equals(backup.AccountId, selected.AccountId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("MetaGrid refused to restore a backup that belongs to a different Steam account.");
            }

            if (!string.IsNullOrWhiteSpace(backup.OriginalPath) && !string.Equals(Path.GetFullPath(backup.OriginalPath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("MetaGrid refused to restore a backup to a different Dota target path than the one it was created from.");
            }

            _ = await _backupService.BackupAsync(targetPath, _settings.BackupRetentionCount, new BackupMetadata
            {
                AccountId = selected.AccountId,
                AccountDisplayName = selected.DisplayName,
                OriginalPath = targetPath,
                PreWriteHash = previousHash,
                OperationId = Guid.NewGuid().ToString("N")
            }, _lifetimeCts.Token);
            var restored = await _backupService.RestoreAsync(backup, targetPath, _lifetimeCts.Token);
            if (!restored)
            {
                return false;
            }

            var restoredJson = await File.ReadAllTextAsync(targetPath, _lifetimeCts.Token);
            if (!_dotaGridService.TryValidate(restoredJson, out var restoredValidationError))
            {
                throw new InvalidOperationException($"Restored file failed validation: {restoredValidationError}");
            }

            await EnsureAvailableInstallSnapshotAsync();
            var restoredHash = await _dotaGridService.ReadInstalledMetaGridHashAsync(targetPath, _lifetimeCts.Token);
            selected.Model.CurrentMetaGridHash = restoredHash;
            _settings.LastInstalledHash = restoredHash;
            InstalledGridHashText = ToShortHash(restoredHash) ?? "Not installed";
            await _historyService.AppendAsync(
            [
                new UpdateHistoryEntry
                {
                    Timestamp = DateTimeOffset.Now,
                    Status = UpdateStatus.BackupRestored,
                    GridHash = restoredHash ?? string.Empty,
                    Source = "MetaGrid Backup",
                    SourceStrategy = "ManualRestore",
                    AccountId = selected.AccountId,
                    AccountDisplayName = selected.DisplayName,
                    Operation = "Restore backup",
                    Message = $"Restored selected backup from {backup.CreatedAt.LocalDateTime:g}.",
                    PreviousHash = previousHash,
                    NewHash = restoredHash,
                    AvailableHash = _settings.LastRemoteHash,
                    InstalledHash = restoredHash,
                    BackupFilePath = backup.FilePath,
                    BackupOperationId = backup.OperationId,
                    BackupHash = backup.BackupHash,
                    TargetPath = targetPath,
                    ChangedHeroCount = 0
                }
            ], _lifetimeCts.Token);
            await LoadHistoryAsync();
            await PersistSettingsAsync(_lifetimeCts.Token);
            RefreshOverviewState();
            RaiseInstallActionState();
            _notificationService.ShowInfo("Backup restored", "The selected hero-grid backup was restored.");
            return true;
        }
        catch (Exception ex)
        {
            if (currentContents is not null)
            {
                await File.WriteAllTextAsync(targetPath, currentContents, _lifetimeCts.Token);
            }

            StatusTitle = "Restore failed";
            StatusText = "MetaGrid could not restore the selected backup. The current configuration was preserved.";
            StatusChipText = "Restore failed";
            await _loggingService.LogAsync(LogLevelKind.Error, "Backup restore failed.", new
            {
                backup.FilePath,
                targetPath,
                ex.Message
            }, _lifetimeCts.Token);
            _notificationService.ShowError("Backup restore failed", "MetaGrid could not restore that backup safely.");
            return false;
        }
    }

    private void AdvanceOnboarding()
    {
        if (OnboardingStep < 5)
        {
            OnboardingStep++;
            if (OnboardingStep == 2)
            {
                _ = EnsureSteamDetectionForOnboardingAsync();
            }
            return;
        }

        FinishOnboardingCommand.Execute(null);
    }

    private async Task FinishOnboardingAsync()
    {
        _settings.OnboardingCompleted = true;
        OnboardingVisible = false;
        OnboardingStep = 5;
        await PersistSettingsAsync(_lifetimeCts.Token);
        ResetEditableSettingsBaseline();
        RefreshOverviewState();
    }

    private void NotifyCommandStates()
    {
        CheckNowCommand.NotifyCanExecuteChanged();
        ForceRefreshCommand.NotifyCanExecuteChanged();
        InstallGridCommand.NotifyCanExecuteChanged();
        DetectSteamCommand.NotifyCanExecuteChanged();
        SaveSettingsCommand.NotifyCanExecuteChanged();
        ClearHistoryCommand.NotifyCanExecuteChanged();
        RestoreBackupCommand.NotifyCanExecuteChanged();
    }

    private void HandleAccountSelected(AccountViewModel selectedAccount)
    {
        foreach (var account in Accounts)
        {
            if (!ReferenceEquals(account, selectedAccount) && account.IsSelected)
            {
                account.IsSelected = false;
            }
        }

        UpdateSelectedAccountUi();
        RaisePropertyChanged(nameof(OnboardingDescription));
        RaisePropertyChanged(nameof(OnboardingSummaryText));
    }

    private void UpdateSelectedAccountUi()
    {
        var selected = Accounts.FirstOrDefault(x => x.IsSelected);
        SelectedAccountText = selected?.DisplayName ?? "No account selected";
        SelectedAccountPathText = selected?.ConfigPath ?? "Choose a Dota 2 account to see its configuration path.";
        InstalledGridHashText = selected is null
            ? "Not installed"
            : ToShortHash(selected.Model.CurrentMetaGridHash) ?? "Not installed";
        RaiseInstallActionState();
    }

    private void ApplySteamDetectionState()
    {
        var detectedRoot = Accounts.FirstOrDefault()?.Model.SteamRootPath;
        if (!string.IsNullOrWhiteSpace(detectedRoot))
        {
            SteamDetectionStatusText = _settings.AutomaticallyDetectSteam
                ? "Steam detected automatically."
                : "Steam detected from the configured directory.";
            SteamDetectionPathText = detectedRoot;
            return;
        }

        if (!_settings.AutomaticallyDetectSteam && !string.IsNullOrWhiteSpace(_settings.SteamDirectoryOverride))
        {
            SteamDetectionStatusText = "The configured Steam directory did not contain Dota 2 userdata yet.";
            SteamDetectionPathText = _settings.SteamDirectoryOverride;
            return;
        }

        SteamDetectionStatusText = "Steam was not detected automatically yet.";
        SteamDetectionPathText = "Check that Steam has been launched on this PC and that a Dota 2 account has userdata for AppID 570.";
    }

    private async Task EnsureSteamDetectionForOnboardingAsync()
    {
        if (_steamAutoDetectionStarted || !_settings.AutomaticallyDetectSteam)
        {
            return;
        }

        _steamAutoDetectionStarted = true;
        try
        {
            await RefreshAccountsAsync();
        }
        catch (Exception ex)
        {
            await _loggingService.LogAsync(LogLevelKind.Warning, "Onboarding Steam auto-detection failed.", new { ex.Message }, _lifetimeCts.Token);
        }
    }

    private string BuildSourceStatusText()
    {
        if (string.IsNullOrWhiteSpace(_settings.LastSourceName))
        {
            return "Official Dota2ProTracker High Winrate";
        }

        var parts = new List<string> { _settings.LastSourceName };
        if (!string.IsNullOrWhiteSpace(_settings.LastSourceStrategy))
        {
            parts.Add(HumanizeSourceStrategy(_settings.LastSourceStrategy));
        }

        if (ParseOrigin(_settings.LastGridOrigin) == GridOriginKind.Cached)
        {
            parts.Add("Using cached grid");
        }

        if (_settings.LastGridCapturedAt is { } capturedAt)
        {
            parts.Add($"Last updated {capturedAt.LocalDateTime:g}");
        }

        return string.Join(" - ", parts);
    }

    private static string HumanizeSourceStrategy(string? sourceStrategy)
        => sourceStrategy switch
        {
            "OfficialDownloadPayload" => "Official download payload",
            "CachedOfficialDownloadPayload" => "Cached official payload",
            null or "" => string.Empty,
            _ => sourceStrategy
        };

    private static string FormatInterval(UpdateInterval interval)
        => interval switch
        {
            UpdateInterval.FifteenMinutes => "15 minutes",
            UpdateInterval.ThirtyMinutes => "30 minutes",
            UpdateInterval.OneHour => "1 hour",
            UpdateInterval.ThreeHours => "3 hours",
            UpdateInterval.SixHours => "6 hours",
            UpdateInterval.TwelveHours => "12 hours",
            UpdateInterval.TwentyFourHours => "24 hours",
            _ => $"{(int)interval} minutes"
        };

    private static string? ToShortHash(string? hash)
        => string.IsNullOrWhiteSpace(hash) ? null : hash[..Math.Min(12, hash.Length)];

    private void ResetEditableSettingsBaseline()
    {
        _editableSettings.PropertyChanged -= HandleEditableSettingsChanged;
        _editableSettings = _settings.CreateCopy();
        _editableSettings.PropertyChanged += HandleEditableSettingsChanged;
        _settingsBaselineSnapshot = CaptureEditableSettingsSnapshot(_editableSettings);
        RaisePropertyChanged(nameof(EditableSettings));
        IsSettingsDirty = false;
    }

    private void HandleEditableSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(AppSettings.SelectedAccountIds), StringComparison.Ordinal))
        {
            return;
        }

        IsSettingsDirty = !string.Equals(
            _settingsBaselineSnapshot,
            CaptureEditableSettingsSnapshot(_editableSettings),
            StringComparison.Ordinal);
    }

    private static string CaptureEditableSettingsSnapshot(AppSettings settings)
        => string.Join("||",
            settings.LaunchWithWindows,
            settings.StartMinimized,
            settings.MinimizeToTray,
            settings.CloseToTray,
            settings.AutoUpdateEnabled,
            settings.UpdateInterval,
            Math.Clamp(settings.BackupRetentionCount, 1, 50),
            settings.AutomaticallyDetectSteam,
            settings.SteamDirectoryOverride?.Trim() ?? string.Empty);

    private Task PersistSettingsAsync(CancellationToken cancellationToken)
    {
        _settings.SelectedAccountIds = Accounts.Where(x => x.IsSelected).Select(x => x.AccountId).ToList();
        _settings.PreferredAccountId = _settings.SelectedAccountIds.FirstOrDefault();
        _settings.LastRoleSummary = RoleSummaryText;
        return _settingsService.SaveAsync(_settings, cancellationToken);
    }

    private static string ToProviderChipText(string? providerStatus, GridOriginKind? originKind, string? sourceName)
        => originKind switch
        {
            GridOriginKind.Cached => "Using cached grid",
            GridOriginKind.NativeD2pt => providerStatus switch
            {
                nameof(ProviderStatus.Online) => "D2PT Online",
                nameof(ProviderStatus.CloudflareBlocked) => "D2PT blocked",
                nameof(ProviderStatus.RateLimited) => "D2PT limited",
                nameof(ProviderStatus.NetworkUnavailable) => "Network issue",
                nameof(ProviderStatus.UnexpectedResponse) => "Unexpected response",
                nameof(ProviderStatus.ParsingFailed) => "Payload invalid",
                nameof(ProviderStatus.Unavailable) => "Source unavailable",
                nameof(ProviderStatus.Cached) => "Using cached grid",
                _ => "Source not checked"
            },
            _ => providerStatus switch
            {
                nameof(ProviderStatus.Online) => string.Equals(sourceName, "Dota2ProTracker", StringComparison.OrdinalIgnoreCase)
                    ? "D2PT Online"
                    : "Provider online",
                nameof(ProviderStatus.Unavailable) => "Source unavailable",
                nameof(ProviderStatus.CloudflareBlocked) => "D2PT blocked",
                nameof(ProviderStatus.RateLimited) => "D2PT limited",
                nameof(ProviderStatus.NetworkUnavailable) => "Network issue",
                nameof(ProviderStatus.UnexpectedResponse) => "Unexpected response",
                nameof(ProviderStatus.ParsingFailed) => "Payload invalid",
                nameof(ProviderStatus.Cached) => "Using cached grid",
                _ => "Source not checked"
            }
        };

    private static string ToProviderStatusText(ProviderStatus providerStatus, GridOriginKind? originKind, string? sourceName)
        => originKind switch
        {
            GridOriginKind.Cached => "Cached data",
            GridOriginKind.NativeD2pt => providerStatus switch
            {
                ProviderStatus.Online => "D2PT Online",
                ProviderStatus.CloudflareBlocked => "D2PT blocked by Cloudflare",
                ProviderStatus.RateLimited => "D2PT rate limited",
                ProviderStatus.NetworkUnavailable => "D2PT unavailable",
                ProviderStatus.UnexpectedResponse => "D2PT returned an unexpected payload",
                ProviderStatus.ParsingFailed => "D2PT payload invalid",
                ProviderStatus.Unavailable => "D2PT unavailable",
                ProviderStatus.Cached => "Cached data",
                _ => "Source unavailable"
            },
            _ => providerStatus switch
            {
                ProviderStatus.Online => string.Equals(sourceName, "Dota2ProTracker", StringComparison.OrdinalIgnoreCase)
                    ? "D2PT Online"
                    : "Provider online",
                ProviderStatus.Unavailable => "Source unavailable",
                ProviderStatus.CloudflareBlocked => "D2PT blocked by Cloudflare",
                ProviderStatus.RateLimited => "Provider rate limited",
                ProviderStatus.NetworkUnavailable => "Network unavailable",
                ProviderStatus.UnexpectedResponse => "Unexpected provider response",
                ProviderStatus.ParsingFailed => "Provider payload invalid",
                ProviderStatus.Cached => "Cached data",
                _ => "Source unavailable"
            }
        };

    private static string ToProviderStatusText(string? providerStatus, GridOriginKind? originKind, string? sourceName)
        => originKind switch
        {
            GridOriginKind.Cached => "Cached data",
            GridOriginKind.NativeD2pt => providerStatus switch
            {
                nameof(ProviderStatus.Online) => "D2PT Online",
                nameof(ProviderStatus.CloudflareBlocked) => "D2PT blocked by Cloudflare",
                nameof(ProviderStatus.RateLimited) => "D2PT rate limited",
                nameof(ProviderStatus.NetworkUnavailable) => "D2PT unavailable",
                nameof(ProviderStatus.UnexpectedResponse) => "D2PT returned an unexpected payload",
                nameof(ProviderStatus.ParsingFailed) => "D2PT payload invalid",
                nameof(ProviderStatus.Unavailable) => "D2PT unavailable",
                nameof(ProviderStatus.Cached) => "Cached data",
                _ => "Source unavailable"
            },
            _ => providerStatus switch
            {
                nameof(ProviderStatus.Online) => string.Equals(sourceName, "Dota2ProTracker", StringComparison.OrdinalIgnoreCase)
                    ? "D2PT Online"
                    : "Provider online",
                nameof(ProviderStatus.Unavailable) => "Source unavailable",
                nameof(ProviderStatus.CloudflareBlocked) => "D2PT blocked by Cloudflare",
                nameof(ProviderStatus.RateLimited) => "Provider rate limited",
                nameof(ProviderStatus.NetworkUnavailable) => "Network unavailable",
                nameof(ProviderStatus.UnexpectedResponse) => "Unexpected provider response",
                nameof(ProviderStatus.ParsingFailed) => "Provider payload invalid",
                nameof(ProviderStatus.Cached) => "Cached data",
                _ => "Source unavailable"
            }
        };

    private static string ToProviderStatusText(string? providerStatus)
        => providerStatus switch
        {
            nameof(ProviderStatus.Online) => "D2PT Online",
            nameof(ProviderStatus.Unavailable) => "Source unavailable",
            nameof(ProviderStatus.CloudflareBlocked) => "D2PT blocked by Cloudflare",
            nameof(ProviderStatus.RateLimited) => "Provider rate limited",
            nameof(ProviderStatus.NetworkUnavailable) => "Network unavailable",
            nameof(ProviderStatus.UnexpectedResponse) => "Unexpected provider response",
            nameof(ProviderStatus.ParsingFailed) => "Provider payload invalid",
            nameof(ProviderStatus.Cached) => "Cached data",
            _ => "Source unavailable"
        };

    private static GridOriginKind? ParseOrigin(string? value)
        => Enum.TryParse<GridOriginKind>(value, out var origin) ? origin : null;

    private static string BuildRoleSummary(HeroGridSnapshot? snapshot)
        => snapshot switch
        {
            null => string.Join(", ", KnownRoles),
            { RoleResults.Count: > 0 } => string.Join(", ", snapshot.RoleResults.OrderBy(result => result.Position).Select(result => result.RoleName)),
            _ => string.Join(", ", snapshot.Layouts.Select(layout => layout.Name).Distinct(StringComparer.OrdinalIgnoreCase))
        };

    private static string BuildTopFivePreview(HeroGridSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return "Run Check for Updates to load the latest official Dota2ProTracker High Winrate layouts.";
        }

        if (snapshot.RoleResults.Count == 0)
        {
            return string.Join(
                Environment.NewLine + Environment.NewLine,
                snapshot.Layouts.Select(layout =>
                    $"{layout.Name}{Environment.NewLine}" +
                    string.Join(
                        Environment.NewLine,
                        layout.Categories.Select(category =>
                            $"- {category.Name}: {category.HeroIds.Count} heroes"))));
        }

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            snapshot.RoleResults
                .OrderBy(result => result.Position)
                .Select(result =>
                    $"{result.RoleName}{Environment.NewLine}" +
                    string.Join(
                        Environment.NewLine,
                        result.Heroes.Select((hero, index) =>
                            $"{index + 1}. {hero.HeroName}  {hero.WinRate:P1}  {hero.Matches:N0} matches"))));
    }

    private async Task UpdateGridPreviewAsync(HeroGridSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            GridPreviewLayouts.Clear();
            SelectedGridPreviewLayout = null;
            RaisePropertyChanged(nameof(HasGridPreviewLayouts));
            RaisePropertyChanged(nameof(CurrentGridPreviewTitle));
            RaisePropertyChanged(nameof(CurrentGridPreviewSubtitle));
            return;
        }

        var existingSelection = SelectedGridPreviewLayout?.Name;
        var heroNamesById = await EnsureHeroNamesByIdAsync();
        var layouts = snapshot.Layouts
            .Select(layout => new HeroGridLayoutPreview(
                layout.Name,
                layout.Categories
                    .Select((category, index) => new HeroGridCategoryPreview(
                        $"{layout.Name}:{index}",
                        index,
                        category.Name,
                        category.HeroIds.Count,
                        BuildHeroPreviewText(category.HeroIds, heroNamesById)))
                    .ToArray()))
            .ToArray();

        GridPreviewLayouts.Clear();
        foreach (var layout in layouts)
        {
            GridPreviewLayouts.Add(layout);
        }

        SelectedGridPreviewLayout = GridPreviewLayouts.FirstOrDefault(layout =>
                                        string.Equals(layout.Name, existingSelection, StringComparison.OrdinalIgnoreCase))
                                    ?? GridPreviewLayouts.FirstOrDefault();
        RaisePropertyChanged(nameof(HasGridPreviewLayouts));
        RaisePropertyChanged(nameof(CurrentGridPreviewTitle));
        RaisePropertyChanged(nameof(CurrentGridPreviewSubtitle));
    }

    private async Task<IReadOnlyDictionary<int, string>> EnsureHeroNamesByIdAsync()
    {
        if (_heroNamesById is not null)
        {
            return _heroNamesById;
        }

        try
        {
            var heroesByName = await _heroCatalogService.LoadByNameAsync(_lifetimeCts.Token);
            _heroNamesById = heroesByName.Values
                .GroupBy(hero => hero.Id)
                .ToDictionary(group => group.Key, group => group.First().LocalizedName);
        }
        catch (Exception ex)
        {
            await _loggingService.LogAsync(
                LogLevelKind.Warning,
                "Hero catalog lookup failed for grid preview. Falling back to hero IDs only.",
                new { ex.Message },
                _lifetimeCts.Token);
            _heroNamesById = new Dictionary<int, string>();
        }

        return _heroNamesById;
    }

    private static string BuildHeroPreviewText(IReadOnlyList<int> heroIds, IReadOnlyDictionary<int, string> heroNamesById)
    {
        if (heroIds.Count == 0)
        {
            return "No heroes in this category.";
        }

        var names = heroIds
            .Select(heroId => heroNamesById.TryGetValue(heroId, out var heroName) ? heroName : $"Hero {heroId}")
            .Take(8)
            .ToList();

        if (heroIds.Count > names.Count)
        {
            names.Add($"+{heroIds.Count - names.Count} more");
        }

        return string.Join(", ", names);
    }

    private async Task TryApplyCachedPreviewAsync()
    {
        var cachedSnapshot = await _gridSnapshotCacheService.LoadAsync(_lifetimeCts.Token);
        if (cachedSnapshot is null)
        {
            return;
        }

        RoleSummaryText = BuildRoleSummary(cachedSnapshot);
        TopFivePreviewText = BuildTopFivePreview(cachedSnapshot);
        await UpdateGridPreviewAsync(cachedSnapshot);
    }

    private static void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private async Task ApplyUpdateResultAsync(UpdateRunResult result, bool rescheduleAfterUpdate)
    {
        if (result.Trigger != UpdateTriggerKind.ManualInstall)
        {
            _settings.LastCheckAt = _appClock.Now;
            LastCheckedText = _settings.LastCheckAt?.ToLocalTime().ToString("g") ?? "Never";
        }

        if (AutomaticUpdateStartupPolicy.IsSuccessfulLiveProviderCheck(result))
        {
            _settings.LastSuccessfulLiveProviderCheckAt = _appClock.Now;
        }

        if (!string.IsNullOrWhiteSpace(result.GridHash))
        {
            _settings.LastRemoteHash = result.GridHash;
            _settings.LastCachedHash = result.GridHash;
            AvailableGridHashText = ToShortHash(result.GridHash) ?? AvailableGridHashText;
            CachedGridHashText = ToShortHash(result.GridHash) ?? CachedGridHashText;
        }

        _settings.LastProviderStatus = result.ProviderStatus.ToString();
        _settings.LastSourceName = result.SourceName ?? _settings.LastSourceName;
        _settings.LastSourceStrategy = result.SourceStrategy ?? _settings.LastSourceStrategy;
        _settings.LastSourceDetails = result.ProviderMessage ?? _settings.LastSourceDetails;
        _settings.LastGridOrigin = result.OriginKind?.ToString() ?? _settings.LastGridOrigin;
        _settings.LastGridCapturedAt = result.RetrievedAt ?? _settings.LastGridCapturedAt;
        _availableInstallSnapshot = result.ConfirmedInstallSnapshot?.Snapshot ?? _availableInstallSnapshot;

        if (result.ChangedHeroCount > 0)
        {
            _settings.LastHeroCount = result.ChangedHeroCount;
            HeroCountText = result.ChangedHeroCount.ToString();
        }

        var previewSnapshot = result.ConfirmedInstallSnapshot?.Snapshot ?? _availableInstallSnapshot;
        RoleSummaryText = BuildRoleSummary(previewSnapshot);
        _settings.LastRoleSummary = RoleSummaryText;
        TopFivePreviewText = BuildTopFivePreview(previewSnapshot);
        await UpdateGridPreviewAsync(previewSnapshot);

        if (result.Status == UpdateStatus.Updated && !string.IsNullOrWhiteSpace(result.InstalledHash))
        {
            _settings.LastInstalledHash = result.InstalledHash;
            _settings.LastSuccessfulUpdateAt = _appClock.Now;
            InstalledGridHashText = ToShortHash(result.InstalledHash) ?? "Not installed";
            LastUpdatedText = _settings.LastSuccessfulUpdateAt?.ToLocalTime().ToString("g") ?? LastUpdatedText;
        }
        else if (!string.IsNullOrWhiteSpace(result.InstalledHash))
        {
            _settings.LastInstalledHash = result.InstalledHash;
            InstalledGridHashText = ToShortHash(result.InstalledHash) ?? "Not installed";
        }
        else if (result.Status == UpdateStatus.BackupRestored)
        {
            InstalledGridHashText = ToShortHash(_settings.LastInstalledHash) ?? "Not installed";
        }

        CurrentSourceText = BuildSourceStatusText();
        var providerStatusText = ToProviderStatusText(result.ProviderStatus, result.OriginKind, result.SourceName);
        var providerStatusChipText = ToProviderChipText(result.ProviderStatus.ToString(), result.OriginKind, result.SourceName);
        var statusPresentation = HeroGridStatusPresenter.CreateFromResult(result);

        await PersistSettingsAsync(_lifetimeCts.Token);
        await LoadHistoryAsync();

        if (rescheduleAfterUpdate)
        {
            await ScheduleAsync();
        }

        UpdateSelectedAccountUi();
        RefreshOverviewState();
        ProviderStatusText = providerStatusText;
        ProviderStatusChipText = providerStatusChipText;
        StatusTitle = statusPresentation.Title;
        StatusText = statusPresentation.Description;
        StatusChipText = statusPresentation.Chip;
        RaiseInstallActionState();
        RaisePropertyChanged(nameof(OnboardingSummaryText));

        if (result.Trigger == UpdateTriggerKind.Automatic)
        {
            if (result.Status == UpdateStatus.Updated)
            {
                _notificationService.ShowSuccess("Automatic update installed", "MetaGrid automatically installed the latest Dota2ProTracker hero grid.");
            }

            return;
        }

        if (result.Status == UpdateStatus.Updated)
        {
            _notificationService.ShowSuccess("MetaGrid installed", "The selected Dota account now has the MetaGrid-managed hero grid installed.");
        }
        else if (result.Status == UpdateStatus.BackupRestored)
        {
            _notificationService.ShowError("MetaGrid rolled back the installation", result.Message);
        }
        else if (result.Status == UpdateStatus.SourceUnavailable)
        {
            _notificationService.ShowError("MetaGrid source issue", result.Message);
        }
        else if (result.Status is UpdateStatus.Failed or UpdateStatus.ParsingFailed or UpdateStatus.UnexpectedResponse or UpdateStatus.WaitingForAccount)
        {
            _notificationService.ShowError("MetaGrid update issue", result.Message);
        }
    }

    private AutomaticCycleInputs CaptureAutomaticCycleInputs()
        => new(
            Accounts.Select(x => x.Model).ToList(),
            _settings.CreateCopy(),
            SelectedAccount?.AccountId,
            IsBusy);

    public async Task<UpdateRunResult> InstallGridAsync(InstallGridSnapshot installSnapshot)
    {
        var result = await _updateService.InstallGridAsync(Accounts.Select(x => x.Model).ToList(), _settings, installSnapshot, _lifetimeCts.Token);
        await ApplyUpdateResultAsync(result, rescheduleAfterUpdate: false);
        return result;
    }

    public async Task<UpdateRunResult> InstallGridAsync()
    {
        var preview = BuildInstallPreview();
        if (preview is null)
        {
            return new UpdateRunResult
            {
                Status = UpdateStatus.Failed,
                Message = "MetaGrid could not build a confirmed install snapshot. Run Check for Updates again before installing.",
                GridHash = _settings.LastRemoteHash ?? string.Empty,
                ProviderStatus = ProviderStatus.Unknown
            };
        }

        return await InstallGridAsync(preview.ConfirmedSnapshot);
    }

    private bool DoHashesMatch()
        => !string.IsNullOrWhiteSpace(_settings.LastRemoteHash)
           && !string.IsNullOrWhiteSpace(_settings.LastInstalledHash)
           && string.Equals(_settings.LastRemoteHash, _settings.LastInstalledHash, StringComparison.OrdinalIgnoreCase);

    public InstallGridPreview? BuildInstallPreview()
    {
        var selected = SelectedAccount;
        if (selected is null
            || string.IsNullOrWhiteSpace(_settings.LastRemoteHash)
            || string.IsNullOrWhiteSpace(selected.Model.DotaConfigDirectory)
            || _availableInstallSnapshot is null
            || !string.Equals(_availableInstallSnapshot.Hash, _settings.LastRemoteHash, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var targetPath = Path.Combine(selected.Model.DotaConfigDirectory, "hero_grid_config.json");
        var installSnapshot = new InstallGridSnapshot
        {
            Snapshot = _availableInstallSnapshot,
            AccountId = selected.AccountId,
            AccountDisplayName = selected.DisplayName,
            TargetPath = targetPath,
            PreparedAt = DateTimeOffset.UtcNow,
            HeroCount = _settings.LastHeroCount ?? _availableInstallSnapshot.Layouts.SelectMany(layout => layout.Categories).SelectMany(category => category.HeroIds).Distinct().Count(),
            GroupCount = _availableInstallSnapshot.Layouts.SelectMany(layout => layout.Categories).Select(category => category.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            HasExistingGridFile = File.Exists(targetPath)
        };

        return new InstallGridPreview(
            selected.DisplayName,
            selected.AccountId,
            _settings.LastSourceName ?? "Unknown source",
            installSnapshot.HeroCount,
            installSnapshot.GroupCount,
            ToShortHash(_settings.LastRemoteHash) ?? _settings.LastRemoteHash,
            targetPath,
            File.Exists(targetPath),
            InstallActionText,
            installSnapshot);
    }

    private void RaiseInstallActionState()
    {
        RaisePropertyChanged(nameof(HasLiveAvailableGrid));
        RaisePropertyChanged(nameof(HasInstalledGrid));
        RaisePropertyChanged(nameof(CanOfferInstallAction));
        RaisePropertyChanged(nameof(InstallActionText));
        RaisePropertyChanged(nameof(InstallActionSummary));
        RaisePropertyChanged(nameof(SelectedAccount));
        InstallGridCommand.NotifyCanExecuteChanged();
    }

    private HeroGridInstallActionState GetInstallActionState()
        => HeroGridInstallActionResolver.Resolve(
            SelectedAccount is { Model.DotaConfigDirectory.Length: > 0 },
            _availableInstallSnapshot is not null && !string.IsNullOrWhiteSpace(_settings.LastRemoteHash)
                && string.Equals(_availableInstallSnapshot.Hash, _settings.LastRemoteHash, StringComparison.OrdinalIgnoreCase),
            _settings.LastRemoteHash,
            _settings.LastInstalledHash,
            ParseOrigin(_settings.LastGridOrigin));

    private void RefreshOverviewState()
    {
        var presentation = HeroGridStatusPresenter.CreateOverview(_settings, Accounts.Count > 0);
        StatusTitle = presentation.Title;
        StatusText = presentation.Description;
        StatusChipText = presentation.Chip;
        RaiseInstallActionState();
    }

    private sealed record AutomaticCycleInputs(
        IReadOnlyList<SteamAccount> Accounts,
        AppSettings Settings,
        string? SelectedAccountId,
        bool IsBusy);
}

public sealed record UpdateIntervalOption(UpdateInterval Value, string Label)
{
    public override string ToString() => Label;
}

public sealed class HeroGridLayoutPreview
{
    public HeroGridLayoutPreview(string name, IReadOnlyList<HeroGridCategoryPreview> categories)
    {
        Name = name;
        Categories = categories;
    }

    public string Name { get; }
    public string DisplayLabel => ToDisplayLabel(Name);
    public IReadOnlyList<HeroGridCategoryPreview> Categories { get; }

    private static string ToDisplayLabel(string name)
    {
        if (name.Contains("Hard Support", StringComparison.OrdinalIgnoreCase))
        {
            return "Hard Support";
        }

        if (name.Contains("All Roles", StringComparison.OrdinalIgnoreCase))
        {
            return "All Roles";
        }

        if (name.Contains("Carry", StringComparison.OrdinalIgnoreCase))
        {
            return "Carry";
        }

        if (name.Contains("Offlane", StringComparison.OrdinalIgnoreCase))
        {
            return "Offlane";
        }

        if (name.Contains("Support", StringComparison.OrdinalIgnoreCase))
        {
            return "Support";
        }

        if (name.Contains("Mid", StringComparison.OrdinalIgnoreCase))
        {
            return "Mid";
        }

        return name;
    }
}

public sealed class HeroGridCategoryPreview
{
    public HeroGridCategoryPreview(string stableId, int order, string name, int heroCount, string heroPreviewText)
    {
        StableId = stableId;
        Order = order;
        Name = name;
        HeroCount = heroCount;
        HeroPreviewText = heroPreviewText;
    }

    public string StableId { get; }
    public int Order { get; }
    public string Name { get; }
    public int HeroCount { get; }
    public string HeroPreviewText { get; }
    public string HeroCountText => $"{HeroCount} {(HeroCount == 1 ? "hero" : "heroes")}";
}

public sealed record InstallGridPreview(
    string AccountDisplayName,
    string AccountId,
    string Source,
    int HeroCount,
    int GroupCount,
    string AvailableHash,
    string TargetPath,
    bool HasExistingGridFile,
    string ActionLabel,
    InstallGridSnapshot ConfirmedSnapshot);
