namespace MetaGrid.Core.Models;

public static class HeroImageSource
{
    public static string ToCdnUrl(string? assetPath) => string.IsNullOrWhiteSpace(assetPath)
        ? string.Empty
        : Uri.TryCreate(assetPath, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? assetPath
            : $"https://cdn.cloudflare.steamstatic.com{assetPath}";
}

public sealed record HeroIconPresentation(int HeroId, string Name, string IconUrl)
{
    public string Fallback => HeroId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static HeroIconPresentation Create(int id, IReadOnlyDictionary<int, HeroDefinition> catalog)
        => catalog.TryGetValue(id, out var hero)
            ? new(id, hero.LocalizedName, HeroImageSource.ToCdnUrl(hero.IconPath))
            : new(id, $"Hero {id}", string.Empty);
}

public static class GuideStatusPresentation
{
    public static string Tone(bool enabled, GuideSubscriptionStatus status, bool busy = false)
        => status is GuideSubscriptionStatus.Removing ? "Busy"
            : status is GuideSubscriptionStatus.RemovalPending or GuideSubscriptionStatus.RemovalFailed ? "Warning"
            : !enabled ? "Off" : busy || status is GuideSubscriptionStatus.Installing or GuideSubscriptionStatus.Pending
            ? "Busy" : status is GuideSubscriptionStatus.Installed or GuideSubscriptionStatus.UpToDate
                ? "Success" : "Warning";

    public static string Label(UiTextService text, bool enabled, GuideSubscriptionStatus status)
        => status is GuideSubscriptionStatus.Removing ? text.T("Removing", "Удаление")
            : status is GuideSubscriptionStatus.RemovalPending ? text.T("Removal pending", "Ожидает удаления")
            : status is GuideSubscriptionStatus.RemovalFailed ? text.T("Removal failed", "Ошибка удаления")
            : !enabled ? text.T("Off", "Выключено") : status switch
        {
            GuideSubscriptionStatus.Pending => text.T("Waiting", "Ожидание"),
            GuideSubscriptionStatus.Installing => text.T("Syncing", "Синхронизация"),
            GuideSubscriptionStatus.Installed => text.T("Installed", "Установлен"),
            GuideSubscriptionStatus.UpToDate => text.T("Up to date", "Актуален"),
            GuideSubscriptionStatus.UpdateAvailable => text.T("Update available", "Есть обновление"),
            GuideSubscriptionStatus.Unsupported => text.T("Recheck required", "Нужна повторная проверка"),
            GuideSubscriptionStatus.NoBuild => text.T("No D2PT build", "Нет билда D2PT"),
            GuideSubscriptionStatus.SourceIncomplete => text.T("Source data incomplete", "Неполные данные источника"),
            GuideSubscriptionStatus.SourcePatchIncompatible => text.T("Source/patch mismatch", "Источник не соответствует патчу"),
            GuideSubscriptionStatus.SteamUnavailable => text.T("Steam unavailable", "Steam недоступен"),
            GuideSubscriptionStatus.SourceUnavailable => text.T("Source unavailable", "Источник недоступен"),
            GuideSubscriptionStatus.MappingFailed => text.T("Mapping failed", "Ошибка сопоставления"),
            GuideSubscriptionStatus.Conflict => text.T("Guide conflict", "Конфликт гайдов"),
            _ => text.T("Needs attention", "Требуется внимание")
        };
}

public static class GuideWorkspace
{
    public static IReadOnlyList<IGrouping<int, GuideSubscriptionRecord>> ConfiguredHeroes(IEnumerable<GuideSubscriptionRecord> subscriptions)
        => subscriptions.Where(record => record.IsEnabled || record.NeedsRemoval).GroupBy(record => record.HeroId).ToArray();

    public static bool Matches(HeroDefinition hero, string? query)
    {
        query = query?.Trim();
        return string.IsNullOrEmpty(query) || hero.LocalizedName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || hero.InternalName.Contains(query, StringComparison.OrdinalIgnoreCase) || hero.Slug.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
