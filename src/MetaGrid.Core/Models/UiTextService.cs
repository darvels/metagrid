using System.ComponentModel;
using System.Text.RegularExpressions;

namespace MetaGrid.Core.Models;

public sealed class UiTextService : INotifyPropertyChanged
{
    private static readonly Regex CachedGridRegex = new(@"^Showing the last valid Dota2ProTracker grid from (?<date>.+) while the source is unavailable\.$", RegexOptions.Compiled);
    private AppLanguage _language = AppLanguage.English;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value)
            {
                return;
            }

            _language = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    public string AppSubtitle => T("Dota 2 Hero Grid Updater", "Обновление сетки героев Dota 2");
    public string Dashboard => T("Dashboard", "Главная");
    public string HeroGrid => T("Hero Grid", "Сетка героев");
    public string Accounts => T("Accounts", "Аккаунты");
    public string UpdateHistory => T("Update History", "История обновлений");
    public string Settings => T("Settings", "Настройки");
    public string About => T("About", "О программе");
    public string DataSource => T("Data Source", "Источник данных");
    public string CheckForUpdates => T("Check for Updates", "Проверить обновления");
    public string ForceUpdate => T("Force Update", "Принудительно обновить");
    public string LastChecked => T("Last Checked", "Последняя проверка");
    public string LastUpdated => T("Last Updated", "Последнее обновление");
    public string SelectedAccount => T("Selected Account", "Выбранный аккаунт");
    public string NextCheck => T("Next Check", "Следующая проверка");
    public string Source => T("Source", "Источник");
    public string AvailableHash => T("Available Hash", "Доступный хэш");
    public string InstalledHash => T("Installed Hash", "Установленный хэш");
    public string SelectedGridPath => T("Selected Grid Path", "Путь к сетке");
    public string AttentionRequired => T("Attention Required", "Требуется внимание");
    public string SteamAccounts => T("Steam Accounts", "Steam-аккаунты");
    public string RedetectSteam => T("Re-detect Steam", "Повторно найти Steam");
    public string DetectedMetaGridHash => T("Detected MetaGrid Hash", "Обнаруженный хэш MetaGrid");
    public string ConfigPath => T("Config Path", "Путь к конфигу");
    public string ClearHistory => T("Clear History", "Очистить историю");
    public string PreviousHash => T("Previous Hash", "Предыдущий хэш");
    public string NewHash => T("New Hash", "Новый хэш");
    public string ChangedHeroes => T("Changed Heroes", "Изменённые герои");
    public string MadeBy => T("Made by Darvel & Codex", "Сделано Darvel и Codex");
    public string AutomaticDetectSteam => T("Automatically detect Steam", "Автоматически определять Steam");
    public string ManualSteamDirectory => T("Manual Steam directory", "Папка Steam вручную");
    public string DetectSteamAgain => T("Detect Steam Again", "Найти Steam снова");
    public string EnableAutomaticUpdates => T("Enable automatic updates", "Включить автообновления");
    public string UpdateInterval => T("Update interval", "Интервал обновления");
    public string SetupSummary => T("Setup Summary", "Итоги настройки");
    public string FinishSetup => T("Finish Setup", "Завершить настройку");

    public string Translate(string text)
    {
        if (Language == AppLanguage.English || string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var cachedMatch = CachedGridRegex.Match(text);
        if (cachedMatch.Success)
        {
            return $"Показана последняя корректная сетка Dota2ProTracker от {cachedMatch.Groups["date"].Value}, потому что источник сейчас недоступен.";
        }

        return text switch
        {
            "Starting MetaGrid" => "Запуск MetaGrid",
            "Rendering the desktop shell and preparing your Dota 2 workspace." => "Подготавливаем окно приложения и рабочее окружение Dota 2.",
            "Never" => "Никогда",
            "No account selected" => "Аккаунт не выбран",
            "Choose a Dota 2 account to see its configuration path." => "Выберите аккаунт Dota 2, чтобы увидеть путь к конфигурации.",
            "Unknown" => "Неизвестно",
            "Not installed" => "Не установлено",
            "Not cached" => "Не кэшировано",
            "Not scheduled" => "Не запланировано",
            "Waiting for initialization" => "Ожидание инициализации",
            "Steam not checked yet" => "Steam ещё не проверен",
            "No Steam installation has been detected yet." => "Установка Steam пока не обнаружена.",
            "Dota2ProTracker not checked yet" => "Dota2ProTracker ещё не проверен",
            "Official Dota2ProTracker High Winrate grid" => "Официальная High Winrate сетка Dota2ProTracker",
            "Run Check for Updates to load the latest official Dota2ProTracker High Winrate layouts." => "Нажмите «Проверить обновления», чтобы загрузить актуальные High Winrate раскладки Dota2ProTracker.",
            "Not checked" => "Не проверено",
            "Auto Update On" => "Автообновление включено",
            "Auto Update Off" => "Автообновление выключено",
            "Source not checked" => "Источник не проверен",
            "Selected Steam account" => "Выбранный Steam-аккаунт",
            "Personal heroes off" => "Персонализация выключена",
            "OpenDota automatically uses your selected Steam account to add MY BEST HEROES to the All Roles layout." => "OpenDota автоматически использует выбранный Steam-аккаунт, чтобы добавить MY BEST HEROES в раскладку All Roles.",
            "No OpenDota account resolved yet" => "Аккаунт OpenDota пока не определён",
            "Unsaved changes are ready to save." => "Есть несохранённые изменения.",
            "All displayed settings are currently saved." => "Все показанные настройки уже сохранены.",
            "Current Grid Preview" => "Предпросмотр текущей сетки",
            "Welcome to MetaGrid" => "Добро пожаловать в MetaGrid",
            "Steam Detection" => "Обнаружение Steam",
            "Dota Accounts" => "Аккаунты Dota",
            "Automatic Updates" => "Автообновления",
            "You're ready" => "Всё готово",
            "Continue" => "Продолжить",
            "Open MetaGrid" => "Открыть MetaGrid",
            "Initializing" => "Инициализация",
            "Loading MetaGrid settings, accounts, and update history without blocking the visible window." => "Загружаем настройки, аккаунты и историю MetaGrid, не блокируя видимое окно.",
            "Initializing services" => "Инициализация служб",
            "Attention required" => "Требуется внимание",
            "MetaGrid opened successfully with a degraded subsystem state. Review the notice below and continue setup." => "MetaGrid открылся, но одна из подсистем работает с ограничениями. Посмотрите уведомление и продолжайте настройку.",
            "Checking for Updates" => "Проверка обновлений",
            "Checking Dota2ProTracker for the latest High Winrate grid." => "Проверяем Dota2ProTracker на наличие актуальной High Winrate сетки.",
            "Checking" => "Проверка",
            "Update check failed" => "Проверка обновлений не удалась",
            "Source error" => "Ошибка источника",
            "Failed" => "Ошибка",
            "Auto update failed" => "Сбой автообновления",
            "Background updates paused" => "Фоновые обновления приостановлены",
            "Scheduler paused" => "Планировщик на паузе",
            "Save failed" => "Ошибка сохранения",
            "Backup restored" => "Резервная копия восстановлена",
            "Action required" => "Требуется действие",
            "Restore failed" => "Восстановление не удалось",
            "Select a Steam Account" => "Выберите Steam-аккаунт",
            "Choose the Steam account MetaGrid should manage before installing hero grid updates." => "Выберите Steam-аккаунт, которым MetaGrid должен управлять перед установкой обновлений сетки героев.",
            "Not checked yet" => "Ещё не проверено",
            "MetaGrid is ready to check Dota2ProTracker for the latest High Winrate grid." => "MetaGrid готов проверить Dota2ProTracker на наличие актуальной High Winrate сетки.",
            "Grid Ready to Install" => "Сетка готова к установке",
            "The latest Dota2ProTracker High Winrate grid is ready to install for this Steam account." => "Актуальная High Winrate сетка Dota2ProTracker готова к установке для этого Steam-аккаунта.",
            "Grid ready to install" => "Готово к установке",
            "Grid Installed" => "Сетка установлена",
            "Your hero grid matches the latest Dota2ProTracker High Winrate grid." => "Сетка героев совпадает с актуальной High Winrate сеткой Dota2ProTracker.",
            "Grid installed" => "Сетка установлена",
            "New Grid Available" => "Доступна новая сетка",
            "A newer Dota2ProTracker High Winrate grid is ready to install." => "Новая High Winrate сетка Dota2ProTracker готова к установке.",
            "Update available" => "Доступно обновление",
            "Dota2ProTracker Unavailable" => "Dota2ProTracker недоступен",
            "MetaGrid could not reach Dota2ProTracker right now." => "MetaGrid сейчас не может получить данные от Dota2ProTracker.",
            "Source unavailable" => "Источник недоступен",
            "Installed Grid Could Not Be Verified" => "Установленную сетку не удалось проверить",
            "MetaGrid found hero_grid_config.json for the selected Steam account, but the file could not be parsed safely. No install state is being inferred from cached metadata." => "MetaGrid нашёл hero_grid_config.json для выбранного Steam-аккаунта, но не смог безопасно прочитать файл. Состояние установки не определяется по кэшу.",
            "Needs review" => "Нужна проверка",
            "Update Check Failed" => "Проверка обновлений не удалась",
            "MetaGrid could not check Dota2ProTracker right now. Your installed grid was left unchanged." => "MetaGrid не смог проверить Dota2ProTracker. Установленная сетка не изменялась.",
            "Automatic Update Failed" => "Автообновление не удалось",
            "MetaGrid could not complete this update attempt. Your installed grid was left unchanged, and automatic updates will try again later." => "MetaGrid не смог завершить эту попытку обновления. Установленная сетка не изменена, следующая авто-попытка произойдёт позже.",
            "Grid Update Failed" => "Обновление сетки не удалось",
            "MetaGrid could not install the new grid. Your previous Dota 2 grid was restored or left unchanged." => "MetaGrid не смог установить новую сетку. Предыдущая сетка Dota 2 была восстановлена или оставлена без изменений.",
            "D2PT Online" => "D2PT онлайн",
            "Cached data" => "Данные из кэша",
            "D2PT blocked by Cloudflare" => "D2PT заблокирован Cloudflare",
            "D2PT unavailable" => "D2PT недоступен",
            "Provider online" => "Источник онлайн",
            "Provider rate limited" => "Источник ограничил запросы",
            "Network unavailable" => "Сеть недоступна",
            "Unexpected provider response" => "Неожиданный ответ источника",
            "Provider payload invalid" => "Некорректный ответ источника",
            "Using cached grid" => "Используется кэш",
            "D2PT blocked" => "D2PT заблокирован",
            "D2PT limited" => "D2PT ограничен",
            "Network issue" => "Проблема сети",
            "Unexpected response" => "Неожиданный ответ",
            "Payload invalid" => "Некорректные данные",
            "MetaGrid Grid" => "Сетка MetaGrid",
            "Unreadable Grid" => "Нечитаемая сетка",
            "No Grid Yet" => "Сетки пока нет",
            "Grid File Found" => "Файл сетки найден",
            "Dota Detected" => "Dota обнаружена",
            "No Dota" => "Dota не найдена",
            "Selected" => "Выбран",
            "Select" => "Выбрать",
            _ => text
        };
    }

    public string FormatStepChip(int step) => $"{T("Step", "Шаг")} {step} {T("of 5", "из 5")}";

    public string T(string english, string russian) => Language == AppLanguage.Russian ? russian : english;
}

public sealed record LanguageOption(AppLanguage Value, string Label)
{
    public override string ToString() => Label;
}
