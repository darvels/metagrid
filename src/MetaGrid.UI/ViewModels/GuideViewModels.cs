using MetaGrid.Core.Models;

namespace MetaGrid.UI.ViewModels;

public sealed class GuideHeroCardViewModel : ObservableObject
{
    private bool _isSelected;
    private int _activeRoleCount;
    private IReadOnlyList<GuideRoleBadge> _roles = [];
    public IReadOnlyList<GuideRoleBadge> Roles
    {
        get => _roles;
        set => SetProperty(ref _roles, value);
    }

    public GuideHeroCardViewModel(HeroDefinition hero, Action<GuideHeroCardViewModel> selectAction)
    {
        Hero = hero;
        SelectCommand = new RelayCommand(() => selectAction(this));
    }

    public HeroDefinition Hero { get; }
    public RelayCommand SelectCommand { get; }
    public string IconUrl => HeroImageSource.ToCdnUrl(Hero.IconPath);
    public string PortraitUrl => HeroImageSource.ToCdnUrl(Hero.PortraitPath);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public int ActiveRoleCount
    {
        get => _activeRoleCount;
        set
        {
            if (SetProperty(ref _activeRoleCount, value))
            {
                RaisePropertyChanged(nameof(HasActiveRoles));
                RaisePropertyChanged(nameof(ActiveRolesLabel));
            }
        }
    }

    public bool HasActiveRoles => ActiveRoleCount > 0;
    public string ActiveRolesLabel => ActiveRoleCount == 1 ? "1 role enabled" : $"{ActiveRoleCount} roles enabled";

}

public sealed class GuideRoleOptionViewModel : ObservableObject
{
    private readonly Func<bool, Task> _toggleAsync;
    private readonly Func<Task> _syncAsync;
    private bool _isEnabled;
    private bool _isBusy;
    private string _statusChip = "Disabled";
    private string _statusDetail = "Auto Guide is disabled for this role.";
    private string _hashDetail = string.Empty;
    private string _statusTone = "Off";

    public GuideRoleOptionViewModel(GuideRole role, string label, bool isEnabled, Func<bool, Task> toggleAsync, Func<Task> syncAsync)
    {
        Role = role;
        Label = label;
        _isEnabled = isEnabled;
        _toggleAsync = toggleAsync;
        _syncAsync = syncAsync;
        SyncCommand = new AsyncRelayCommand(SyncAsync, () => !IsBusy && IsEnabled);
    }

    public GuideRole Role { get; }
    public string Label { get; }
    public AsyncRelayCommand SyncCommand { get; }
    public Task ToggleTask { get; private set; } = Task.CompletedTask;
    public string StatusTone
    {
        get => _statusTone;
        set => SetProperty(ref _statusTone, value);
    }

    public string StatusTooltip => $"{Label}: {StatusChip}";

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (IsBusy) return;
            if (SetProperty(ref _isEnabled, value))
            {
                RaisePropertyChanged(nameof(CanSync));
                SyncCommand.NotifyCanExecuteChanged();
                ToggleTask = _toggleAsync(value);
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaisePropertyChanged(nameof(CanSync));
                RaisePropertyChanged(nameof(CanToggle));
                SyncCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusChip
    {
        get => _statusChip;
        set
        {
            if (SetProperty(ref _statusChip, value))
                RaisePropertyChanged(nameof(StatusTooltip));
        }
    }

    public string StatusDetail
    {
        get => _statusDetail;
        set => SetProperty(ref _statusDetail, value);
    }

    public string HashDetail
    {
        get => _hashDetail;
        set
        {
            if (SetProperty(ref _hashDetail, value))
            {
                RaisePropertyChanged(nameof(HasHashDetail));
            }
        }
    }

    public bool HasHashDetail => !string.IsNullOrWhiteSpace(HashDetail);
    public bool CanSync => !IsBusy && IsEnabled;
    public bool CanToggle => !IsBusy;

    private async Task SyncAsync()
    {
        if (IsBusy)
        {
            return;
        }

        await _syncAsync();
    }
}

public sealed record GuideRoleBadge(string Label, string StatusTone, string StatusTooltip);
