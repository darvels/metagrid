using MetaGrid.Core.Models;

namespace MetaGrid.UI.ViewModels;

public sealed class AccountViewModel : ObservableObject
{
    private readonly Action<AccountViewModel>? _onSelected;
    private bool _isSelected;

    public AccountViewModel(SteamAccount account, Action<AccountViewModel>? onSelected = null)
    {
        Model = account;
        _onSelected = onSelected;
        _isSelected = account.IsSelected;
    }

    public SteamAccount Model { get; }
    public string AccountId => Model.AccountId;
    public string DisplayName => Model.DisplayName;
    public string PersonaName => string.IsNullOrWhiteSpace(Model.PersonaName) ? "Persona name unavailable" : Model.PersonaName!;
    public string AccountSubtitle => string.IsNullOrWhiteSpace(Model.PersonaName) ? $"Steam account {AccountId}" : $"Steam account {AccountId}";
    public string ConfigPath => Model.DotaConfigDirectory;
    public string SteamRootPath => Model.SteamRootPath;
    public bool HasHeroGridConfig => Model.HasHeroGridConfig;
    public string DotaStatus => Model.HasDotaUserData ? "Dota Detected" : "No Dota";
    public string GridConfigStatus => Model.HasHeroGridConfig ? "Grid Ready" : "No Grid Yet";
    public string GridConfigDetail => Model.HasHeroGridConfig
        ? "MetaGrid found an existing hero_grid_config.json for this account."
        : "hero_grid_config.json will be created after the first successful update.";
    public string CurrentGridHash => string.IsNullOrWhiteSpace(Model.CurrentMetaGridHash) ? "Not detected" : Model.CurrentMetaGridHash[..Math.Min(12, Model.CurrentMetaGridHash.Length)];
    public string SelectionLabel => IsSelected ? "Selected" : "Select";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                Model.IsSelected = value;
                RaisePropertyChanged(nameof(SelectionLabel));
                if (value)
                {
                    _onSelected?.Invoke(this);
                }
            }
        }
    }
}
