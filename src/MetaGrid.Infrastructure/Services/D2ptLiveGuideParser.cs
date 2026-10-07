using System.Text.Json;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public static class D2ptLiveGuideParser
{
    public static NormalizedHeroGuideBuild Parse(string html)
    {
        using var doc = JsonDocument.Parse(D2ptPageDataExtractor.ExtractHydrationJson(html));
        var shared = doc.RootElement[0].GetProperty("data");
        var page = doc.RootElement[1].GetProperty("data");
        var source = page.GetProperty("build");
        return ParseSource(shared, page, source, page.GetProperty("slug").GetString()!, "https://dota2protracker.com/builds/" + page.GetProperty("slug").GetString());
    }

    public static NormalizedHeroGuideBuild ParseIndex(string html, int heroId, GuideRole role)
    {
        using var doc = JsonDocument.Parse(D2ptPageDataExtractor.ExtractHydrationJson(html));
        return ParseIndex(doc.RootElement, heroId, role);
    }

    public static NormalizedHeroGuideBuild ParseIndex(JsonElement root, int heroId, GuideRole role)
    {
        var shared = root[0].GetProperty("data");
        var page = root.EnumerateArray().Select(n => n.TryGetProperty("data", out var data) ? data : default)
            .First(n => n.ValueKind == JsonValueKind.Object && n.TryGetProperty("builds", out _));
        var candidates = page.GetProperty("builds").EnumerateArray()
            .Where(b => b.GetProperty("hero_id").GetInt32() == heroId && b.GetProperty("position").GetString() == $"pos {(int)role + 1}")
            .OrderByDescending(b => b.GetProperty("num_matches").GetInt32()).ThenBy(b => b.GetProperty("build_id").GetInt32()).ToArray();
        if (candidates.Length == 0) throw new D2ptNoGuideBuildException(heroId, role);
        var source = candidates[0];
        return ParseSource(shared, page, source, $"{heroId}-pos{(int)role + 1}-{source.GetProperty("build_id").GetInt32()}", "https://dota2protracker.com/builds");
    }

    private static NormalizedHeroGuideBuild ParseSource(JsonElement shared, JsonElement page, JsonElement source, string identity, string url)
    {
        var heroId = source.GetProperty("hero_id").GetInt32();
        var position = source.GetProperty("position").GetString();
        if (position is null || !int.TryParse(position.Replace("pos ", ""), out var number) || number is < 1 or > 5)
            throw new InvalidDataException("Unknown D2PT role identity.");
        var hero = shared.GetProperty("heroesMapping").GetProperty(heroId.ToString());
        var items = shared.GetProperty("itemsMapping");
        var abilities = new Dictionary<int, string>();
        CollectAbilities(page, abilities);
        string Item(JsonElement item) => items.GetProperty(item.GetProperty("item_id").GetInt32().ToString()).GetProperty("name").GetString()!;
        string Ability(JsonElement choice) => abilities.TryGetValue(choice.GetProperty("ability_id").GetInt32(), out var name)
            ? name : throw new InvalidOperationException("D2PT did not supply an internal ability name for " + choice.GetProperty("ability_id"));
        JsonElement Popular(JsonElement choices) => choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
            ? choices.EnumerateArray().OrderByDescending(c => c.GetProperty("pick_rate").GetDouble()).ThenBy(c => c.GetProperty("ability_id").GetInt32()).First()
            : throw new GuideSourceDataException(GuideFailureKind.SourceIncomplete, "D2PT supplied an empty recommendation choice list.");
        var build = new NormalizedHeroGuideBuild
        {
            HeroId = heroId, HeroInternalName = "npc_dota_hero_" + hero.GetProperty("npc").GetString(), Role = (GuideRole)(number - 1),
            Source = "Dota2ProTracker", SourceClassification = "Live", SourceBuildId = identity,
            SourceTitle = source.GetProperty("build_label").GetString()!,
            GameplayVersion = shared.GetProperty("config").GetProperty("patches").EnumerateArray()
                .Where(p => p.GetProperty("time").GetInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                .OrderByDescending(p => p.GetProperty("time").GetInt64()).First().GetProperty("v").GetString()!,
            Matches = source.GetProperty("num_matches").GetInt32(),
            WinRate = 100d * source.GetProperty("num_wins").GetInt32() / source.GetProperty("num_matches").GetInt32(),
            SourceUrl = url, RetrievedAtUtc = DateTimeOffset.UtcNow
        };
        var startingChoices = OptionalArray(source, "starting_items");
        build.StartingInventories = startingChoices.Select((x, index) => new GuideStartingInventory(
            RequiredMatches(x, "matches"), x.TryGetProperty("win_rate", out var wr) ? ParsePickRate(wr) : null,
            index, "build-cluster:" + identity + ":" + index,
            x.GetProperty("items").EnumerateArray().Select(i => new GuideStartingItem(Item(i), RequiredQuantity(i))).ToArray())).ToList();
        var selected = SelectStartingInventory(build.StartingInventories);
        build.SelectedStartingInventoryOrder = selected?.SourceOrder;
        var startingItems = selected is null ? [] : ExpandStartingInventory(selected);
        build.ItemGroups.Add(new NormalizedGuideItemGroup { CategoryKey = "#DOTA_Item_Build_Starting_Items", ItemIds = startingItems });
        if (startingItems.Count == 0) build.AbsentOptionalSections.Add("Starting items");
        var core = source.TryGetProperty("core_items", out var coreData) ? coreData : default;
        var order = OptionalArray(core, "build_order").OrderBy(i => i.GetProperty("order_position").GetInt32()).ToArray();
        foreach (var group in new[] { ("#DOTA_Item_Build_Early_Game", 0d, 10d), ("#DOTA_Item_Build_Core_Items", 10d, 30d), ("#DOTA_Item_Build_Late_Items", 30d, double.MaxValue) })
            build.ItemGroups.Add(new NormalizedGuideItemGroup { CategoryKey = group.Item1, ItemIds = order.Where(i => i.GetProperty("median_minute").GetDouble() >= group.Item2 && i.GetProperty("median_minute").GetDouble() < group.Item3).Select(Item).ToList() });
        var coreIds = order.Select(Item).ToHashSet();
        build.ItemGroups.Add(new NormalizedGuideItemGroup { CategoryKey = "#DOTA_Item_Build_Luxury", ItemIds = OptionalArray(core, "top_items").Select(Item).Where(i => !coreIds.Contains(i)).ToList() });
        var skills = source.TryGetProperty("abilities", out var abilityData) ? abilityData : default;
        var sourceSkills = OptionalArray(skills, "skill_order").OrderBy(s => s.GetProperty("slot").GetInt32()).ToArray();
        if (!sourceSkills.Select(s => s.GetProperty("slot").GetInt32()).SequenceEqual(Enumerable.Range(1, sourceSkills.Length)) || sourceSkills.Length > 26)
            throw new GuideSourceDataException(GuideFailureKind.SourceIncomplete, "D2PT skill slots are non-contiguous or exceed the supported Valve ordinary skill slots; refusing to shift levels.");
        build.SkillCandidates = sourceSkills
            .Select(s => s.GetProperty("choices").EnumerateArray().Select(c => new GuideSkillCandidate(Ability(c), c.GetProperty("pick_rate").GetDouble())).ToList()).ToList();
        build.SkillOrder = sourceSkills.Select(s => Ability(Popular(s.GetProperty("choices")))).ToList();
        var talentRows = OptionalArray(skills, "talents").OrderBy(t => t.GetProperty("level").GetInt32()).ToArray();
        build.TalentChoices = talentRows.Select(t =>
        {
            var level = t.GetProperty("level").GetInt32();
            var candidates = t.GetProperty("choices").EnumerateArray().Select(c => new GuideTalentCandidate(level,
                Ability(c), c.GetProperty("ability_name").GetString()!, ParsePickRate(c.TryGetProperty("pick_rate", out var pr) ? pr : default),
                c.TryGetProperty("win_rate", out var wr) ? ParsePickRate(wr) : null,
                c.TryGetProperty("matches", out var matches) ? matches.GetInt32() : null,
                c.TryGetProperty("wins", out var wins) ? wins.GetInt32() : null)).ToArray();
            if (candidates.Length == 0)
                throw new GuideSourceDataException(GuideFailureKind.SourceIncomplete, "Empty D2PT talent row.");
            build.RawTalentCandidates.AddRange(candidates);
            var selected = candidates.OrderByDescending(c => c.PickRate).ThenBy(c => c.TalentId, StringComparer.Ordinal).First();
            return new NormalizedGuideTalentChoice { Level = level, TalentId = selected.TalentId, SelectedLabel = selected.Label };
        }).Where(t => t.Level is 10 or 15 or 20 or 25).ToList();
        foreach (var level in new[] { 10, 15, 20, 25 }.Except(build.TalentChoices.Select(t => t.Level)))
            build.AbsentOptionalSections.Add("Talent " + level);
        if (build.SkillOrder.Count == 0) build.AbsentOptionalSections.Add("Ability planning");
        if (build.Matches <= 0 || build.ItemGroups.Sum(g => g.ItemIds.Count) + build.SkillOrder.Count + build.TalentChoices.Count == 0)
            throw new GuideSourceDataException(GuideFailureKind.SourceIncomplete, "D2PT provides no useful guide recommendations or no source matches.");
        if (build.TalentChoices.Select(t => t.Level).Distinct().Count() != build.TalentChoices.Count)
            throw new GuideSourceDataException(GuideFailureKind.SourceIncomplete, "D2PT contains conflicting duplicate talent tiers.");
        if (source.TryGetProperty("meta", out var meta) && meta.TryGetProperty("patches", out var sourcePatches)
            && sourcePatches.GetArrayLength() > 0)
            build.GameplayVersion = sourcePatches.EnumerateArray().Select(p => p.GetProperty("version").GetString()!).OrderByDescending(v => v, StringComparer.Ordinal).First();
        build.CanonicalSourceHash = D2ptGuideProvider.ComputeCanonicalSourceHash(build);
        return build;
    }

    public static GuideStartingInventory? SelectStartingInventory(IEnumerable<GuideStartingInventory> candidates)
        => candidates.OrderByDescending(c => c.MatchCount).ThenBy(c => c.SourceOrder).FirstOrDefault();

    public static List<string> ExpandStartingInventory(GuideStartingInventory inventory)
        => inventory.Items.SelectMany(i => Enumerable.Repeat(i.ItemId, i.Quantity)).ToList();

    public static void ApplyExactStartingInventories(NormalizedHeroGuideBuild build, string heroHtml)
    {
        using var document = JsonDocument.Parse(D2ptPageDataExtractor.ExtractHydrationJson(heroHtml));
        var root = document.RootElement;
        var shared = root[0].GetProperty("data");
        var page = root.EnumerateArray().Where(n => n.ValueKind == JsonValueKind.Object)
            .Select(n => n.GetProperty("data"))
            .First(n => n.ValueKind == JsonValueKind.Object && n.TryGetProperty("buildData", out _));
        if (page.GetProperty("heroId").GetInt32() != build.HeroId)
            throw new GuideSourceDataException(GuideFailureKind.SourceIncomplete, "Opening inventory hero mismatch.");
        var position = $"pos {(int)build.Role + 1}";
        var rows = page.GetProperty("buildData").EnumerateArray()
            .Where(b => b.GetProperty("hero_id").GetInt32() == build.HeroId && b.GetProperty("position").GetString() == position
                && b.GetProperty("build_id").GetInt32() == 0).ToArray();
        if (rows.Length != 1 || !rows[0].GetProperty("build_data").TryGetProperty("starting_inventory_options", out var options)
            || options.ValueKind != JsonValueKind.Array)
            throw new GuideSourceDataException(GuideFailureKind.SourceIncomplete, "Exact role opening inventories are missing; no aggregate fallback.");
        var items = shared.GetProperty("itemsMapping");
        var inventories = options.EnumerateArray().Select((option, index) =>
        {
            if (option.ValueKind != JsonValueKind.Array || option.GetArrayLength() != 2)
                throw new GuideSourceDataException(GuideFailureKind.SourceIncomplete, "Malformed exact opening inventory.");
            var stats = option[1];
            var entries = option[0].EnumerateArray().Select(item => new GuideStartingItem(
                items.GetProperty(item.GetProperty("item_id").GetInt32().ToString()).GetProperty("name").GetString()!,
                1, item.GetProperty("slot").GetString(),
                item.TryGetProperty("charges", out var charges) ? charges.GetInt32() : null,
                item.TryGetProperty("secondary_charges", out var secondary) ? secondary.GetInt32() : null)).ToArray();
            if (entries.Length > 32 || entries.Select(i => i.Slot).Distinct().Count() != entries.Length)
                throw new GuideSourceDataException(GuideFailureKind.SourceIncomplete, "Conflicting opening inventory slots.");
            return new GuideStartingInventory(RequiredMatches(stats, "count"),
                stats.TryGetProperty("win_rate", out var rate) ? ParsePickRate(rate) : null,
                index, $"hero:{build.HeroId}:{position}:starting_inventory_options:{index}", entries);
        }).ToList();
        var selected = SelectStartingInventory(inventories);
        build.StartingInventories = inventories;
        build.SelectedStartingInventoryOrder = selected?.SourceOrder;
        build.StartingItemsVersion = NormalizedHeroGuideBuild.CurrentStartingItemsVersion;
        build.ItemGroups.Single(g => g.CategoryKey == "#DOTA_Item_Build_Starting_Items").ItemIds =
            selected is null ? [] : ExpandStartingInventory(selected);
        build.AbsentOptionalSections.Remove("Starting items");
        if (selected is null || selected.Items.Count == 0) build.AbsentOptionalSections.Add("Starting items");
        build.CanonicalSourceHash = D2ptGuideProvider.ComputeCanonicalSourceHash(build);
    }

    public static bool HasCurrentStartingInventory(NormalizedHeroGuideBuild build)
    {
        if (build.StartingItemsVersion != NormalizedHeroGuideBuild.CurrentStartingItemsVersion) return false;
        var selected = SelectStartingInventory(build.StartingInventories);
        var group = build.ItemGroups.SingleOrDefault(g => g.CategoryKey == "#DOTA_Item_Build_Starting_Items");
        return group is not null && build.SelectedStartingInventoryOrder == selected?.SourceOrder
            && build.StartingInventories.All(i => i.MatchCount > 0 && i.Items.All(x => x.Quantity is > 0 and <= 32))
            && group.ItemIds.SequenceEqual(selected is null ? [] : ExpandStartingInventory(selected));
    }

    private static int RequiredMatches(JsonElement node, string key)
    {
        if (!node.TryGetProperty(key, out var count) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var value) || value <= 0)
            throw new GuideSourceDataException(GuideFailureKind.SourceIncomplete, "Missing/invalid opening inventory match count.");
        return value;
    }

    private static int RequiredQuantity(JsonElement item)
    {
        if (!item.TryGetProperty("quantity", out var count) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var value) || value is < 1 or > 32)
            throw new GuideSourceDataException(GuideFailureKind.SourceIncomplete, "Invalid opening inventory quantity.");
        return value;
    }

    private static JsonElement[] OptionalArray(JsonElement parent, string name)
    {
        if (parent.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null || !parent.TryGetProperty(name, out var section) || section.ValueKind == JsonValueKind.Null)
            return [];
        if (section.ValueKind != JsonValueKind.Array)
            throw new GuideSourceDataException(GuideFailureKind.SourceIncomplete, "D2PT section '" + name + "' has an unexpected shape.");
        return section.EnumerateArray().ToArray();
    }

    public static double ParsePickRate(JsonElement value)
    {
        double number;
        if (value.ValueKind == JsonValueKind.Number) number = value.GetDouble();
        else if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!.Trim();
            var percent = text.EndsWith('%');
            if (!double.TryParse(text.TrimEnd('%').Trim().Replace(',', '.'),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number))
                throw new GuideSourceDataException(GuideFailureKind.SourceIncomplete, "Unusable D2PT pick rate.");
            if (percent) number /= 100;
        }
        else throw new GuideSourceDataException(GuideFailureKind.SourceIncomplete, "Missing D2PT pick rate.");
        if (!double.IsFinite(number) || number < 0 || number > 1)
            throw new GuideSourceDataException(GuideFailureKind.SourceIncomplete, "D2PT pick rate outside 0..1.");
        return number;
    }

    private static void CollectAbilities(JsonElement node, Dictionary<int, string> names)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("ability_id", out var id) && node.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.String && id.ValueKind == JsonValueKind.Number)
            {
                var value = name.GetString()!;
                if (names.TryGetValue(id.GetInt32(), out var existing) && existing != value)
                    throw new InvalidOperationException("Conflicting D2PT ability mapping.");
                names[id.GetInt32()] = value;
            }
            foreach (var property in node.EnumerateObject()) CollectAbilities(property.Value, names);
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var child in node.EnumerateArray()) CollectAbilities(child, names);
    }
}
