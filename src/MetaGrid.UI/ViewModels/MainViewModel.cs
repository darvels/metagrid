using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;
using MetaGrid.UI.Services;

namespace MetaGrid.UI.ViewModels;

public enum AppPage
{
    Dashboard,
    HeroGrid,
    Guides,
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
    private readonly IAppUpdateService _appUpdateService;
    private readonly IAppRuntimeInfo _appRuntimeInfo;
    private readonly IAppClock _appClock;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly GridSnapshotCacheService _gridSnapshotCacheService;
    private readonly IPersonalizationService _personalizationService;
    private readonly IPersonalHeroCacheService _personalHeroCacheService;
    private readonly IPersonalizedGridComposer _personalizedGridComposer;
    private readonly IGuideSubscriptionService _guideSubscriptionService;
    private readonly UiTextService _text;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private Task _startupGuideSyncTask = Task.CompletedTask;
    private readonly SemaphoreSlim _guideUiSyncGate = new(1, 1);
    private readonly HashSet<string> _busyGuideKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingGuideToggleKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _isGuidePickerOpen;
    private GuideHeroCardViewModel? _guidePickerHighlightedHero;
    private IReadOnlyDictionary<int, HeroDefinition> _heroDefinitionsById = new Dictionary<int, HeroDefinition>();
    private double _guideCatalogItemWidth = 240;
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
    private string _baseGridHashText = "Unknown";
    private string _roleSummaryText = string.Join(", ", KnownRoles);
    private string _gridCriteriaText = "Official Dota2ProTracker High Winrate grid";
    private string _topFivePreviewText = "Run Check for Updates to load the latest official Dota2ProTracker High Winrate layouts.";
    private string _statusChipText = "Not checked";
    private string _autoUpdateChipText = "Auto Update On";
    private string _providerStatusChipText = "Source not checked";
    private string _personalizationInput = string.Empty;
    private string _personalizationSourceText = "Selected Steam account";
    private string _personalizationStatusText = "Personal heroes off";
    private string _personalizationDetailsText = "OpenDota automatically uses your selected Steam account to add MY BEST HEROES to the All Roles layout.";
    private string _personalizationConnectedAccountText = "No OpenDota account resolved yet";
    private string _personalizationLastRefreshText = "Never";
    private string _guidesSearchText = string.Empty;
    private GuideHeroCardViewModel? _selectedGuideHero;
    private HeroGridLayoutPreview? _selectedGridPreviewLayout;
    private IReadOnlyDictionary<int, string>? _heroNamesById;
    private IReadOnlyList<GuideHeroCardViewModel> _allGuideHeroes = [];
    private bool _steamAutoDetectionStarted;
    private CancellationTokenSource? _scheduleCts;
    private Task? _schedulerTask;
    private Task? _startupAutomaticUpdateTask;
    private CancellationTokenSource? _appUpdateScheduleCts;
    private Task? _appUpdateSchedulerTask;
    private AppUpdateInfo? _latestAppUpdateInfo;
    private HeroGridOverviewState _overviewState = HeroGridOverviewState.NotChecked;
    private string _appUpdateStatusTitle = "App updates not checked yet";
    private string _appUpdateStatusText = "MetaGrid can check GitHub Releases for newer application builds separately from Dota hero-grid updates.";
    private string _appUpdateChipText = "Not checked";
    private string _appUpdateLastCheckedText = "Never";
    private string _appUpdateAvailableVersionText = "Unknown";
    private readonly string _currentAppVersionText;
    private MediaBrush _statusChipBackgroundBrush = CreateBrush("#262626");
    private MediaBrush _statusChipBorderBrush = CreateBrush("#343434");
    private MediaBrush _providerChipBackgroundBrush = CreateBrush("#262626");
    private MediaBrush _providerChipBorderBrush = CreateBrush("#343434");
    private MediaBrush _providerIndicatorBrush = CreateBrush("#858585");
    private LanguageOption? _selectedLanguageOption;

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
        IAppUpdateService appUpdateService,
        IAppRuntimeInfo appRuntimeInfo,
        IAppClock appClock,
        IUiDispatcher uiDispatcher,
        GridSnapshotCacheService gridSnapshotCacheService,
        IPersonalizationService personalizationService,
        IPersonalHeroCacheService personalHeroCacheService,
        IPersonalizedGridComposer personalizedGridComposer,
        IGuideSubscriptionService guideSubscriptionService,
        UiTextService text)
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
        _appUpdateService = appUpdateService;
        _appRuntimeInfo = appRuntimeInfo;
        _appClock = appClock;
        _uiDispatcher = uiDispatcher;
        _gridSnapshotCacheService = gridSnapshotCacheService;
        _personalizationService = personalizationService;
        _personalHeroCacheService = personalHeroCacheService;
        _personalizedGridComposer = personalizedGridComposer;
        _guideSubscriptionService = guideSubscriptionService;
        _text = text;
        _currentAppVersionText = appRuntimeInfo.GetCurrentVersion();

        Accounts = [];
        History = [];
        GridPreviewLayouts = [];
        PersonalHeroPreviewItems = [];
        GuideHeroes = [];
        MyGuideHeroes = [];
        GuideRoleOptions = [];
        Text = text;
        Presets = Enum.GetValues<HeroGridPreset>();
        LanguageOptions =
        [
            new LanguageOption(AppLanguage.English, "EN"),
            new LanguageOption(AppLanguage.Russian, "RU")
        ];
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
        PersonalizationSourceOptions =
        [
            new PersonalizationSourceModeOption(PersonalizationAccountSourceMode.SelectedSteamAccount, "Selected Steam account"),
            new PersonalizationSourceModeOption(PersonalizationAccountSourceMode.ManualAccount, "Manual OpenDota account")
        ];

        CheckNowCommand = new AsyncRelayCommand(() => CheckForUpdatesAsync(forceWrite: false), CanRunInteractiveCommand);
        ForceRefreshCommand = new AsyncRelayCommand(ForceUpdateAsync, CanRunInteractiveCommand);
        InstallGridCommand = new AsyncRelayCommand(InstallGridAsync, CanRunInstallCommand);
        CheckAppUpdatesCommand = new AsyncRelayCommand(CheckAppUpdatesAsync, CanRunInteractiveCommand);
        InstallAppUpdateCommand = new AsyncRelayCommand(InstallAppUpdateAsync, CanInstallAppUpdate);
        LaterAppUpdateCommand = new AsyncRelayCommand(DeferAppUpdateAsync, CanDeferAppUpdate);
        DetectSteamCommand = new AsyncRelayCommand(RefreshAccountsAsync, CanRunInteractiveCommand);
        SaveSettingsCommand = new AsyncRelayCommand(SaveEditableSettingsAsync, CanSaveSettings);
        ClearHistoryCommand = new AsyncRelayCommand(ClearHistoryAsync, CanRunInteractiveCommand);
        RestoreBackupCommand = new AsyncRelayCommand(RestoreBackupAsync, CanRunInteractiveCommand);
        OpenLogsCommand = new RelayCommand(() => OpenFolder(_loggingService.GetLogsDirectory()));
        OpenBackupsCommand = new RelayCommand(() => OpenFolder(_backupService.GetBackupsDirectory()));
        OpenConfigCommand = new RelayCommand(() => OpenFolder(_appPaths.RootDirectory));
        NextOnboardingCommand = new RelayCommand(AdvanceOnboarding);
        FinishOnboardingCommand = new AsyncRelayCommand(FinishOnboardingAsync);
        ConnectPersonalizationCommand = new AsyncRelayCommand(ConnectPersonalizationAsync, CanRunInteractiveCommand);
        DisconnectPersonalizationCommand = new AsyncRelayCommand(DisconnectPersonalizationAsync, CanRunInteractiveCommand);
        RefreshPersonalHeroesCommand = new AsyncRelayCommand(RefreshPersonalHeroesAsync, CanRunInteractiveCommand);
    }

    public ObservableCollection<AccountViewModel> Accounts { get; }
    public ObservableCollection<UpdateHistoryEntry> History { get; }
    public ObservableCollection<HeroGridLayoutPreview> GridPreviewLayouts { get; }
    public ObservableCollection<PersonalHeroPreviewItem> PersonalHeroPreviewItems { get; }
    public ObservableCollection<GuideHeroCardViewModel> GuideHeroes { get; }
    public ObservableCollection<GuideRoleOptionViewModel> GuideRoleOptions { get; }
    public ObservableCollection<GuideHeroCardViewModel> MyGuideHeroes { get; }
    public bool HasMyGuides => MyGuideHeroes.Count > 0;
    public bool HasPickerResults => GuideHeroes.Count > 0;
    public bool IsGuidePickerOpen
    {
        get => _isGuidePickerOpen;
        set => SetProperty(ref _isGuidePickerOpen, value);
    }
    public GuideHeroCardViewModel? GuidePickerHighlightedHero
    {
        get => _guidePickerHighlightedHero;
        set => SetProperty(ref _guidePickerHighlightedHero, value);
    }
    public void SelectPickerHero()
    {
        if (GuidePickerHighlightedHero is not { } hero) return;
        HandleGuideHeroSelected(hero);
        GuidesSearchText = string.Empty;
        IsGuidePickerOpen = false;
    }
    public double GuideCatalogItemWidth
    {
        get => _guideCatalogItemWidth;
        set => SetProperty(ref _guideCatalogItemWidth, value);
    }
    public Array Presets { get; }
    public UiTextService Text { get; }
    public IReadOnlyList<LanguageOption> LanguageOptions { get; }
    public IReadOnlyList<UpdateIntervalOption> UpdateIntervalOptions { get; private set; }
    public IReadOnlyList<PersonalizationSourceModeOption> PersonalizationSourceOptions { get; }

    public AsyncRelayCommand CheckNowCommand { get; }
    public AsyncRelayCommand ForceRefreshCommand { get; }
    public AsyncRelayCommand InstallGridCommand { get; }
    public AsyncRelayCommand CheckAppUpdatesCommand { get; }
    public AsyncRelayCommand InstallAppUpdateCommand { get; }
    public AsyncRelayCommand LaterAppUpdateCommand { get; }
    public AsyncRelayCommand DetectSteamCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public AsyncRelayCommand ClearHistoryCommand { get; }
    public AsyncRelayCommand RestoreBackupCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public RelayCommand OpenBackupsCommand { get; }
    public RelayCommand OpenConfigCommand { get; }
    public RelayCommand NextOnboardingCommand { get; }
    public AsyncRelayCommand FinishOnboardingCommand { get; }
    public AsyncRelayCommand ConnectPersonalizationCommand { get; }
    public AsyncRelayCommand DisconnectPersonalizationCommand { get; }
    public AsyncRelayCommand RefreshPersonalHeroesCommand { get; }

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
        get => _text.Translate(_statusTitle);
        set => SetProperty(ref _statusTitle, value);
    }

    public string StatusText
    {
        get => _text.Translate(_statusText);
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
        get => _text.Translate(_selectedAccountText);
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

    public string AppUpdateStatusTitle
    {
        get => _text.Translate(_appUpdateStatusTitle);
        private set => SetProperty(ref _appUpdateStatusTitle, value);
    }

    public string AppUpdateStatusText
    {
        get => _text.Translate(_appUpdateStatusText);
        private set => SetProperty(ref _appUpdateStatusText, value);
    }

    public string AppUpdateChipText
    {
        get => _text.Translate(_appUpdateChipText);
        private set => SetProperty(ref _appUpdateChipText, value);
    }

    public string AppUpdateLastCheckedText
    {
        get => _appUpdateLastCheckedText;
        private set => SetProperty(ref _appUpdateLastCheckedText, value);
    }

    public string AppUpdateAvailableVersionText
    {
        get => _appUpdateAvailableVersionText;
        private set => SetProperty(ref _appUpdateAvailableVersionText, value);
    }

    public string CurrentAppVersionText => _currentAppVersionText;

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
                RaisePropertyChanged(nameof(OnboardingStepLabel));
                RaisePropertyChanged(nameof(OnboardingSelectedAccountLabel));
                RaisePropertyChanged(nameof(OnboardingAccountsHintText));
                RaisePropertyChanged(nameof(OnboardingAutoUpdateHelpText));
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
        get => _text.Translate(_providerStatusText);
        set => SetProperty(ref _providerStatusText, value);
    }

    public string ProviderCardTitle
    {
        get => _text.Translate(_providerCardTitle);
        set => SetProperty(ref _providerCardTitle, value);
    }

    public string SteamDetectionStatusText
    {
        get => _text.Translate(_steamDetectionStatusText);
        set => SetProperty(ref _steamDetectionStatusText, value);
    }

    public string SteamDetectionPathText
    {
        get => _text.Translate(_steamDetectionPathText);
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
        get => _text.Translate(_currentSourceText);
        set => SetProperty(ref _currentSourceText, value);
    }

    public string HeroCountText
    {
        get => _heroCountText;
        set => SetProperty(ref _heroCountText, value);
    }

    public string BaseGridHashText
    {
        get => _baseGridHashText;
        set => SetProperty(ref _baseGridHashText, value);
    }

    public string RoleSummaryText
    {
        get => _roleSummaryText;
        set => SetProperty(ref _roleSummaryText, value);
    }

    public string GridCriteriaText
    {
        get => _text.Translate(_gridCriteriaText);
        set => SetProperty(ref _gridCriteriaText, value);
    }

    public string TopFivePreviewText
    {
        get => _text.Translate(_topFivePreviewText);
        set => SetProperty(ref _topFivePreviewText, value);
    }

    public string StatusChipText
    {
        get => _text.Translate(_statusChipText);
        set => SetProperty(ref _statusChipText, value);
    }

    public string AutoUpdateChipText
    {
        get => _text.Translate(_autoUpdateChipText);
        set => SetProperty(ref _autoUpdateChipText, value);
    }

    public string ProviderStatusChipText
    {
        get => _text.Translate(_providerStatusChipText);
        set => SetProperty(ref _providerStatusChipText, value);
    }

    public string PersonalizationInput
    {
        get => _personalizationInput;
        set => SetProperty(ref _personalizationInput, value);
    }

    public string PersonalizationStatusText
    {
        get => _text.Translate(_personalizationStatusText);
        set => SetProperty(ref _personalizationStatusText, value);
    }

    public string PersonalizationSourceText
    {
        get => _text.Translate(_personalizationSourceText);
        set => SetProperty(ref _personalizationSourceText, value);
    }

    public string PersonalizationDetailsText
    {
        get => _text.Translate(_personalizationDetailsText);
        set => SetProperty(ref _personalizationDetailsText, value);
    }

    public string PersonalizationConnectedAccountText
    {
        get => _text.Translate(_personalizationConnectedAccountText);
        set => SetProperty(ref _personalizationConnectedAccountText, value);
    }

    public string PersonalizationLastRefreshText
    {
        get => _text.Translate(_personalizationLastRefreshText);
        set => SetProperty(ref _personalizationLastRefreshText, value);
    }

    public string GuidesSearchText
    {
        get => _guidesSearchText;
        set
        {
            if (SetProperty(ref _guidesSearchText, value))
            {
                ApplyGuideFilter();
                if (!string.IsNullOrWhiteSpace(value)) IsGuidePickerOpen = true;
            }
        }
    }

    public GuideHeroCardViewModel? SelectedGuideHero
    {
        get => _selectedGuideHero;
        private set
        {
            if (SetProperty(ref _selectedGuideHero, value))
            {
                RaisePropertyChanged(nameof(HasGuideSelection));
                RaisePropertyChanged(nameof(SelectedGuideName));
                RaisePropertyChanged(nameof(SelectedGuideInternalName));
                RaisePropertyChanged(nameof(SelectedGuidePortraitUrl));
                RaisePropertyChanged(nameof(SelectedGuideRoleSummary));
                RaisePropertyChanged(nameof(SelectedGuideSubscriptionSummary));
                RefreshGuideRoleOptions();
            }
        }
    }

    public LanguageOption? SelectedLanguageOption
    {
        get => _selectedLanguageOption;
        set
        {
            if (SetProperty(ref _selectedLanguageOption, value) && value is not null)
            {
                _text.Language = value.Value;
                _settings.Language = value.Value;
                _editableSettings.Language = value.Value;
                RefreshLocalizedBindings();
                _ = PersistSettingsAsync(CancellationToken.None);
            }
        }
    }

    public MediaBrush StatusChipBackgroundBrush
    {
        get => _statusChipBackgroundBrush;
        private set => SetProperty(ref _statusChipBackgroundBrush, value);
    }

    public MediaBrush StatusChipBorderBrush
    {
        get => _statusChipBorderBrush;
        private set => SetProperty(ref _statusChipBorderBrush, value);
    }

    public MediaBrush ProviderStatusChipBackgroundBrush
    {
        get => _providerChipBackgroundBrush;
        private set => SetProperty(ref _providerChipBackgroundBrush, value);
    }

    public MediaBrush ProviderStatusChipBorderBrush
    {
        get => _providerChipBorderBrush;
        private set => SetProperty(ref _providerChipBorderBrush, value);
    }

    public MediaBrush ProviderIndicatorBrush
    {
        get => _providerIndicatorBrush;
        private set => SetProperty(ref _providerIndicatorBrush, value);
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
    public bool ShowAppUpdateBanner => _latestAppUpdateInfo?.State == AppUpdateCheckState.UpdateAvailable;
    public bool HasAppUpdateAvailable => _latestAppUpdateInfo?.IsUpdateAvailable == true;
    public bool HasDeferredAppUpdate => _latestAppUpdateInfo?.State == AppUpdateCheckState.Deferred;
    public bool HasLiveAvailableGrid => !string.IsNullOrWhiteSpace(_settings.LastEffectiveGridHash ?? _settings.LastRemoteHash) && ParseOrigin(_settings.LastGridOrigin) != GridOriginKind.Cached;
    public bool HasInstalledGrid => GetSelectedInstalledState() == InstalledMetaGridState.Present && !string.IsNullOrWhiteSpace(GetSelectedInstalledHash());
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
        ? _text.Translate("Unsaved changes are ready to save.")
        : _text.Translate("All displayed settings are currently saved.");
    public string HeroCountLabel => _text.T("Unique Heroes", "Уникальные герои");
    public string CurrentGridPreviewTitle => SelectedGridPreviewLayout is null
        ? _text.Translate("Current Grid Preview")
        : $"{SelectedGridPreviewLayout.Name} Layout";
    public string CurrentGridPreviewSubtitle => SelectedGridPreviewLayout is null
        ? _text.Translate("Run Check for Updates to load the latest official Dota2ProTracker High Winrate layouts.")
        : $"{SelectedGridPreviewLayout.Categories.Count} category blocks in official order.";

    public string OnboardingTitle => OnboardingStep switch
    {
        1 => _text.Translate("Welcome to MetaGrid"),
        2 => _text.Translate("Steam Detection"),
        3 => _text.Translate("Dota Accounts"),
        4 => _text.Translate("Automatic Updates"),
        _ => _text.Translate("You're ready")
    };

    public string OnboardingDescription => OnboardingStep switch
    {
        1 => "Keep a dedicated MetaGrid hero layout current using the official Dota2ProTracker High Winrate grid while preserving your own custom Dota layouts.",
        2 => SteamDetectionStatusText,
        3 => Accounts.Count == 0
            ? _text.T("No Dota accounts are selected yet. MetaGrid stays usable even if Steam or Dota is not detected immediately.", "Пока не выбран ни один аккаунт Dota. MetaGrid остаётся рабочим, даже если Steam или Dota не были обнаружены сразу.")
            : _text.T($"Detected {Accounts.Count} Dota-ready account(s). Select the account MetaGrid should update.", $"Найдено Dota-аккаунтов: {Accounts.Count}. Выберите аккаунт, который будет обновлять MetaGrid."),
        4 => _text.T(
            $"Automatic updates are {(Settings.AutoUpdateEnabled ? "enabled" : "disabled")} with a {FormatInterval(Settings.UpdateInterval)} interval for checking the official Dota2ProTracker High Winrate grid.",
            $"Автообновления {(Settings.AutoUpdateEnabled ? "включены" : "выключены")}. Интервал проверки официальной High Winrate сетки Dota2ProTracker: {FormatInterval(Settings.UpdateInterval)}."),
        _ => _text.T("MetaGrid can now open the main workspace, continue background checks, and keep your Dota 2 hero grid in sync.", "MetaGrid теперь может открыть основное окно, продолжить фоновые проверки и держать вашу сетку героев Dota 2 в актуальном состоянии.")
    };

    public string OnboardingPrimaryLabel => IsOnboardingReadyStep ? _text.Translate("Open MetaGrid") : _text.Translate("Continue");
    public string OnboardingStepLabel => _text.FormatStepChip(OnboardingStep);
    public string OnboardingSelectedAccountLabel => _text.T($"Selected account: {SelectedAccountText}", $"Выбранный аккаунт: {SelectedAccountText}");
    public string OnboardingAccountsHintText => _text.T("If you have multiple valid Dota accounts, you can fine-tune the selection later on the Accounts page.", "Если у вас несколько корректных аккаунтов Dota, позже можно точно выбрать нужный на странице аккаунтов.");
    public string OnboardingAutoUpdateHelpText => _text.T("When enabled, MetaGrid checks Dota2ProTracker on schedule and safely installs a new grid automatically only when the validated semantic hash changes.", "Когда функция включена, MetaGrid по расписанию проверяет Dota2ProTracker и автоматически устанавливает новую сетку только при реальном изменении проверенного семантического хэша.");

    public string OnboardingSummaryText =>
        _text.Language == AppLanguage.Russian
            ? $"Источник: {CurrentSourceText}\nВыбранный аккаунт: {SelectedAccountText}\nСледующая проверка: {NextCheckText}"
            : $"Source: {CurrentSourceText}\nSelected account: {SelectedAccountText}\nNext check: {NextCheckText}";

    public string AppUpdateBannerTitle => HasAppUpdateAvailable ? "MetaGrid update available" : "MetaGrid is current";
    public string AppUpdateBannerText => HasAppUpdateAvailable
        ? _text.T($"GitHub Releases has MetaGrid {AppUpdateAvailableVersionText} ready. Your current app version is {CurrentAppVersionText}.", $"GitHub Releases Ð´Ð¾ÑÑ‚ÑƒÐ¿Ð½Ð° Ð²ÐµÑ€ÑÐ¸Ñ MetaGrid {AppUpdateAvailableVersionText}. Ð¡ÐµÐ¹Ñ‡Ð°Ñ ÑƒÑÑ‚Ð°Ð½Ð¾Ð²Ð»ÐµÐ½Ð° Ð²ÐµÑ€ÑÐ¸Ñ {CurrentAppVersionText}.")
        : _text.T("This MetaGrid build is already current.", "Ð­Ñ‚Ð° ÑÐ±Ð¾Ñ€ÐºÐ° MetaGrid ÑƒÐ¶Ðµ Ð°ÐºÑ‚ÑƒÐ°Ð»ÑŒÐ½Ð°.");
    public string CheckAppUpdatesLabel => _text.T("Check for App Updates", "ÐŸÑ€Ð¾Ð²ÐµÑ€Ð¸Ñ‚ÑŒ Ð¾Ð±Ð½Ð¾Ð²Ð»ÐµÐ½Ð¸Ñ Ð¿Ñ€Ð¸Ð»Ð¾Ð¶ÐµÐ½Ð¸Ñ");
    public string UpdateNowLabel => _text.T("Update Now", "ÐžÐ±Ð½Ð¾Ð²Ð¸Ñ‚ÑŒ ÑÐµÐ¹Ñ‡Ð°Ñ");
    public string LaterLabel => _text.T("Later", "ÐŸÐ¾Ð·Ð¶Ðµ");

    public bool HasPersonalHeroPreview => PersonalHeroPreviewItems.Count > 0;
    public bool HasGuideSelection => SelectedGuideHero is not null;
    public string SelectedGuideName => SelectedGuideHero?.Hero.LocalizedName ?? "Select a hero";
    public string SelectedGuideInternalName => SelectedGuideHero?.Hero.InternalName ?? "Choose a hero from the catalog to configure Auto Guides.";
    public string? SelectedGuidePortraitUrl => SelectedGuideHero?.PortraitUrl;
    public string SelectedGuideRoleSummary => SelectedGuideHero is null
        ? "Each role is subscribed independently. Enabling Carry does not automatically enable Mid or Support."
        : $"Configure MetaGrid Auto Guides for {SelectedGuideHero.Hero.LocalizedName}. Each role is tracked independently and stored as its own subscription.";
    public string SelectedGuideSubscriptionSummary => SelectedGuideHero is null
        ? "No hero selected yet."
        : SelectedGuideHero.ActiveRoleCount == 0
            ? "No Auto Guide roles are enabled for this hero yet."
            : $"{SelectedGuideHero.ActiveRoleCount} Auto Guide role(s) enabled for this hero.";

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
        await TryLoadGuideCatalogAsync();
        ApplySettingsToUi();
        await TryStartAutomaticUpdatesAsync();
        await TryStartAppUpdateChecksAsync();

        IsInitializing = false;
        _startupGuideSyncTask = SyncEnabledGuidesAtStartupAsync();

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

    private async Task SyncEnabledGuidesAtStartupAsync()
    {
        await Task.Yield();
        try
        {
            await MetaGrid.Core.Services.GuideStartupSync.RunAsync(_settings.GuideSubscriptions, async (heroId, role, token) =>
            {
                await _loggingService.LogAsync(LogLevelKind.Information, "Startup Auto Guide check started.", new { heroId, role }, token);
                var hero = _allGuideHeroes.FirstOrDefault(item => item.Hero.Id == heroId)?.Hero;
                if (hero is not null)
                {
                    try { await SyncGuideRoleSubscriptionAsync(hero, role, token); }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        await _loggingService.LogAsync(LogLevelKind.Warning, "Startup Auto Guide check timed out.", new { heroId, role }, CancellationToken.None);
                    }
                }
                else
                    await _loggingService.LogAsync(LogLevelKind.Warning, "Enabled guide hero is absent from the catalog.", new { heroId, role }, token);
            }, _lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            await _loggingService.LogAsync(LogLevelKind.Warning, "Startup guide sync failed.", new { ex.Message }, CancellationToken.None);
        }
    }

    public async Task ShutdownAsync()
    {
        _lifetimeCts.Cancel();
        try { await _startupGuideSyncTask.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException)
        {
            await _loggingService.LogAsync(LogLevelKind.Warning, "Startup guide sync exceeded shutdown grace period.", null, CancellationToken.None);
        }
        await StopSchedulerAsync();
        await StopAppUpdateSchedulerAsync();

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
    private bool CanInstallAppUpdate() => CanRunInteractiveCommand() && HasAppUpdateAvailable;
    private bool CanDeferAppUpdate() => CanRunInteractiveCommand() && HasAppUpdateAvailable;

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

    private async Task TryLoadGuideCatalogAsync()
    {
        try
        {
            var heroes = await _heroCatalogService.LoadAllAsync(_lifetimeCts.Token);
            _allGuideHeroes = heroes
                .Select(hero => new GuideHeroCardViewModel(hero, HandleGuideHeroSelected))
                .ToList();
            ApplyGuideFilter();
            RestoreGuideSelection();
        }
        catch (Exception ex)
        {
            InitializationWarning = string.IsNullOrWhiteSpace(InitializationWarning)
                ? "The Guides catalog could not be loaded."
                : InitializationWarning;
            await _loggingService.LogAsync(LogLevelKind.Warning, "Guide catalog loading failed during startup.", new { ex.Message }, _lifetimeCts.Token);
        }
    }

    private void ApplySettingsToUi()
    {
        _text.Language = _settings.Language;
        SelectedLanguageOption = LanguageOptions.FirstOrDefault(option => option.Value == _settings.Language) ?? LanguageOptions[0];
        LastCheckedText = FormatDashboardTimestamp(_settings.LastCheckAt);
        LastUpdatedText = FormatDashboardTimestamp(_settings.LastSuccessfulUpdateAt);
        AvailableGridHashText = ToShortHash(_settings.LastEffectiveGridHash ?? _settings.LastRemoteHash) ?? "Unknown";
        InstalledGridHashText = "Not installed";
        CachedGridHashText = ToShortHash(_settings.LastCachedHash) ?? "Not cached";
        BaseGridHashText = ToShortHash(_settings.LastBaseSourceHash) ?? "Unknown";
        CurrentSourceText = BuildSourceStatusText();
        ApplyAppUpdateStateFromSettings();
        ApplyAppUpdateStateFromSettings();
        RoleSummaryText = string.IsNullOrWhiteSpace(_settings.LastRoleSummary) ? string.Join(", ", KnownRoles) : _settings.LastRoleSummary;
        GridCriteriaText = BuildGridCriteriaText();
        AutoUpdateChipText = _settings.AutoUpdateEnabled ? "Auto Update On" : "Auto Update Off";
        ProviderCardTitle = "Data Source";
        ProviderStatusText = ToProviderStatusText(_settings.LastProviderStatus, ParseOrigin(_settings.LastGridOrigin), _settings.LastSourceName);
        ProviderStatusChipText = ToProviderChipText(_settings.LastProviderStatus, ParseOrigin(_settings.LastGridOrigin), _settings.LastSourceName);
        ApplyAppUpdateStateFromSettings();
        PersonalizationInput = _settings.PersonalizationAccountSourceMode == PersonalizationAccountSourceMode.ManualAccount
            ? _settings.PersonalizationManualAccountId ?? string.Empty
            : string.Empty;
        ApplyPersonalizationStateFromSettings();
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
        _ = TryLoadPersonalHeroPreviewAsync();
        SyncGuideSubscriptionStateFromSettings();

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

    private void ApplyGuideFilter()
    {
        var filtered = _allGuideHeroes.Where(hero => GuideWorkspace.Matches(hero.Hero, GuidesSearchText)).ToList();

        GuideHeroes.Clear();
        foreach (var hero in filtered)
        {
            GuideHeroes.Add(hero);
        }

        GuidePickerHighlightedHero = GuideHeroes.FirstOrDefault();
        RaisePropertyChanged(nameof(HasPickerResults));
    }

    private void RestoreGuideSelection()
    {
        var preferredHeroId = _settings.GuideSubscriptions.FirstOrDefault(subscription => subscription.IsEnabled || subscription.NeedsRemoval)?.HeroId;
        SelectedGuideHero = preferredHeroId.HasValue
            ? _allGuideHeroes.FirstOrDefault(hero => hero.Hero.Id == preferredHeroId.Value)
            : null;
        UpdateGuideSelectionFlags();
    }

    private void SyncGuideSubscriptionStateFromSettings()
    {
        var activeKeys = _settings.GuideSubscriptions
            .Where(subscription => subscription.IsEnabled)
            .Select(subscription => subscription.StableKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var hero in _allGuideHeroes)
        {
            hero.ActiveRoleCount = Enum.GetValues<GuideRole>()
                .Count(role => activeKeys.Contains(GuideSubscriptionRecord.CreateStableKey(hero.Hero.Id, role)));
        }

        MyGuideHeroes.Clear();
        foreach (var group in GuideWorkspace.ConfiguredHeroes(_settings.GuideSubscriptions))
        {
            var hero = _allGuideHeroes.FirstOrDefault(item => item.Hero.Id == group.Key);
            if (hero is null)
            {
                var record = group.First();
                hero = new GuideHeroCardViewModel(new HeroDefinition { Id = group.Key, LocalizedName = record.HeroDisplayName,
                    InternalName = record.HeroInternalName, Slug = record.HeroInternalName }, HandleGuideHeroSelected);
            }
            hero.Roles = group.Select(record => new GuideRoleBadge(ToGuideRoleLabel(record.Role),
                GuideStatusPresentation.Tone(record.IsEnabled, record.Status),
                $"{ToGuideRoleLabel(record.Role)}: {GuideStatusPresentation.Label(_text, record.IsEnabled, record.Status)}")).ToArray();
            MyGuideHeroes.Add(hero);
        }
        RaisePropertyChanged(nameof(HasMyGuides));
        RefreshGuideRoleOptions();
        RaisePropertyChanged(nameof(SelectedGuideSubscriptionSummary));
    }

    private void HandleGuideHeroSelected(GuideHeroCardViewModel hero)
    {
        SelectedGuideHero = hero;
        UpdateGuideSelectionFlags();
    }

    private void UpdateGuideSelectionFlags()
    {
        foreach (var hero in _allGuideHeroes)
        {
            hero.IsSelected = ReferenceEquals(hero, SelectedGuideHero);
        }
    }

    private void RefreshGuideRoleOptions()
    {
        GuideRoleOptions.Clear();
        if (SelectedGuideHero is null)
        {
            return;
        }

        var hero = SelectedGuideHero.Hero;
        foreach (var role in Enum.GetValues<GuideRole>())
        {
            var existing = _settings.GuideSubscriptions.FirstOrDefault(subscription =>
                subscription.HeroId == SelectedGuideHero.Hero.Id &&
                subscription.Role == role);

            var option = new GuideRoleOptionViewModel(
                role,
                ToGuideRoleLabel(role),
                existing?.IsEnabled ?? false,
                isEnabled => SetGuideRoleSubscriptionSafelyAsync(hero, role, isEnabled),
                () => SyncGuideRoleSubscriptionAsync(hero, role));
            ApplyGuideRoleOptionState(option, existing);
            GuideRoleOptions.Add(option);
        }
    }

    private async Task SetGuideRoleSubscriptionSafelyAsync(HeroDefinition hero, GuideRole role, bool enabled)
    {
        var key = GuideSubscriptionRecord.CreateStableKey(hero.Id, role);
        if (!_pendingGuideToggleKeys.Add(key)) { RefreshGuideRoleOptions(); return; }
        RefreshGuideRoleOptions();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        cts.CancelAfter(TimeSpan.FromSeconds(90));
        var acquired = false;
        try
        {
            await _guideUiSyncGate.WaitAsync(cts.Token);
            acquired = true;
            await SetGuideRoleSubscriptionAsync(hero, role, enabled, cts.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            var record = _settings.GuideSubscriptions.FirstOrDefault(item => item.StableKey == key);
            if (record is not null)
            {
                record.Status = record.NeedsRemoval ? GuideSubscriptionStatus.RemovalPending : GuideSubscriptionStatus.Error;
                record.LastError = record.StatusMessage = ex.Message;
                await PersistSettingsAsync(CancellationToken.None);
            }
            await _loggingService.LogAsync(LogLevelKind.Warning, "Guide subscription toggle failed.", new { hero.Id, role, ex.Message }, CancellationToken.None);
        }
        finally
        {
            if (acquired) _guideUiSyncGate.Release();
            _pendingGuideToggleKeys.Remove(key);
            SyncGuideSubscriptionStateFromSettings();
        }
    }

    private async Task SetGuideRoleSubscriptionAsync(HeroDefinition hero, GuideRole role, bool isEnabled, CancellationToken token)
    {
        var existing = _settings.GuideSubscriptions.FirstOrDefault(subscription =>
            subscription.HeroId == hero.Id &&
            subscription.Role == role);

        if (existing is null)
        {
            existing = new GuideSubscriptionRecord
            {
                HeroId = hero.Id,
                HeroInternalName = hero.InternalName,
                HeroDisplayName = hero.LocalizedName,
                Role = role
            };
            _settings.GuideSubscriptions.Add(existing);
        }

        existing.IsEnabled = isEnabled;
        existing.RemovalRequested = !isEnabled;
        existing.LastError = null;
        existing.HeroInternalName = hero.InternalName;
        existing.HeroDisplayName = hero.LocalizedName;
        existing.Status = isEnabled ? GuideSubscriptionStatus.Pending : GuideSubscriptionStatus.RemovalPending;
        existing.StatusMessage = isEnabled
            ? "Auto Guide is enabled and ready to sync."
            : "Owned guide removal is pending.";
        _settings.GuideSubscriptions = GuideSubscriptionCollection.Normalize(_settings.GuideSubscriptions);
        SyncGuideSubscriptionStateFromSettings();
        RaisePropertyChanged(nameof(Settings));
        await PersistSettingsAsync(token);
        await _loggingService.LogAsync(LogLevelKind.Information, "Auto Guide role intent persisted.",
            new { hero.Id, role, Enabled = isEnabled, RemovalRequested = !isEnabled }, token);
        await SyncGuideRoleCoreAsync(hero, role, token);
    }

    private async Task SyncGuideRoleSubscriptionAsync(HeroDefinition hero, GuideRole role, CancellationToken cancellationToken = default)
    {
        using var syncCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token, cancellationToken);
        syncCts.CancelAfter(TimeSpan.FromSeconds(90));
        await _guideUiSyncGate.WaitAsync(syncCts.Token);
        var key = GuideSubscriptionRecord.CreateStableKey(hero.Id, role);
        _busyGuideKeys.Add(key);
        RefreshGuideRoleOptions();
        try { await SyncGuideRoleCoreAsync(hero, role, syncCts.Token); }
        finally
        {
            _busyGuideKeys.Remove(key);
            RefreshGuideRoleOptions();
            _guideUiSyncGate.Release();
        }
    }

    private async Task SyncGuideRoleCoreAsync(HeroDefinition hero, GuideRole role, CancellationToken token)
    {
        var subscription = _settings.GuideSubscriptions.FirstOrDefault(record =>
            record.HeroId == hero.Id &&
            record.Role == role);
        var option = SelectedGuideHero?.Hero.Id == hero.Id
            ? GuideRoleOptions.FirstOrDefault(item => item.Role == role) : null;
        if (subscription is null || (!subscription.IsEnabled && !subscription.NeedsRemoval))
        {
            return;
        }

        if (SelectedAccount?.Model is not { } account)
        {
            subscription.Status = subscription.NeedsRemoval ? GuideSubscriptionStatus.RemovalPending : GuideSubscriptionStatus.Pending;
            subscription.StatusMessage = "Select a Dota account before syncing this Auto Guide.";
            subscription.LastError = null;
            if (option is not null) ApplyGuideRoleOptionState(option, subscription);
            SyncGuideSubscriptionStateFromSettings();
            await PersistSettingsAsync(token);
            return;
        }

        if (subscription.NeedsRemoval)
        {
            subscription.Status = GuideSubscriptionStatus.Removing;
            SyncGuideSubscriptionStateFromSettings();
            try
            {
                var result = await _guideSubscriptionService.RemoveAsync(account, subscription, token);
                subscription = _settings.GuideSubscriptions.First(record => record.HeroId == hero.Id && record.Role == role);
                if (!subscription.ApplyVerifiedRemoval(result))
                {
                    subscription.IsEnabled = false;
                    subscription.RemovalRequested = true;
                    subscription.RemoteFile = result.RemoteFile ?? subscription.RemoteFile;
                    subscription.Status = result.Status;
                    subscription.LastError = subscription.StatusMessage = result.Message;
                }
                await PersistSettingsAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                subscription = _settings.GuideSubscriptions.First(record => record.HeroId == hero.Id && record.Role == role);
                subscription.Status = GuideSubscriptionStatus.RemovalPending;
                subscription.StatusMessage = "Removal interrupted; ownership data retained for retry.";
                await PersistSettingsAsync(CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                subscription = _settings.GuideSubscriptions.First(record => record.HeroId == hero.Id && record.Role == role);
                subscription.Status = GuideSubscriptionStatus.RemovalFailed;
                subscription.LastError = subscription.StatusMessage = ex.Message;
                await PersistSettingsAsync(CancellationToken.None);
                await _loggingService.LogAsync(LogLevelKind.Error, "Auto Guide removal failed; ownership metadata retained.", new { hero.Id, role, Error = ex.ToString() }, CancellationToken.None);
            }
            finally { SyncGuideSubscriptionStateFromSettings(); }
            return;
        }

        if (option is not null) option.IsBusy = true;
        subscription.Status = GuideSubscriptionStatus.Installing;
        subscription.StatusMessage = "Syncing Auto Guide through Steam RemoteStorage.";
        if (option is not null) ApplyGuideRoleOptionState(option, subscription);
        SyncGuideSubscriptionStateFromSettings();

        try
        {
            var result = await _guideSubscriptionService.SyncAsync(account, subscription, token);
            subscription = _settings.GuideSubscriptions.First(record => record.HeroId == hero.Id && record.Role == role);
            subscription.ProviderName = result.ProviderName ?? subscription.ProviderName;
            subscription.SourceStrategy = result.SourceStrategy ?? subscription.SourceStrategy;
            subscription.BuildTitle = result.BuildTitle ?? subscription.BuildTitle;
            subscription.PatchLabel = result.PatchLabel ?? subscription.PatchLabel;
            subscription.AvailableHash = result.CanonicalSourceHash ?? subscription.AvailableHash;
            if (subscription.ApplyVerifiedInstallation(result)) subscription.InstalledAccountId = account.AccountId;
            subscription.LastCheckedAt = DateTimeOffset.UtcNow;
            subscription.LastRetrievedAt = result.RetrievedAtUtc ?? subscription.LastRetrievedAt;
            subscription.Status = result.Status;
            subscription.StatusMessage = result.Message;
            subscription.LastError = result.Status is GuideSubscriptionStatus.Error or GuideSubscriptionStatus.MappingFailed or GuideSubscriptionStatus.SourceIncomplete or GuideSubscriptionStatus.SourcePatchIncompatible or GuideSubscriptionStatus.Conflict or GuideSubscriptionStatus.SourceUnavailable or GuideSubscriptionStatus.SteamUnavailable
                ? result.Message
                : null;


            if (option is not null) ApplyGuideRoleOptionState(option, subscription);
            SyncGuideSubscriptionStateFromSettings();
            await PersistSettingsAsync(token);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            subscription = _settings.GuideSubscriptions.First(record => record.HeroId == hero.Id && record.Role == role);
            subscription.Status = GuideSubscriptionStatus.Error;
            subscription.StatusMessage = ex.Message;
            subscription.LastError = ex.Message;
            if (option is not null) ApplyGuideRoleOptionState(option, subscription);
            SyncGuideSubscriptionStateFromSettings();
            await _loggingService.LogAsync(LogLevelKind.Warning, "Guide subscription sync failed from the UI workflow.", new
            {
                hero.Id,
                role,
                ex.Message
            }, CancellationToken.None);
            await PersistSettingsAsync(CancellationToken.None);
        }
        finally
        {
            if (option is not null) option.IsBusy = false;
            if (option is not null) ApplyGuideRoleOptionState(option, subscription);
            SyncGuideSubscriptionStateFromSettings();
        }
    }

    private void ApplyGuideRoleOptionState(GuideRoleOptionViewModel option, GuideSubscriptionRecord? subscription)
    {
        var enabled = subscription?.IsEnabled == true;
        var status = subscription?.Status ?? GuideSubscriptionStatus.Disabled;
        var key = SelectedGuideHero is null ? string.Empty : GuideSubscriptionRecord.CreateStableKey(SelectedGuideHero.Hero.Id, option.Role);
        option.IsBusy = _busyGuideKeys.Contains(key) || _pendingGuideToggleKeys.Contains(key);
        option.StatusChip = GuideStatusPresentation.Label(_text, enabled, status);
        option.StatusTone = GuideStatusPresentation.Tone(enabled, status, option.IsBusy);
        option.StatusDetail = subscription?.StatusMessage ?? option.StatusChip;
        option.HashDetail = subscription?.EffectiveGuideHash ?? string.Empty;
    }

    private string ToGuideRoleLabel(GuideRole role)
        => role switch
        {
            GuideRole.Carry => _text.T("Carry", "Керри"),
            GuideRole.Mid => _text.T("Mid", "Мид"),
            GuideRole.Offlane => _text.T("Offlane", "Оффлейн"),
            GuideRole.Support => _text.T("Support", "Саппорт"),
            GuideRole.HardSupport => _text.T("Hard Support", "Саппорт 5"),
            _ => role.ToString()
        };

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

        if (string.IsNullOrWhiteSpace(_settings.LastBaseSourceHash))
        {
            _settings.LastBaseSourceHash = cachedSnapshot.Hash;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(_settings.LastEffectiveGridHash))
        {
            _settings.LastEffectiveGridHash = _settings.LastRemoteHash ?? cachedSnapshot.Hash;
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

        var effectiveHash = _settings.LastEffectiveGridHash ?? _settings.LastRemoteHash;
        if (!string.IsNullOrWhiteSpace(effectiveHash)
            && string.Equals(effectiveHash, cachedSnapshot.Hash, StringComparison.OrdinalIgnoreCase))
        {
            _availableInstallSnapshot = cachedSnapshot;
        }
    }

    private async Task EnsureAvailableInstallSnapshotAsync()
    {
        var effectiveHash = _settings.LastEffectiveGridHash ?? _settings.LastRemoteHash;
        if (_availableInstallSnapshot is not null
            && !string.IsNullOrWhiteSpace(effectiveHash)
            && string.Equals(_availableInstallSnapshot.Hash, effectiveHash, StringComparison.OrdinalIgnoreCase))
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

    private async Task TryStartAppUpdateChecksAsync()
    {
        try
        {
            await StartAppUpdateChecksAsync();
        }
        catch (Exception ex)
        {
            await _loggingService.LogAsync(LogLevelKind.Warning, "App update startup orchestration failed during startup.", new { ex.Message }, _lifetimeCts.Token);
            AppUpdateStatusTitle = "App update checks unavailable";
            AppUpdateStatusText = "MetaGrid could not start the GitHub Releases update check loop.";
            AppUpdateChipText = "Failed";
        }
    }

    private Task StartAppUpdateChecksAsync()
    {
        if (!_settings.AutomaticallyCheckAppUpdates)
        {
            AppUpdateStatusTitle = "Automatic app updates disabled";
            AppUpdateStatusText = "MetaGrid will not check GitHub Releases automatically until you enable app update checks again.";
            AppUpdateChipText = "Disabled";
            return StopAppUpdateSchedulerAsync();
        }

        var nextAt = _settings.LastAppUpdateCheckAt?.AddHours(12);
        var initialDelay = nextAt is null || nextAt <= _appClock.Now
            ? TimeSpan.FromSeconds(45)
            : nextAt.Value - _appClock.Now;

        return ScheduleAppUpdateChecksAsync(initialDelay);
    }

    private Task ScheduleAppUpdateChecksAsync(TimeSpan initialDelay)
    {
        _appUpdateScheduleCts?.Cancel();
        _appUpdateScheduleCts?.Dispose();
        _appUpdateScheduleCts = null;
        _appUpdateSchedulerTask = null;

        if (!_settings.AutomaticallyCheckAppUpdates)
        {
            return Task.CompletedTask;
        }

        _appUpdateScheduleCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var token = _appUpdateScheduleCts.Token;
        _appUpdateSchedulerTask = Task.Run(() => RunAppUpdateSchedulerLoopAsync(initialDelay, token), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task RunAppUpdateSchedulerLoopAsync(TimeSpan initialDelay, CancellationToken token)
    {
        try
        {
            if (initialDelay > TimeSpan.Zero)
            {
                await _appClock.DelayAsync(initialDelay, token);
            }

            while (!token.IsCancellationRequested)
            {
                if (!IsBusy)
                {
                    await RunAppUpdateCheckAsync(manual: false);
                }

                await _appClock.DelayAsync(TimeSpan.FromHours(12), token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown or scheduler reconfiguration.
        }
    }

    private async Task StopAppUpdateSchedulerAsync()
    {
        if (_appUpdateScheduleCts is null)
        {
            return;
        }

        _appUpdateScheduleCts.Cancel();
        var schedulerTask = _appUpdateSchedulerTask;
        _appUpdateScheduleCts.Dispose();
        _appUpdateScheduleCts = null;
        _appUpdateSchedulerTask = null;

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

    private Task CheckAppUpdatesAsync() => RunAppUpdateCheckAsync(manual: true);

    private async Task RunAppUpdateCheckAsync(bool manual)
    {
        if (manual && IsBusy)
        {
            return;
        }

        var shouldManageBusy = manual;
        try
        {
            if (shouldManageBusy)
            {
                IsBusy = true;
            }

            var progress = new Progress<AppUpdateProgress>(update =>
            {
                AppUpdateStatusTitle = update.Phase switch
                {
                    AppUpdateProgressPhase.Checking => "Checking app updates",
                    AppUpdateProgressPhase.Downloading => "Downloading update",
                    AppUpdateProgressPhase.Verifying => "Verifying update",
                    AppUpdateProgressPhase.Extracting => "Preparing update",
                    AppUpdateProgressPhase.LaunchingUpdater => "Launching updater",
                    AppUpdateProgressPhase.Completed => "Update complete",
                    _ => "App update failed"
                };
                AppUpdateStatusText = update.Message;
                AppUpdateChipText = update.Phase == AppUpdateProgressPhase.Failed ? "Failed" : "Checking";
            });

            var updateInfo = await _appUpdateService.CheckForUpdatesAsync(_settings, manual, progress, _lifetimeCts.Token);
            ApplyAppUpdateInfo(updateInfo);
            await PersistSettingsAsync(_lifetimeCts.Token);

            if (manual)
            {
                if (updateInfo.State == AppUpdateCheckState.UpdateAvailable)
                {
                    _notificationService.ShowInfo("MetaGrid update available", $"MetaGrid {updateInfo.AvailableVersion} is ready to install.");
                }
                else if (updateInfo.State == AppUpdateCheckState.LatestInstalled)
                {
                    _notificationService.ShowSuccess("MetaGrid is up to date", $"You are already on MetaGrid {CurrentAppVersionText}.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _loggingService.LogAsync(LogLevelKind.Error, "MetaGrid app update check crashed unexpectedly.", new { ex.Message }, CancellationToken.None);
            AppUpdateStatusTitle = "App update check failed";
            AppUpdateStatusText = ex.Message;
            AppUpdateChipText = "Failed";
        }
        finally
        {
            if (shouldManageBusy)
            {
                IsBusy = false;
            }
        }
    }

    private async Task InstallAppUpdateAsync()
    {
        if (_latestAppUpdateInfo is null || !_latestAppUpdateInfo.IsUpdateAvailable)
        {
            await RunAppUpdateCheckAsync(manual: true);
            if (_latestAppUpdateInfo is null || !_latestAppUpdateInfo.IsUpdateAvailable)
            {
                return;
            }
        }

        try
        {
            IsBusy = true;
            var progress = new Progress<AppUpdateProgress>(update =>
            {
                AppUpdateStatusTitle = update.Phase switch
                {
                    AppUpdateProgressPhase.Downloading => "Downloading MetaGrid update",
                    AppUpdateProgressPhase.Verifying => "Verifying MetaGrid update",
                    AppUpdateProgressPhase.Extracting => "Preparing MetaGrid update",
                    AppUpdateProgressPhase.LaunchingUpdater => "Starting MetaGrid updater",
                    _ => "Preparing MetaGrid update"
                };
                AppUpdateStatusText = update.Message;
                AppUpdateChipText = "Checking";
            });

            var launchResult = await _appUpdateService.PrepareAndLaunchUpdateAsync(_settings, _latestAppUpdateInfo, progress, _lifetimeCts.Token);
            AppUpdateStatusTitle = launchResult.State == AppUpdateInstallState.Started ? "MetaGrid updater started" : "MetaGrid update failed";
            AppUpdateStatusText = launchResult.Message;
            AppUpdateChipText = launchResult.State == AppUpdateInstallState.Started ? "Updating" : "Failed";

            if (launchResult.ShouldExitApplication)
            {
                await PersistSettingsAsync(_lifetimeCts.Token);
                System.Windows.Application.Current?.Shutdown();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _loggingService.LogAsync(LogLevelKind.Error, "MetaGrid app update launch failed unexpectedly.", new { ex.Message }, CancellationToken.None);
            AppUpdateStatusTitle = "MetaGrid update failed";
            AppUpdateStatusText = ex.Message;
            AppUpdateChipText = "Failed";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeferAppUpdateAsync()
    {
        if (_latestAppUpdateInfo?.AvailableVersion is null)
        {
            return;
        }

        _settings.DeferredAppUpdateVersion = _latestAppUpdateInfo.AvailableVersion;
        _settings.DeferredAppUpdateUntil = _appClock.Now.AddHours(12);
        ApplyAppUpdateInfo(_latestAppUpdateInfo with
        {
            State = AppUpdateCheckState.Deferred,
            Message = $"MetaGrid {_latestAppUpdateInfo.AvailableVersion} will be shown again later.",
            IsUpdateAvailable = false,
            IsDeferred = true
        });
        await PersistSettingsAsync(_lifetimeCts.Token);
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

        NextCheckText = _text.T("Startup check pending", "Проверка при запуске ожидается");
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
            NextCheckText = _text.T("Automatic updates disabled", "Автообновления отключены");
            AutoUpdateChipText = "Auto Update Off";
            RaisePropertyChanged(nameof(OnboardingSummaryText));
            return Task.CompletedTask;
        }

        var interval = TimeSpan.FromMinutes((int)_settings.UpdateInterval);
        var nextRun = nextCheckAtOverride ?? AutomaticUpdateStartupPolicy.ComputeNextScheduledCheckAt(_appClock.Now, _settings.UpdateInterval);
        NextCheckText = FormatSchedulerTimestamp(nextRun);
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
                    NextCheckText = FormatSchedulerTimestamp(nextCheckAt);
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
                        LastCheckedText = FormatDashboardTimestamp(_appClock.Now);
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
            Accounts.Add(new AccountViewModel(account, _text, HandleAccountSelected));
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

        if (_settings.PersonalizationEnabled && _settings.PersonalizationAccountSourceMode == PersonalizationAccountSourceMode.SelectedSteamAccount)
        {
            await RefreshPersonalizationFromCurrentSelectionAsync(forceRefresh: false, notify: false);
        }
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

    private async Task ForceUpdateAsync()
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

            var result = await _updateService.ForceInstallLatestAsync(
                Accounts.Select(x => x.Model).ToList(),
                _settings,
                _lifetimeCts.Token);
            await ApplyUpdateResultAsync(result, rescheduleAfterUpdate: true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusTitle = "Update Check Failed";
            StatusText = "MetaGrid could not check Dota2ProTracker right now. Your installed grid was left unchanged.";
            ProviderStatusText = "Update check failed";
            ProviderStatusChipText = "Source error";
            StatusChipText = "Failed";
            await _loggingService.LogAsync(LogLevelKind.Error, "Force update failed unexpectedly.", new { ex.Message }, CancellationToken.None);
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
                    LastCheckedText = FormatDashboardTimestamp(_appClock.Now);
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
        candidateSettings.PersonalizationManualAccountId = candidateSettings.PersonalizationAccountSourceMode == PersonalizationAccountSourceMode.ManualAccount
            ? string.IsNullOrWhiteSpace(PersonalizationInput) ? null : PersonalizationInput.Trim()
            : null;
        candidateSettings.PersonalizationAccountId = candidateSettings.PersonalizationAccountSourceMode == PersonalizationAccountSourceMode.SelectedSteamAccount
            ? SelectedAccount?.AccountId
            : candidateSettings.PersonalizationManualAccountId;
        candidateSettings.SelectedAccountIds = Accounts.Where(x => x.IsSelected).Select(x => x.AccountId).ToList();
        candidateSettings.PreferredAccountId = candidateSettings.SelectedAccountIds.FirstOrDefault();
        candidateSettings.LastRoleSummary = RoleSummaryText;

        var steamSettingsChanged =
            _settings.AutomaticallyDetectSteam != candidateSettings.AutomaticallyDetectSteam ||
            !string.Equals(_settings.SteamDirectoryOverride, candidateSettings.SteamDirectoryOverride, StringComparison.Ordinal);
        var personalizationSettingsChanged =
            _settings.PersonalizationEnabled != candidateSettings.PersonalizationEnabled ||
            _settings.PersonalizationAccountSourceMode != candidateSettings.PersonalizationAccountSourceMode ||
            !string.Equals(_settings.PersonalizationManualAccountId, candidateSettings.PersonalizationManualAccountId, StringComparison.Ordinal) ||
            !string.Equals(_settings.PersonalizationAccountId, candidateSettings.PersonalizationAccountId, StringComparison.Ordinal);
        var appUpdateSettingsChanged =
            _settings.AutomaticallyCheckAppUpdates != candidateSettings.AutomaticallyCheckAppUpdates;

        try
        {
            await _settingsService.SaveAsync(candidateSettings, _lifetimeCts.Token);
            _startupService.ApplyLaunchAtStartup(candidateSettings.LaunchWithWindows);
            _settings = candidateSettings;
            RaisePropertyChanged(nameof(Settings));
            ResetEditableSettingsBaseline();
            AutoUpdateChipText = _settings.AutoUpdateEnabled ? "Auto Update On" : "Auto Update Off";
            await ScheduleAsync();
            if (appUpdateSettingsChanged)
            {
                await StartAppUpdateChecksAsync();
                ApplyAppUpdateStateFromSettings();
            }

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

            if (personalizationSettingsChanged)
            {
                await RefreshPersonalizationFromCurrentSelectionAsync(forceRefresh: false, notify: false);
                ResetEditableSettingsBaseline();
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
            await RefreshSelectedInstalledStateAsync(selected.Model, targetPath);
            var restoredHash = selected.Model.CurrentMetaGridHash;
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
        CheckAppUpdatesCommand.NotifyCanExecuteChanged();
        InstallAppUpdateCommand.NotifyCanExecuteChanged();
        LaterAppUpdateCommand.NotifyCanExecuteChanged();
        DetectSteamCommand.NotifyCanExecuteChanged();
        SaveSettingsCommand.NotifyCanExecuteChanged();
        ClearHistoryCommand.NotifyCanExecuteChanged();
        RestoreBackupCommand.NotifyCanExecuteChanged();
        ConnectPersonalizationCommand.NotifyCanExecuteChanged();
        DisconnectPersonalizationCommand.NotifyCanExecuteChanged();
        RefreshPersonalHeroesCommand.NotifyCanExecuteChanged();
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
        if (_settings.PersonalizationEnabled && _settings.PersonalizationAccountSourceMode == PersonalizationAccountSourceMode.SelectedSteamAccount)
        {
            _ = RefreshPersonalizationFromCurrentSelectionAsync(forceRefresh: false, notify: false);
        }
    }

    private void UpdateSelectedAccountUi()
    {
        var selected = Accounts.FirstOrDefault(x => x.IsSelected);
        SelectedAccountText = selected?.DisplayName ?? "No account selected";
        SelectedAccountPathText = selected?.ConfigPath ?? "Choose a Dota 2 account to see its configuration path.";
        SyncSelectedInstalledStateToSettings();
        InstalledGridHashText = selected is null
            ? "Not installed"
            : ToShortHash(selected.Model.CurrentMetaGridHash) ?? "Not installed";
        RaisePropertyChanged(nameof(OnboardingSelectedAccountLabel));
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
            parts.Add(_text.T(
                $"Last updated {FormatInlineTimestamp(capturedAt)}",
                $"Обновлено {FormatInlineTimestamp(capturedAt)}"));
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

    private void ApplyAppUpdateStateFromSettings()
    {
        var state = ParseAppUpdateState(_settings.LastAppUpdateState);
        var availableVersion = string.IsNullOrWhiteSpace(_settings.LastAvailableAppVersion)
            ? "Unknown"
            : _settings.LastAvailableAppVersion;

        AppUpdateAvailableVersionText = availableVersion;
        AppUpdateLastCheckedText = FormatDashboardTimestamp(_settings.LastAppUpdateCheckAt);

        _latestAppUpdateInfo = state is AppUpdateCheckState.UpdateAvailable or AppUpdateCheckState.Deferred
            ? new AppUpdateInfo(
                CurrentAppVersionText,
                _settings.LastAvailableAppVersion,
                state,
                _settings.LastAppUpdateMessage ?? string.Empty,
                _settings.LastAppUpdateCheckAt ?? _appClock.Now,
                state == AppUpdateCheckState.UpdateAvailable,
                state == AppUpdateCheckState.Deferred)
            : null;

        if (!_settings.AutomaticallyCheckAppUpdates && state is not AppUpdateCheckState.UpdateAvailable and not AppUpdateCheckState.Deferred)
        {
            AppUpdateStatusTitle = "Automatic app updates disabled";
            AppUpdateStatusText = "MetaGrid will not check GitHub Releases automatically until you enable app update checks again.";
            AppUpdateChipText = "Disabled";
            RaisePropertyChanged(nameof(ShowAppUpdateBanner));
            RaisePropertyChanged(nameof(HasAppUpdateAvailable));
            RaisePropertyChanged(nameof(HasDeferredAppUpdate));
            RaisePropertyChanged(nameof(AppUpdateBannerTitle));
            RaisePropertyChanged(nameof(AppUpdateBannerText));
            NotifyCommandStates();
            return;
        }

        var (title, message, chip) = BuildAppUpdatePresentation(
            state,
            CurrentAppVersionText,
            _settings.LastAvailableAppVersion,
            _settings.LastAppUpdateMessage,
            _settings.DeferredAppUpdateUntil);

        AppUpdateStatusTitle = title;
        AppUpdateStatusText = message;
        AppUpdateChipText = chip;
        RaisePropertyChanged(nameof(ShowAppUpdateBanner));
        RaisePropertyChanged(nameof(HasAppUpdateAvailable));
        RaisePropertyChanged(nameof(HasDeferredAppUpdate));
        RaisePropertyChanged(nameof(AppUpdateBannerTitle));
        RaisePropertyChanged(nameof(AppUpdateBannerText));
        NotifyCommandStates();
    }

    private void ApplyAppUpdateInfo(AppUpdateInfo updateInfo)
    {
        _latestAppUpdateInfo = updateInfo;
        _settings.LastAppUpdateCheckAt = updateInfo.CheckedAt;
        _settings.LastAvailableAppVersion = updateInfo.AvailableVersion;
        _settings.LastAppUpdateState = updateInfo.State.ToString();
        _settings.LastAppUpdateMessage = updateInfo.Message;

        if (updateInfo.State == AppUpdateCheckState.LatestInstalled)
        {
            _settings.DeferredAppUpdateVersion = null;
            _settings.DeferredAppUpdateUntil = null;
        }
        else if (updateInfo.State == AppUpdateCheckState.UpdateAvailable)
        {
            if (!string.Equals(_settings.DeferredAppUpdateVersion, updateInfo.AvailableVersion, StringComparison.OrdinalIgnoreCase))
            {
                _settings.DeferredAppUpdateVersion = null;
                _settings.DeferredAppUpdateUntil = null;
            }
        }

        AppUpdateAvailableVersionText = updateInfo.AvailableVersion ?? "Unknown";
        AppUpdateLastCheckedText = FormatDashboardTimestamp(updateInfo.CheckedAt);

        var (title, message, chip) = BuildAppUpdatePresentation(
            updateInfo.State,
            updateInfo.CurrentVersion,
            updateInfo.AvailableVersion,
            updateInfo.Message,
            _settings.DeferredAppUpdateUntil);

        AppUpdateStatusTitle = title;
        AppUpdateStatusText = message;
        AppUpdateChipText = chip;
        RaisePropertyChanged(nameof(ShowAppUpdateBanner));
        RaisePropertyChanged(nameof(HasAppUpdateAvailable));
        RaisePropertyChanged(nameof(HasDeferredAppUpdate));
        RaisePropertyChanged(nameof(AppUpdateBannerTitle));
        RaisePropertyChanged(nameof(AppUpdateBannerText));
        NotifyCommandStates();
    }

    private static AppUpdateCheckState ParseAppUpdateState(string? value)
        => Enum.TryParse<AppUpdateCheckState>(value, ignoreCase: true, out var parsed)
            ? parsed
            : AppUpdateCheckState.Unknown;

    private (string Title, string Message, string Chip) BuildAppUpdatePresentation(
        AppUpdateCheckState state,
        string currentVersion,
        string? availableVersion,
        string? message,
        DateTimeOffset? deferredUntil)
        => state switch
        {
            AppUpdateCheckState.UpdateAvailable => (
                "MetaGrid update available",
                string.IsNullOrWhiteSpace(message) ? $"MetaGrid {availableVersion} is available. You are currently on {currentVersion}." : message,
                "Update available"),
            AppUpdateCheckState.LatestInstalled => (
                "MetaGrid is up to date",
                string.IsNullOrWhiteSpace(message) ? $"MetaGrid {currentVersion} matches the latest GitHub Release." : message,
                "Up to date"),
            AppUpdateCheckState.Deferred => (
                "MetaGrid update deferred",
                deferredUntil is null
                    ? (string.IsNullOrWhiteSpace(message) ? $"MetaGrid {availableVersion} is available and will be shown again later." : message)
                    : (string.IsNullOrWhiteSpace(message) ? $"MetaGrid {availableVersion} was deferred until {FormatInlineTimestamp(deferredUntil.Value)}." : message),
                "Deferred"),
            AppUpdateCheckState.Checking => (
                "Checking app updates",
                "MetaGrid is checking GitHub Releases for a newer application build.",
                "Checking"),
            AppUpdateCheckState.RateLimited => (
                "GitHub rate limit reached",
                string.IsNullOrWhiteSpace(message) ? "GitHub temporarily rate-limited MetaGrid update checks. Try again later." : message,
                "Rate limited"),
            AppUpdateCheckState.Unavailable => (
                "App update source unavailable",
                string.IsNullOrWhiteSpace(message) ? "MetaGrid could not reach GitHub Releases right now." : message,
                "Unavailable"),
            AppUpdateCheckState.InvalidRelease => (
                "Release package unavailable",
                string.IsNullOrWhiteSpace(message) ? "No valid stable MetaGrid release package was available to install." : message,
                "Invalid release"),
            AppUpdateCheckState.Failed => (
                "App update check failed",
                string.IsNullOrWhiteSpace(message) ? "MetaGrid hit an unexpected app update error." : message,
                "Failed"),
            _ => (
                "App updates not checked yet",
                "MetaGrid can check GitHub Releases for newer application builds separately from Dota hero-grid updates.",
                "Not checked")
        };

    private string FormatInterval(UpdateInterval interval)
        => interval switch
        {
            UpdateInterval.FifteenMinutes => _text.T("15 minutes", "15 минут"),
            UpdateInterval.ThirtyMinutes => _text.T("30 minutes", "30 минут"),
            UpdateInterval.OneHour => _text.T("1 hour", "1 час"),
            UpdateInterval.ThreeHours => _text.T("3 hours", "3 часа"),
            UpdateInterval.SixHours => _text.T("6 hours", "6 часов"),
            UpdateInterval.TwelveHours => _text.T("12 hours", "12 часов"),
            UpdateInterval.TwentyFourHours => _text.T("24 hours", "24 часа"),
            _ => _text.T($"{(int)interval} minutes", $"{(int)interval} минут")
        };

    private void RefreshLocalizedBindings()
    {
        SyncGuideSubscriptionStateFromSettings();
        UpdateIntervalOptions =
        [
            new UpdateIntervalOption(UpdateInterval.FifteenMinutes, FormatInterval(UpdateInterval.FifteenMinutes)),
            new UpdateIntervalOption(UpdateInterval.ThirtyMinutes, FormatInterval(UpdateInterval.ThirtyMinutes)),
            new UpdateIntervalOption(UpdateInterval.OneHour, FormatInterval(UpdateInterval.OneHour)),
            new UpdateIntervalOption(UpdateInterval.ThreeHours, FormatInterval(UpdateInterval.ThreeHours)),
            new UpdateIntervalOption(UpdateInterval.SixHours, FormatInterval(UpdateInterval.SixHours)),
            new UpdateIntervalOption(UpdateInterval.TwelveHours, FormatInterval(UpdateInterval.TwelveHours)),
            new UpdateIntervalOption(UpdateInterval.TwentyFourHours, FormatInterval(UpdateInterval.TwentyFourHours))
        ];
        LastCheckedText = FormatDashboardTimestamp(_settings.LastCheckAt);
        LastUpdatedText = FormatDashboardTimestamp(_settings.LastSuccessfulUpdateAt);
        PersonalizationLastRefreshText = FormatDashboardTimestamp(_settings.LastSuccessfulPersonalStatsRefreshAt);
        CurrentSourceText = BuildSourceStatusText();
        if (!_settings.AutoUpdateEnabled)
        {
            NextCheckText = _text.T("Automatic updates disabled", "Автообновления отключены");
        }
        else if (_settings.LastCheckAt is not null || _settings.LastSuccessfulLiveProviderCheckAt is not null)
        {
            NextCheckText = FormatSchedulerTimestamp(AutomaticUpdateStartupPolicy.ComputeNextScheduledCheckAt(_appClock.Now, _settings.UpdateInterval));
        }

        RaisePropertyChanged(nameof(UpdateIntervalOptions));
        RaisePropertyChanged(nameof(StatusTitle));
        RaisePropertyChanged(nameof(StatusText));
        RaisePropertyChanged(nameof(AppUpdateStatusTitle));
        RaisePropertyChanged(nameof(AppUpdateStatusText));
        RaisePropertyChanged(nameof(AppUpdateChipText));
        RaisePropertyChanged(nameof(AppUpdateLastCheckedText));
        RaisePropertyChanged(nameof(AppUpdateAvailableVersionText));
        RaisePropertyChanged(nameof(CurrentAppVersionText));
        RaisePropertyChanged(nameof(ProviderStatusText));
        RaisePropertyChanged(nameof(ProviderCardTitle));
        RaisePropertyChanged(nameof(SteamDetectionStatusText));
        RaisePropertyChanged(nameof(SteamDetectionPathText));
        RaisePropertyChanged(nameof(CurrentSourceText));
        RaisePropertyChanged(nameof(GridCriteriaText));
        RaisePropertyChanged(nameof(TopFivePreviewText));
        RaisePropertyChanged(nameof(StatusChipText));
        RaisePropertyChanged(nameof(AutoUpdateChipText));
        RaisePropertyChanged(nameof(ProviderStatusChipText));
        RaisePropertyChanged(nameof(PersonalizationStatusText));
        RaisePropertyChanged(nameof(PersonalizationSourceText));
        RaisePropertyChanged(nameof(PersonalizationDetailsText));
        RaisePropertyChanged(nameof(PersonalizationConnectedAccountText));
        RaisePropertyChanged(nameof(PersonalizationLastRefreshText));
        RaisePropertyChanged(nameof(SettingsSaveStateText));
        RaisePropertyChanged(nameof(CurrentGridPreviewTitle));
        RaisePropertyChanged(nameof(CurrentGridPreviewSubtitle));
        RaisePropertyChanged(nameof(OnboardingTitle));
        RaisePropertyChanged(nameof(OnboardingDescription));
        RaisePropertyChanged(nameof(OnboardingPrimaryLabel));
        RaisePropertyChanged(nameof(OnboardingStepLabel));
        RaisePropertyChanged(nameof(OnboardingSelectedAccountLabel));
        RaisePropertyChanged(nameof(OnboardingAccountsHintText));
        RaisePropertyChanged(nameof(OnboardingAutoUpdateHelpText));
        RaisePropertyChanged(nameof(OnboardingSummaryText));
        RaisePropertyChanged(nameof(ShowAppUpdateBanner));
        RaisePropertyChanged(nameof(HasAppUpdateAvailable));
        RaisePropertyChanged(nameof(HasDeferredAppUpdate));
        RaisePropertyChanged(nameof(AppUpdateBannerTitle));
        RaisePropertyChanged(nameof(AppUpdateBannerText));
        RaisePropertyChanged(nameof(CheckAppUpdatesLabel));
        RaisePropertyChanged(nameof(UpdateNowLabel));
        RaisePropertyChanged(nameof(LaterLabel));
    }

    private string FormatDashboardTimestamp(DateTimeOffset? value)
        => value is null
            ? _text.T("Never", "Никогда")
            : FormatTimestamp(value.Value.ToLocalTime(), includeLineBreak: true);

    private string FormatInlineTimestamp(DateTimeOffset value)
        => FormatTimestamp(value.ToLocalTime(), includeLineBreak: false);

    private string FormatSchedulerTimestamp(DateTimeOffset? value)
        => value is null
            ? _text.T("Not scheduled", "Не запланировано")
            : FormatTimestamp(value.Value.ToLocalTime(), includeLineBreak: true);

    private string FormatTimestamp(DateTimeOffset localValue, bool includeLineBreak)
    {
        var culture = _text.Language == AppLanguage.Russian
            ? CultureInfo.GetCultureInfo("ru-RU")
            : CultureInfo.GetCultureInfo("en-US");
        var separator = includeLineBreak ? Environment.NewLine : " ";
        var format = _text.Language == AppLanguage.Russian
            ? $"dd.MM.yyyy{separator}HH:mm"
            : $"MMM d, yyyy{separator}h:mm tt";
        return localValue.ToString(format, culture);
    }

    private static string? ToShortHash(string? hash)
        => string.IsNullOrWhiteSpace(hash) ? null : hash[..Math.Min(12, hash.Length)];

    private string BuildGridCriteriaText()
        => _settings.PersonalizationEnabled && GetCurrentPersonalizationAccountContext(_settings).HasAccount
            ? "Official Dota2ProTracker High Winrate grid + optional MY BEST HEROES row"
            : "Official Dota2ProTracker High Winrate grid";

    private static MediaBrush CreateBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private void ApplyPersonalizationStateFromSettings()
    {
        var status = ParsePersonalizationStatus(_settings.LastPersonalizationStatus);
        var sourceMode = _settings.PersonalizationAccountSourceMode;
        var selectedAccount = SelectedAccount;
        PersonalizationSourceText = sourceMode == PersonalizationAccountSourceMode.ManualAccount
            ? "Manual OpenDota account"
            : "Selected Steam account";
        PersonalizationStatusText = status switch
        {
            PersonalizationStatus.Ready => "Personal heroes ready",
            PersonalizationStatus.NoQualifyingHeroes => "No qualifying heroes in last 90 days",
            PersonalizationStatus.ProfileUnavailable => "Profile private or match data unavailable",
            PersonalizationStatus.Cached => "Using cached personal heroes",
            PersonalizationStatus.OpenDotaUnavailable => "OpenDota unavailable",
            PersonalizationStatus.InvalidInput => "OpenDota account required",
            _ => "Personal heroes off"
        };

        PersonalizationDetailsText = string.IsNullOrWhiteSpace(_settings.LastPersonalizationMessage)
            ? sourceMode == PersonalizationAccountSourceMode.ManualAccount
                ? "Enter another OpenDota account only when you want to override the selected Steam account."
                : "OpenDota automatically uses your selected Steam account to add MY BEST HEROES to the All Roles layout."
            : _settings.LastPersonalizationMessage;
        PersonalizationConnectedAccountText = sourceMode == PersonalizationAccountSourceMode.SelectedSteamAccount
            ? selectedAccount is null
                ? "Select a Steam account to resolve OpenDota personalization"
                : $"{selectedAccount.DisplayName} ({selectedAccount.AccountId})"
            : string.IsNullOrWhiteSpace(_settings.PersonalizationAccountId)
                ? "No OpenDota account resolved yet"
                : string.IsNullOrWhiteSpace(_settings.LastPersonalizationAccountDisplayName)
                    ? _settings.PersonalizationAccountId
                    : $"{_settings.LastPersonalizationAccountDisplayName} ({_settings.PersonalizationAccountId})";
        PersonalizationLastRefreshText = FormatDashboardTimestamp(_settings.LastSuccessfulPersonalStatsRefreshAt);
    }

    private async Task TryLoadPersonalHeroPreviewAsync()
    {
        PersonalHeroPreviewItems.Clear();
        RaisePropertyChanged(nameof(HasPersonalHeroPreview));

        if (string.IsNullOrWhiteSpace(_settings.PersonalizationAccountId))
        {
            return;
        }

        var cache = await _personalHeroCacheService.LoadAsync(_settings.PersonalizationAccountId, _lifetimeCts.Token);
        if (cache is null)
        {
            return;
        }

        foreach (var hero in cache.SelectedHeroes)
        {
            PersonalHeroPreviewItems.Add(new PersonalHeroPreviewItem(hero.HeroName, $"{hero.WinRate:P1} WR - {hero.Games} matches"));
        }

        RaisePropertyChanged(nameof(HasPersonalHeroPreview));
    }

    private void ApplyPersonalizationResolution(PersonalizationResolution resolution)
    {
        _settings.PersonalizationAccountId = resolution.AccountId;
        _editableSettings.PersonalizationAccountId = resolution.AccountId;

        _settings.LastPersonalizationStatus = resolution.Status.ToString();
        _settings.LastPersonalizationMessage = resolution.Message;
        _settings.LastPersonalizationAccountDisplayName = resolution.DisplayName;
        if (resolution.Status is PersonalizationStatus.Ready or PersonalizationStatus.NoQualifyingHeroes or PersonalizationStatus.Cached)
        {
            _settings.LastSuccessfulPersonalStatsRefreshAt = resolution.FetchedAt ?? _appClock.Now;
        }

        _editableSettings.LastPersonalizationStatus = _settings.LastPersonalizationStatus;
        _editableSettings.LastPersonalizationMessage = _settings.LastPersonalizationMessage;
        _editableSettings.LastPersonalizationAccountDisplayName = _settings.LastPersonalizationAccountDisplayName;
        _editableSettings.LastSuccessfulPersonalStatsRefreshAt = _settings.LastSuccessfulPersonalStatsRefreshAt;
        ApplyPersonalizationStateFromSettings();

        PersonalHeroPreviewItems.Clear();
        foreach (var hero in resolution.Selection?.SelectedHeroes ?? [])
        {
            PersonalHeroPreviewItems.Add(new PersonalHeroPreviewItem(hero.HeroName, $"{hero.WinRate:P1} WR - {hero.Games} matches"));
        }

        RaisePropertyChanged(nameof(HasPersonalHeroPreview));
        GridCriteriaText = BuildGridCriteriaText();
    }

    private PersonalizationAccountContext GetCurrentPersonalizationAccountContext(AppSettings settings)
        => settings.PersonalizationAccountSourceMode == PersonalizationAccountSourceMode.ManualAccount
            ? new PersonalizationAccountContext
            {
                SourceMode = PersonalizationAccountSourceMode.ManualAccount,
                AccountId = settings.PersonalizationManualAccountId ?? settings.PersonalizationAccountId
            }
            : new PersonalizationAccountContext
            {
                SourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount,
                AccountId = SelectedAccount?.AccountId,
                DisplayName = SelectedAccount?.DisplayName
            };

    private async Task RecomposeAvailableSnapshotAsync(PersonalizationResolution resolution)
    {
        var cachedBaseSnapshot = await _gridSnapshotCacheService.LoadAsync(_lifetimeCts.Token);
        if (cachedBaseSnapshot is null || ParseOrigin(_settings.LastGridOrigin) == GridOriginKind.Cached)
        {
            return;
        }

        var effective = _personalizedGridComposer.Compose(cachedBaseSnapshot, resolution);
        _availableInstallSnapshot = effective;
        _settings.LastBaseSourceHash = cachedBaseSnapshot.Hash;
        _settings.LastEffectiveGridHash = effective.Hash;
        _settings.LastRemoteHash = effective.Hash;
        _settings.LastCachedHash = effective.Hash;
        AvailableGridHashText = ToShortHash(effective.Hash) ?? "Unknown";
        CachedGridHashText = ToShortHash(effective.Hash) ?? "Not cached";
        BaseGridHashText = ToShortHash(cachedBaseSnapshot.Hash) ?? "Unknown";
        TopFivePreviewText = BuildTopFivePreview(effective);
        RoleSummaryText = BuildRoleSummary(effective);
        await UpdateGridPreviewAsync(effective);
        RefreshOverviewState();
    }

    private async Task RefreshPersonalizationFromCurrentSelectionAsync(bool forceRefresh, bool notify)
    {
        if (!_settings.PersonalizationEnabled)
        {
            return;
        }

        try
        {
            var resolution = forceRefresh
                ? await _personalizationService.RefreshAsync(_settings, GetCurrentPersonalizationAccountContext(_settings), _lifetimeCts.Token)
                : await _personalizationService.ResolveAsync(_settings, GetCurrentPersonalizationAccountContext(_settings), forceRefresh: false, _lifetimeCts.Token);
            ApplyPersonalizationResolution(resolution);
            await RecomposeAvailableSnapshotAsync(resolution);
            await PersistSettingsAsync(_lifetimeCts.Token);
            if (notify)
            {
                _notificationService.ShowInfo("Personal heroes refreshed", resolution.Message);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _loggingService.LogAsync(LogLevelKind.Warning, "Automatic personalization refresh failed.", new { ex.Message }, CancellationToken.None);
        }
    }

    private async Task ConnectPersonalizationAsync()
    {
        if (string.IsNullOrWhiteSpace(PersonalizationInput))
        {
            PersonalizationStatusText = "Profile input invalid";
            PersonalizationDetailsText = "Enter a numeric account ID or a supported OpenDota/Dotabuff player URL.";
            return;
        }

        IsBusy = true;
        try
        {
            var resolution = await _personalizationService.ConnectAsync(PersonalizationInput, _lifetimeCts.Token);
            if (resolution.Status == PersonalizationStatus.InvalidInput)
            {
                PersonalizationStatusText = "Profile input invalid";
                PersonalizationDetailsText = resolution.Message;
                return;
            }

            _settings.PersonalizationEnabled = true;
            _editableSettings.PersonalizationEnabled = true;
            _settings.PersonalizationAccountSourceMode = PersonalizationAccountSourceMode.ManualAccount;
            _editableSettings.PersonalizationAccountSourceMode = PersonalizationAccountSourceMode.ManualAccount;
            _settings.PersonalizationManualAccountId = resolution.AccountId;
            _editableSettings.PersonalizationManualAccountId = resolution.AccountId;
            PersonalizationInput = resolution.AccountId ?? PersonalizationInput;
            ApplyPersonalizationResolution(resolution);
            await RecomposeAvailableSnapshotAsync(resolution);
            await PersistSettingsAsync(_lifetimeCts.Token);
            ResetEditableSettingsBaseline();
            _notificationService.ShowSuccess("Personal heroes connected", "MetaGrid connected the OpenDota personalization profile.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _loggingService.LogAsync(LogLevelKind.Error, "OpenDota personalization connect failed.", new { ex.Message }, CancellationToken.None);
            PersonalizationStatusText = "OpenDota unavailable";
            PersonalizationDetailsText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshPersonalHeroesAsync()
    {
        IsBusy = true;
        try
        {
            var resolution = await _personalizationService.RefreshAsync(_settings, GetCurrentPersonalizationAccountContext(_settings), _lifetimeCts.Token);
            ApplyPersonalizationResolution(resolution);
            await RecomposeAvailableSnapshotAsync(resolution);
            await PersistSettingsAsync(_lifetimeCts.Token);
            _notificationService.ShowInfo("Personal heroes refreshed", resolution.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DisconnectPersonalizationAsync()
    {
        IsBusy = true;
        try
        {
            await _personalizationService.DisconnectAsync(_settings, _lifetimeCts.Token);
            _settings.PersonalizationEnabled = false;
            _settings.PersonalizationAccountId = null;
            _settings.PersonalizationManualAccountId = null;
            _settings.PersonalizationAccountSourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount;
            _settings.LastPersonalizationStatus = PersonalizationStatus.Disabled.ToString();
            _settings.LastPersonalizationMessage = "Personal heroes are disabled.";
            _settings.LastPersonalizationAccountDisplayName = null;
            _editableSettings.PersonalizationEnabled = false;
            _editableSettings.PersonalizationAccountId = null;
            _editableSettings.PersonalizationManualAccountId = null;
            _editableSettings.PersonalizationAccountSourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount;
            PersonalizationInput = string.Empty;
            ApplyPersonalizationStateFromSettings();
            PersonalHeroPreviewItems.Clear();
            RaisePropertyChanged(nameof(HasPersonalHeroPreview));
            GridCriteriaText = BuildGridCriteriaText();
            await RecomposeAvailableSnapshotAsync(new PersonalizationResolution
            {
                Status = PersonalizationStatus.Disabled,
                Message = "Personal heroes are disabled."
            });
            await PersistSettingsAsync(_lifetimeCts.Token);
            ResetEditableSettingsBaseline();
        }
        finally
        {
            IsBusy = false;
        }
    }

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
            settings.AutomaticallyCheckAppUpdates,
            settings.UpdateInterval,
            Math.Clamp(settings.BackupRetentionCount, 1, 50),
            settings.AutomaticallyDetectSteam,
            settings.SteamDirectoryOverride?.Trim() ?? string.Empty,
            settings.PersonalizationEnabled,
            settings.PersonalizationAccountSourceMode,
            settings.PersonalizationManualAccountId?.Trim() ?? string.Empty);

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
                    : $"{sourceName ?? "Provider"} Online",
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
                    : $"{sourceName ?? "Provider"} Online",
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
                    : $"{sourceName ?? "Provider"} Online",
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

    private static PersonalizationStatus ParsePersonalizationStatus(string? value)
        => Enum.TryParse<PersonalizationStatus>(value, out var status) ? status : PersonalizationStatus.Disabled;

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
        var iconCatalog = _heroDefinitionsById;
        var layouts = snapshot.Layouts
            .Select(layout => new HeroGridLayoutPreview(
                layout.Name,
                layout.Categories
                    .Select((category, index) => new HeroGridCategoryPreview(
                        $"{layout.Name}:{index}",
                        index,
                        category.Name,
                        category.HeroIds.Count,
                        BuildHeroPreviewText(category.HeroIds, heroNamesById),
                        category.HeroIds.Select(id => HeroIconPresentation.Create(id, iconCatalog)).ToArray()))
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
            _heroDefinitionsById = heroesByName.Values.GroupBy(hero => hero.Id)
                .ToDictionary(group => group.Key, group => group.First());
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
            LastCheckedText = FormatDashboardTimestamp(_settings.LastCheckAt);
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

        if (!string.IsNullOrWhiteSpace(result.BaseSourceHash))
        {
            _settings.LastBaseSourceHash = result.BaseSourceHash;
            BaseGridHashText = ToShortHash(result.BaseSourceHash) ?? BaseGridHashText;
        }

        if (!string.IsNullOrWhiteSpace(result.EffectiveGridHash))
        {
            _settings.LastEffectiveGridHash = result.EffectiveGridHash;
            _settings.LastRemoteHash = result.EffectiveGridHash;
            AvailableGridHashText = ToShortHash(result.EffectiveGridHash) ?? AvailableGridHashText;
        }

        _settings.LastProviderStatus = result.ProviderStatus.ToString();
        _settings.LastSourceName = result.SourceName ?? _settings.LastSourceName;
        _settings.LastSourceStrategy = result.SourceStrategy ?? _settings.LastSourceStrategy;
        _settings.LastSourceDetails = result.ProviderMessage ?? _settings.LastSourceDetails;
        _settings.LastGridOrigin = result.OriginKind?.ToString() ?? _settings.LastGridOrigin;
        _settings.LastGridCapturedAt = result.RetrievedAt ?? _settings.LastGridCapturedAt;
        _settings.LastPersonalizationStatus = result.PersonalizationStatus ?? _settings.LastPersonalizationStatus;
        _settings.LastPersonalizationMessage = result.PersonalizationMessage ?? _settings.LastPersonalizationMessage;
        _settings.LastPersonalizationAccountDisplayName = result.PersonalizationAccountDisplayName ?? _settings.LastPersonalizationAccountDisplayName;
        if (!string.IsNullOrWhiteSpace(result.PersonalizationAccountId))
        {
            _settings.PersonalizationAccountId = result.PersonalizationAccountId;
        }

        if (result.PersonalizationStatus is not null)
        {
            ApplyPersonalizationStateFromSettings();
        }

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
        await TryLoadPersonalHeroPreviewAsync();

        if (result.Status == UpdateStatus.Updated && !string.IsNullOrWhiteSpace(result.InstalledHash))
        {
            _settings.LastInstalledHash = result.InstalledHash;
            _settings.LastSuccessfulUpdateAt = _appClock.Now;
            InstalledGridHashText = ToShortHash(result.InstalledHash) ?? "Not installed";
            LastUpdatedText = FormatDashboardTimestamp(_settings.LastSuccessfulUpdateAt);
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
        GridCriteriaText = BuildGridCriteriaText();
        var statusPresentation = HeroGridStatusPresenter.CreateFromResult(result);

        await PersistSettingsAsync(_lifetimeCts.Token);
        await LoadHistoryAsync();

        if (rescheduleAfterUpdate)
        {
            await ScheduleAsync();
        }

        if (SelectedAccount is { } selectedAccount && !string.IsNullOrWhiteSpace(result.TargetPath))
        {
            await RefreshSelectedInstalledStateAsync(selectedAccount.Model, result.TargetPath);
        }

        UpdateSelectedAccountUi();
        RefreshOverviewState();
        ProviderStatusText = ToProviderStatusText(result.ProviderStatus, result.OriginKind, result.SourceName);
        ProviderStatusChipText = ToProviderChipText(result.ProviderStatus.ToString(), result.OriginKind, result.SourceName);
        ApplyOverviewPresentation(statusPresentation);
        ApplyProviderVisualState(result.ProviderStatus, result.OriginKind, result.SourceName);
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
        => !string.IsNullOrWhiteSpace(_settings.LastEffectiveGridHash ?? _settings.LastRemoteHash)
           && !string.IsNullOrWhiteSpace(GetSelectedInstalledHash())
           && string.Equals(_settings.LastEffectiveGridHash ?? _settings.LastRemoteHash, GetSelectedInstalledHash(), StringComparison.OrdinalIgnoreCase);

    public InstallGridPreview? BuildInstallPreview()
    {
        var selected = SelectedAccount;
        var effectiveHash = _settings.LastEffectiveGridHash ?? _settings.LastRemoteHash;
        if (selected is null
            || string.IsNullOrWhiteSpace(effectiveHash)
            || string.IsNullOrWhiteSpace(selected.Model.DotaConfigDirectory)
            || _availableInstallSnapshot is null
            || !string.Equals(_availableInstallSnapshot.Hash, effectiveHash, StringComparison.OrdinalIgnoreCase))
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
            BaseSourceHash = _settings.LastBaseSourceHash,
            EffectiveGridHash = effectiveHash,
            PersonalizationStatus = _settings.LastPersonalizationStatus,
            PersonalizationMessage = _settings.LastPersonalizationMessage,
            PersonalizationAccountId = _settings.PersonalizationAccountId,
            PersonalizationUsedCache = ParsePersonalizationStatus(_settings.LastPersonalizationStatus) == PersonalizationStatus.Cached,
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
            ToShortHash(effectiveHash) ?? effectiveHash,
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
            _availableInstallSnapshot is not null && !string.IsNullOrWhiteSpace(_settings.LastEffectiveGridHash ?? _settings.LastRemoteHash)
                && string.Equals(_availableInstallSnapshot.Hash, _settings.LastEffectiveGridHash ?? _settings.LastRemoteHash, StringComparison.OrdinalIgnoreCase),
            _settings.LastEffectiveGridHash ?? _settings.LastRemoteHash,
            GetSelectedInstalledHash(),
            ParseOrigin(_settings.LastGridOrigin));

    private void RefreshOverviewState()
    {
        SyncSelectedInstalledStateToSettings();
        var presentation = HeroGridStatusPresenter.CreateOverview(
            _settings,
            Accounts.Count > 0,
            GetSelectedInstalledState(),
            GetSelectedInstalledHash());
        ApplyOverviewPresentation(presentation);
        ApplyProviderVisualState(_settings.LastProviderStatus, ParseOrigin(_settings.LastGridOrigin), _settings.LastSourceName);
        RaiseInstallActionState();
    }

    private void ApplyOverviewPresentation(HeroGridStatusPresentation presentation)
    {
        _overviewState = presentation.State;
        StatusTitle = presentation.Title;
        StatusText = presentation.Description;
        StatusChipText = presentation.Chip;

        switch (presentation.State)
        {
            case HeroGridOverviewState.GridInstalled:
                StatusChipBackgroundBrush = CreateBrush("#1D2A22");
                StatusChipBorderBrush = CreateBrush("#355340");
                break;
            case HeroGridOverviewState.UpdateAvailable:
            case HeroGridOverviewState.GridReadyToInstall:
            case HeroGridOverviewState.CachedData:
                StatusChipBackgroundBrush = CreateBrush("#2C2417");
                StatusChipBorderBrush = CreateBrush("#5A4727");
                break;
            case HeroGridOverviewState.SourceUnavailable:
            case HeroGridOverviewState.NeedsReview:
            case HeroGridOverviewState.UpdateCheckFailed:
            case HeroGridOverviewState.AutomaticUpdateFailed:
            case HeroGridOverviewState.RestoreFailed:
                StatusChipBackgroundBrush = CreateBrush("#2D1E1E");
                StatusChipBorderBrush = CreateBrush("#5A3636");
                break;
            default:
                StatusChipBackgroundBrush = CreateBrush("#262626");
                StatusChipBorderBrush = CreateBrush("#343434");
                break;
        }
    }

    private void ApplyProviderVisualState(ProviderStatus providerStatus, GridOriginKind? originKind, string? sourceName)
    {
        var chip = ToProviderChipText(providerStatus.ToString(), originKind, sourceName);
        ApplyProviderVisualState(chip);
    }

    private void ApplyProviderVisualState(string? providerStatus, GridOriginKind? originKind, string? sourceName)
    {
        var chip = ToProviderChipText(providerStatus, originKind, sourceName);
        ApplyProviderVisualState(chip);
    }

    private void ApplyProviderVisualState(string providerChip)
    {
        switch (providerChip)
        {
            case "D2PT Online":
                ProviderStatusChipBackgroundBrush = CreateBrush("#1D2A22");
                ProviderStatusChipBorderBrush = CreateBrush("#355340");
                ProviderIndicatorBrush = CreateBrush("#84CC9A");
                break;
            case "Using cached grid":
            case "D2PT blocked":
            case "D2PT limited":
            case "Network issue":
                ProviderStatusChipBackgroundBrush = CreateBrush("#2C2417");
                ProviderStatusChipBorderBrush = CreateBrush("#5A4727");
                ProviderIndicatorBrush = CreateBrush("#D8B26A");
                break;
            case "Source unavailable":
            case "Unexpected response":
            case "Payload invalid":
            default:
                if (providerChip.EndsWith("Online", StringComparison.OrdinalIgnoreCase))
                {
                    ProviderStatusChipBackgroundBrush = CreateBrush("#1D2A22");
                    ProviderStatusChipBorderBrush = CreateBrush("#355340");
                    ProviderIndicatorBrush = CreateBrush("#84CC9A");
                }
                else
                {
                    ProviderStatusChipBackgroundBrush = CreateBrush("#2D1E1E");
                    ProviderStatusChipBorderBrush = CreateBrush("#5A3636");
                    ProviderIndicatorBrush = CreateBrush(providerChip == "Source not checked" ? "#858585" : "#D37C7C");
                }
                break;
        }
    }

    private string? GetSelectedInstalledHash()
        => SelectedAccount?.Model.CurrentMetaGridHash;

    private InstalledMetaGridState GetSelectedInstalledState()
        => SelectedAccount?.Model.InstalledMetaGridState ?? InstalledMetaGridState.Unknown;

    private void SyncSelectedInstalledStateToSettings()
    {
        var selected = SelectedAccount?.Model;
        if (selected is null)
        {
            _settings.LastInstalledHash = null;
            return;
        }

        _settings.LastInstalledHash = selected.InstalledMetaGridState == InstalledMetaGridState.Present
            ? selected.CurrentMetaGridHash
            : null;
    }

    private async Task RefreshSelectedInstalledStateAsync(SteamAccount account, string targetPath)
    {
        var inspection = await _dotaGridService.InspectInstalledMetaGridAsync(targetPath, _lifetimeCts.Token);
        account.HasHeroGridConfig = inspection.State is not InstalledMetaGridState.MissingFile;
        account.CurrentMetaGridHash = inspection.InstalledHash;
        account.InstalledMetaGridState = inspection.State;
        account.InstalledMetaGridError = inspection.Error;
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

public sealed record PersonalizationSourceModeOption(PersonalizationAccountSourceMode Value, string Label)
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
    public HeroGridCategoryPreview(string stableId, int order, string name, int heroCount, string heroPreviewText, IReadOnlyList<HeroIconPresentation>? heroes = null)
    {
        StableId = stableId;
        Order = order;
        Name = name;
        HeroCount = heroCount;
        HeroPreviewText = heroPreviewText;
        Heroes = heroes ?? [];
    }

    public string StableId { get; }
    public int Order { get; }
    public string Name { get; }
    public int HeroCount { get; }
    public string HeroPreviewText { get; }
    public IReadOnlyList<HeroIconPresentation> Heroes { get; }
    public string HeroCountText => $"{HeroCount} {(HeroCount == 1 ? "hero" : "heroes")}";
}

public sealed class PersonalHeroPreviewItem
{
    public PersonalHeroPreviewItem(string heroName, string statLine)
    {
        HeroName = heroName;
        StatLine = statLine;
    }

    public string HeroName { get; }
    public string StatLine { get; }
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
