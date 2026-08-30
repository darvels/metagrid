using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MetaGrid.Core.Models;

public sealed class AppSettings : INotifyPropertyChanged
{
    private bool _autoUpdateEnabled = true;
    private UpdateInterval _updateInterval = UpdateInterval.OneHour;
    private int _backupRetentionCount = 10;
    private bool _launchWithWindows;
    private bool _startMinimized;
    private bool _minimizeToTray = true;
    private bool _closeToTray = true;
    private bool _automaticallyCheckAppUpdates = true;
    private bool _automaticallyDetectSteam = true;
    private string? _steamDirectoryOverride;
    private string? _preferredAccountId;
    private List<string> _selectedAccountIds = [];
    private bool _onboardingCompleted;
    private HeroGridPreset _preferredPreset = HeroGridPreset.HighWinrate;
    private bool _personalizationEnabled;
    private PersonalizationAccountSourceMode _personalizationAccountSourceMode = PersonalizationAccountSourceMode.SelectedSteamAccount;
    private string? _personalizationAccountId;
    private string? _personalizationManualAccountId;
    private DateTimeOffset? _lastSuccessfulPersonalStatsRefreshAt;
    private string? _lastPersonalizationStatus;
    private string? _lastPersonalizationMessage;
    private string? _lastPersonalizationAccountDisplayName;
    private DateTimeOffset? _lastCheckAt;
    private DateTimeOffset? _lastSuccessfulUpdateAt;
    private DateTimeOffset? _lastSuccessfulLiveProviderCheckAt;
    private DateTimeOffset? _lastAppUpdateCheckAt;
    private string? _lastBaseSourceHash;
    private string? _lastEffectiveGridHash;
    private string? _lastRemoteHash;
    private string? _lastCachedHash;
    private string? _lastInstalledHash;
    private string? _lastProviderStatus;
    private string? _lastSourceName;
    private string? _lastSourceStrategy;
    private string? _lastSourceDetails;
    private string? _lastGridOrigin;
    private DateTimeOffset? _lastGridCapturedAt;
    private int? _lastHeroCount;
    private string? _lastRoleSummary;
    private string? _lastAvailableAppVersion;
    private string? _lastAppUpdateState;
    private string? _lastAppUpdateMessage;
    private string? _deferredAppUpdateVersion;
    private DateTimeOffset? _deferredAppUpdateUntil;
    private bool _hasSeenCloseToTrayNotification;
    private AppLanguage _language = AppLanguage.English;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool AutoUpdateEnabled
    {
        get => _autoUpdateEnabled;
        set => SetProperty(ref _autoUpdateEnabled, value);
    }

    public UpdateInterval UpdateInterval
    {
        get => _updateInterval;
        set => SetProperty(ref _updateInterval, value);
    }

    public int BackupRetentionCount
    {
        get => _backupRetentionCount;
        set => SetProperty(ref _backupRetentionCount, value);
    }

    public bool LaunchWithWindows
    {
        get => _launchWithWindows;
        set => SetProperty(ref _launchWithWindows, value);
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set => SetProperty(ref _startMinimized, value);
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => SetProperty(ref _minimizeToTray, value);
    }

    public bool CloseToTray
    {
        get => _closeToTray;
        set => SetProperty(ref _closeToTray, value);
    }

    public bool AutomaticallyCheckAppUpdates
    {
        get => _automaticallyCheckAppUpdates;
        set => SetProperty(ref _automaticallyCheckAppUpdates, value);
    }

    public bool AutomaticallyDetectSteam
    {
        get => _automaticallyDetectSteam;
        set => SetProperty(ref _automaticallyDetectSteam, value);
    }

    public string? SteamDirectoryOverride
    {
        get => _steamDirectoryOverride;
        set => SetProperty(ref _steamDirectoryOverride, value);
    }

    public string? PreferredAccountId
    {
        get => _preferredAccountId;
        set => SetProperty(ref _preferredAccountId, value);
    }

    public List<string> SelectedAccountIds
    {
        get => _selectedAccountIds;
        set => SetProperty(ref _selectedAccountIds, value ?? []);
    }

    public bool OnboardingCompleted
    {
        get => _onboardingCompleted;
        set => SetProperty(ref _onboardingCompleted, value);
    }

    public HeroGridPreset PreferredPreset
    {
        get => _preferredPreset;
        set => SetProperty(ref _preferredPreset, value);
    }

    public bool PersonalizationEnabled
    {
        get => _personalizationEnabled;
        set => SetProperty(ref _personalizationEnabled, value);
    }

    public PersonalizationAccountSourceMode PersonalizationAccountSourceMode
    {
        get => _personalizationAccountSourceMode;
        set => SetProperty(ref _personalizationAccountSourceMode, value);
    }

    public string? PersonalizationAccountId
    {
        get => _personalizationAccountId;
        set => SetProperty(ref _personalizationAccountId, value);
    }

    public string? PersonalizationManualAccountId
    {
        get => _personalizationManualAccountId;
        set => SetProperty(ref _personalizationManualAccountId, value);
    }

    public DateTimeOffset? LastSuccessfulPersonalStatsRefreshAt
    {
        get => _lastSuccessfulPersonalStatsRefreshAt;
        set => SetProperty(ref _lastSuccessfulPersonalStatsRefreshAt, value);
    }

    public string? LastPersonalizationStatus
    {
        get => _lastPersonalizationStatus;
        set => SetProperty(ref _lastPersonalizationStatus, value);
    }

    public string? LastPersonalizationMessage
    {
        get => _lastPersonalizationMessage;
        set => SetProperty(ref _lastPersonalizationMessage, value);
    }

    public string? LastPersonalizationAccountDisplayName
    {
        get => _lastPersonalizationAccountDisplayName;
        set => SetProperty(ref _lastPersonalizationAccountDisplayName, value);
    }

    public DateTimeOffset? LastCheckAt
    {
        get => _lastCheckAt;
        set => SetProperty(ref _lastCheckAt, value);
    }

    public DateTimeOffset? LastSuccessfulUpdateAt
    {
        get => _lastSuccessfulUpdateAt;
        set => SetProperty(ref _lastSuccessfulUpdateAt, value);
    }

    public DateTimeOffset? LastSuccessfulLiveProviderCheckAt
    {
        get => _lastSuccessfulLiveProviderCheckAt;
        set => SetProperty(ref _lastSuccessfulLiveProviderCheckAt, value);
    }

    public DateTimeOffset? LastAppUpdateCheckAt
    {
        get => _lastAppUpdateCheckAt;
        set => SetProperty(ref _lastAppUpdateCheckAt, value);
    }

    public string? LastBaseSourceHash
    {
        get => _lastBaseSourceHash;
        set => SetProperty(ref _lastBaseSourceHash, value);
    }

    public string? LastEffectiveGridHash
    {
        get => _lastEffectiveGridHash;
        set => SetProperty(ref _lastEffectiveGridHash, value);
    }

    public string? LastRemoteHash
    {
        get => _lastRemoteHash;
        set => SetProperty(ref _lastRemoteHash, value);
    }

    public string? LastCachedHash
    {
        get => _lastCachedHash;
        set => SetProperty(ref _lastCachedHash, value);
    }

    public string? LastInstalledHash
    {
        get => _lastInstalledHash;
        set => SetProperty(ref _lastInstalledHash, value);
    }

    public string? LastProviderStatus
    {
        get => _lastProviderStatus;
        set => SetProperty(ref _lastProviderStatus, value);
    }

    public string? LastSourceName
    {
        get => _lastSourceName;
        set => SetProperty(ref _lastSourceName, value);
    }

    public string? LastSourceStrategy
    {
        get => _lastSourceStrategy;
        set => SetProperty(ref _lastSourceStrategy, value);
    }

    public string? LastSourceDetails
    {
        get => _lastSourceDetails;
        set => SetProperty(ref _lastSourceDetails, value);
    }

    public string? LastGridOrigin
    {
        get => _lastGridOrigin;
        set => SetProperty(ref _lastGridOrigin, value);
    }

    public DateTimeOffset? LastGridCapturedAt
    {
        get => _lastGridCapturedAt;
        set => SetProperty(ref _lastGridCapturedAt, value);
    }

    public int? LastHeroCount
    {
        get => _lastHeroCount;
        set => SetProperty(ref _lastHeroCount, value);
    }

    public string? LastRoleSummary
    {
        get => _lastRoleSummary;
        set => SetProperty(ref _lastRoleSummary, value);
    }

    public string? LastAvailableAppVersion
    {
        get => _lastAvailableAppVersion;
        set => SetProperty(ref _lastAvailableAppVersion, value);
    }

    public string? LastAppUpdateState
    {
        get => _lastAppUpdateState;
        set => SetProperty(ref _lastAppUpdateState, value);
    }

    public string? LastAppUpdateMessage
    {
        get => _lastAppUpdateMessage;
        set => SetProperty(ref _lastAppUpdateMessage, value);
    }

    public string? DeferredAppUpdateVersion
    {
        get => _deferredAppUpdateVersion;
        set => SetProperty(ref _deferredAppUpdateVersion, value);
    }

    public DateTimeOffset? DeferredAppUpdateUntil
    {
        get => _deferredAppUpdateUntil;
        set => SetProperty(ref _deferredAppUpdateUntil, value);
    }

    public bool HasSeenCloseToTrayNotification
    {
        get => _hasSeenCloseToTrayNotification;
        set => SetProperty(ref _hasSeenCloseToTrayNotification, value);
    }

    public AppLanguage Language
    {
        get => _language;
        set => SetProperty(ref _language, value);
    }

    public AppSettings CreateCopy() => new()
    {
        AutoUpdateEnabled = AutoUpdateEnabled,
        UpdateInterval = UpdateInterval,
        BackupRetentionCount = BackupRetentionCount,
        LaunchWithWindows = LaunchWithWindows,
        StartMinimized = StartMinimized,
        MinimizeToTray = MinimizeToTray,
        CloseToTray = CloseToTray,
        AutomaticallyCheckAppUpdates = AutomaticallyCheckAppUpdates,
        AutomaticallyDetectSteam = AutomaticallyDetectSteam,
        SteamDirectoryOverride = SteamDirectoryOverride,
        PreferredAccountId = PreferredAccountId,
        SelectedAccountIds = [.. SelectedAccountIds],
        OnboardingCompleted = OnboardingCompleted,
        PreferredPreset = PreferredPreset,
        PersonalizationEnabled = PersonalizationEnabled,
        PersonalizationAccountSourceMode = PersonalizationAccountSourceMode,
        PersonalizationAccountId = PersonalizationAccountId,
        PersonalizationManualAccountId = PersonalizationManualAccountId,
        LastSuccessfulPersonalStatsRefreshAt = LastSuccessfulPersonalStatsRefreshAt,
        LastPersonalizationStatus = LastPersonalizationStatus,
        LastPersonalizationMessage = LastPersonalizationMessage,
        LastPersonalizationAccountDisplayName = LastPersonalizationAccountDisplayName,
        LastCheckAt = LastCheckAt,
        LastSuccessfulUpdateAt = LastSuccessfulUpdateAt,
        LastSuccessfulLiveProviderCheckAt = LastSuccessfulLiveProviderCheckAt,
        LastAppUpdateCheckAt = LastAppUpdateCheckAt,
        LastBaseSourceHash = LastBaseSourceHash,
        LastEffectiveGridHash = LastEffectiveGridHash,
        LastRemoteHash = LastRemoteHash,
        LastCachedHash = LastCachedHash,
        LastInstalledHash = LastInstalledHash,
        LastProviderStatus = LastProviderStatus,
        LastSourceName = LastSourceName,
        LastSourceStrategy = LastSourceStrategy,
        LastSourceDetails = LastSourceDetails,
        LastGridOrigin = LastGridOrigin,
        LastGridCapturedAt = LastGridCapturedAt,
        LastHeroCount = LastHeroCount,
        LastRoleSummary = LastRoleSummary,
        LastAvailableAppVersion = LastAvailableAppVersion,
        LastAppUpdateState = LastAppUpdateState,
        LastAppUpdateMessage = LastAppUpdateMessage,
        DeferredAppUpdateVersion = DeferredAppUpdateVersion,
        DeferredAppUpdateUntil = DeferredAppUpdateUntil,
        HasSeenCloseToTrayNotification = HasSeenCloseToTrayNotification,
        Language = Language
    };

    public void CopyFrom(AppSettings other)
    {
        AutoUpdateEnabled = other.AutoUpdateEnabled;
        UpdateInterval = other.UpdateInterval;
        BackupRetentionCount = other.BackupRetentionCount;
        LaunchWithWindows = other.LaunchWithWindows;
        StartMinimized = other.StartMinimized;
        MinimizeToTray = other.MinimizeToTray;
        CloseToTray = other.CloseToTray;
        AutomaticallyCheckAppUpdates = other.AutomaticallyCheckAppUpdates;
        AutomaticallyDetectSteam = other.AutomaticallyDetectSteam;
        SteamDirectoryOverride = other.SteamDirectoryOverride;
        PreferredAccountId = other.PreferredAccountId;
        SelectedAccountIds = [.. other.SelectedAccountIds];
        OnboardingCompleted = other.OnboardingCompleted;
        PreferredPreset = other.PreferredPreset;
        PersonalizationEnabled = other.PersonalizationEnabled;
        PersonalizationAccountSourceMode = other.PersonalizationAccountSourceMode;
        PersonalizationAccountId = other.PersonalizationAccountId;
        PersonalizationManualAccountId = other.PersonalizationManualAccountId;
        LastSuccessfulPersonalStatsRefreshAt = other.LastSuccessfulPersonalStatsRefreshAt;
        LastPersonalizationStatus = other.LastPersonalizationStatus;
        LastPersonalizationMessage = other.LastPersonalizationMessage;
        LastPersonalizationAccountDisplayName = other.LastPersonalizationAccountDisplayName;
        LastCheckAt = other.LastCheckAt;
        LastSuccessfulUpdateAt = other.LastSuccessfulUpdateAt;
        LastSuccessfulLiveProviderCheckAt = other.LastSuccessfulLiveProviderCheckAt;
        LastAppUpdateCheckAt = other.LastAppUpdateCheckAt;
        LastBaseSourceHash = other.LastBaseSourceHash;
        LastEffectiveGridHash = other.LastEffectiveGridHash;
        LastRemoteHash = other.LastRemoteHash;
        LastCachedHash = other.LastCachedHash;
        LastInstalledHash = other.LastInstalledHash;
        LastProviderStatus = other.LastProviderStatus;
        LastSourceName = other.LastSourceName;
        LastSourceStrategy = other.LastSourceStrategy;
        LastSourceDetails = other.LastSourceDetails;
        LastGridOrigin = other.LastGridOrigin;
        LastGridCapturedAt = other.LastGridCapturedAt;
        LastHeroCount = other.LastHeroCount;
        LastRoleSummary = other.LastRoleSummary;
        LastAvailableAppVersion = other.LastAvailableAppVersion;
        LastAppUpdateState = other.LastAppUpdateState;
        LastAppUpdateMessage = other.LastAppUpdateMessage;
        DeferredAppUpdateVersion = other.DeferredAppUpdateVersion;
        DeferredAppUpdateUntil = other.DeferredAppUpdateUntil;
        HasSeenCloseToTrayNotification = other.HasSeenCloseToTrayNotification;
        Language = other.Language;
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
