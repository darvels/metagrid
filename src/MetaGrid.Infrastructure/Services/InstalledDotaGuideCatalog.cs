using System.Text.RegularExpressions;

namespace MetaGrid.Infrastructure.Services;

public sealed class InstalledDotaGuideCatalog
{
    public required ValveDataNode Hero { get; init; }
    public required IReadOnlyDictionary<string, ValveDataNode> Items { get; init; }
    public required IReadOnlyDictionary<string, string> Labels { get; init; }
    public required string Evidence { get; init; }
    public IReadOnlyDictionary<string, ValveDataNode> TalentDefinitions { get; init; } = new Dictionary<string, ValveDataNode>();

    public static InstalledDotaGuideCatalog Load(string steamRoot, string hero, CancellationToken token)
        => CreateSession(steamRoot, token)(hero, token);

    public static Func<string, CancellationToken, InstalledDotaGuideCatalog> CreateSession(string steamRoot, CancellationToken token)
    {
        var libraries = new List<string> { steamRoot };
        var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryFile))
            libraries.AddRange(Regex.Matches(File.ReadAllText(libraryFile), "\"path\"\\s+\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value.Replace("\\\\", "\\")));
        var directory = libraries.Select(p => Path.Combine(p, "steamapps", "common", "dota 2 beta", "game", "dota", "pak01_dir.vpk"))
            .Distinct(StringComparer.OrdinalIgnoreCase).SingleOrDefault(File.Exists)
            ?? throw new FileNotFoundException("No uniquely identified installed Dota VPK mapping source.");
        var vpk = new DotaVpkReader(directory, token);
        var talentDefinitions = ValveDataDocument.Parse(vpk.ReadText("scripts/npc/npc_abilities.txt")).Single().Children
            .Where(n => n.Key.StartsWith("special_bonus_", StringComparison.Ordinal)).ToDictionary(n => n.Key, StringComparer.OrdinalIgnoreCase);
        var items = ValveDataDocument.Parse(vpk.ReadText("scripts/npc/items.txt")).Single().Children
            .Where(n => n.Children.Count > 0 && n.Key.StartsWith("item_"))
            .ToDictionary(n => n.Key, StringComparer.OrdinalIgnoreCase);
        var labels = ValveDataDocument.Parse(vpk.ReadText("resource/localization/abilities_english.txt"))
            .Single().Child("Tokens").Children.Where(n => n.Value is not null)
            .GroupBy(n => n.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Value!, StringComparer.OrdinalIgnoreCase);
        foreach (var label in ValveDataDocument.Parse(vpk.ReadText("resource/localization/dota_english.txt")).Single().Child("Tokens").Children.Where(n => n.Value is not null))
            labels.TryAdd(label.Key, label.Value!);
        return (hero, cancellation) =>
        {
            cancellation.ThrowIfCancellationRequested();
            var heroPath = $"scripts/npc/heroes/{hero}.txt";
            var heroNode = ValveDataDocument.Parse(vpk.ReadText(heroPath)).Single(n => n.Key == "DOTAHeroes").Child(hero);
            return new() { Hero = heroNode, Items = items, Labels = labels, TalentDefinitions = talentDefinitions,
                Evidence = directory + " :: " + heroPath + "; scripts/npc/items.txt; resource/localization/abilities_english.txt; resource/localization/dota_english.txt" };
        };
    }

    public string? FormatTalentLabel(string id)
    {
        if (!Labels.TryGetValue("DOTA_Tooltip_ability_" + id, out var label)) return null;
        return Regex.Replace(label, @"\{s:([a-zA-Z0-9_]+)\}", match =>
        {
            var key = match.Groups[1].Value;
            var values = new HashSet<decimal>();
            if (TalentDefinitions.TryGetValue(id, out var definition))
            {
                var field = definition.Children.FirstOrDefault(n => n.Key == "AbilityValues")?.Children.FirstOrDefault(n => n.Key == key);
                Add(field?.Value ?? field?.Get("value"));
            }
            var abilityValues = Hero.Children.FirstOrDefault(n => n.Key == "AbilityDefinitions")?.Children
                .SelectMany(n => n.Children.Where(c => c.Key == "AbilityValues")).SelectMany(n => n.Children) ?? [];
            var fieldName = key.StartsWith("bonus_", StringComparison.Ordinal) ? key[6..] : key;
            foreach (var field in abilityValues.Where(n => n.Key.Equals(fieldName, StringComparison.OrdinalIgnoreCase)))
                Add(field.Get(id));
            return values.Count == 1 ? values.Single().ToString("0.################", System.Globalization.CultureInfo.InvariantCulture) : match.Value;
            void Add(string? value)
            {
                if (value is not null && decimal.TryParse(value.TrimStart('+', '='), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var numeric)) values.Add(Math.Abs(numeric));
            }
        });
    }

    public string ResolveItem(string source)
    {
        if (Items.ContainsKey(source)) return source;
        var normalized = Normalize(source);
        var candidates = Items.Keys.Where(id => Labels.TryGetValue("DOTA_Tooltip_ability_" + id, out var display)
            && Normalize(display) == normalized).ToArray();
        return candidates.Length == 1 ? candidates[0]
            : throw new InvalidDataException($"Item '{source}' has {candidates.Length} current-Dota display-name matches; refusing mapping.");
    }

    private static string Normalize(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
