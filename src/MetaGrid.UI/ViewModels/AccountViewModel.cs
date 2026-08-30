using MetaGrid.Core.Models;

namespace MetaGrid.UI.ViewModels;

public sealed class AccountViewModel : ObservableObject
{
    private readonly Action<AccountViewModel>? _onSelected;
    private readonly UiTextService _text;
    private bool _isSelected;

    public AccountViewModel(SteamAccount account, UiTextService text, Action<AccountViewModel>? onSelected = null)
    {
        Model = account;
        _text = text;
        _onSelected = onSelected;
        _isSelected = account.IsSelected;
    }

    public SteamAccount Model { get; }
    public string AccountId => Model.AccountId;
    public string DisplayName => Model.DisplayName;
    public string PersonaName => string.IsNullOrWhiteSpace(Model.PersonaName) ? _text.T("Persona name unavailable", "Имя профиля недоступно") : Model.PersonaName!;
    public string AccountSubtitle => _text.T($"Steam account {AccountId}", $"Steam-аккаунт {AccountId}");
    public string ConfigPath => Model.DotaConfigDirectory;
    public string SteamRootPath => Model.SteamRootPath;
    public bool HasHeroGridConfig => Model.HasHeroGridConfig;
    public string DotaStatus => _text.Translate(Model.HasDotaUserData ? "Dota Detected" : "No Dota");
    public InstalledMetaGridState GridState => Model.InstalledMetaGridState;
    public string GridConfigStatus => Model.InstalledMetaGridState switch
    {
        InstalledMetaGridState.Present => _text.Translate("MetaGrid Grid"),
        InstalledMetaGridState.MalformedFile => _text.Translate("Unreadable Grid"),
        InstalledMetaGridState.NoManagedGrid => _text.Translate("No Grid Yet"),
        _ => _text.Translate(Model.HasHeroGridConfig ? "Grid File Found" : "No Grid Yet")
    };
    public string GridConfigDetail => Model.InstalledMetaGridState switch
    {
        InstalledMetaGridState.Present => _text.T("MetaGrid detected a valid MetaGrid-managed hero grid for this account.", "MetaGrid обнаружил корректную управляемую сетку для этого аккаунта."),
        InstalledMetaGridState.MalformedFile => _text.T("hero_grid_config.json exists, but MetaGrid could not parse it safely.", "hero_grid_config.json существует, но MetaGrid не смог безопасно его прочитать."),
        InstalledMetaGridState.NoManagedGrid => _text.T("hero_grid_config.json exists, but it does not currently contain a MetaGrid-managed grid.", "hero_grid_config.json существует, но сейчас не содержит сетку, управляемую MetaGrid."),
        _ => _text.T("hero_grid_config.json will be created after the first successful update.", "hero_grid_config.json будет создан после первого успешного обновления.")
    };
    public string CurrentGridHash => string.IsNullOrWhiteSpace(Model.CurrentMetaGridHash) ? _text.T("Not detected", "Не обнаружено") : Model.CurrentMetaGridHash[..Math.Min(12, Model.CurrentMetaGridHash.Length)];
    public string SelectionLabel => _text.Translate(IsSelected ? "Selected" : "Select");

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
