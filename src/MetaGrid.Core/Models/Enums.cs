namespace MetaGrid.Core.Models;

public enum UpdateInterval
{
    FifteenMinutes = 15,
    ThirtyMinutes = 30,
    OneHour = 60,
    ThreeHours = 180,
    SixHours = 360,
    TwelveHours = 720,
    TwentyFourHours = 1440
}

public enum HeroGridPreset
{
    MostPlayed,
    HighWinrate,
    D2ptRating
}

public enum UpdateStatus
{
    Checking,
    Updated,
    AlreadyUpToDate,
    Failed,
    BackupRestored,
    SourceUnavailable,
    UpdateAvailable,
    ParsingFailed,
    UnexpectedResponse,
    WaitingForAccount
}

public enum UpdateTriggerKind
{
    ManualCheck,
    ManualInstall,
    Automatic
}

public enum ProviderStatus
{
    Unknown,
    Online,
    Unavailable,
    CloudflareBlocked,
    RateLimited,
    NetworkUnavailable,
    UnexpectedResponse,
    ParsingFailed,
    Cached
}

public enum GridOriginKind
{
    NativeD2pt,
    RebuiltD2pt,
    AlternativeProvider,
    Cached
}

public enum InstalledMetaGridState
{
    Unknown,
    MissingFile,
    MalformedFile,
    NoManagedGrid,
    Present
}

public enum LogLevelKind
{
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

public enum AppLanguage
{
    English,
    Russian
}
