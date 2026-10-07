using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;

namespace MetaGrid.Tests;

public sealed class InstalledGuideMappingTests
{
    internal static readonly string[] Items = ["item_branches", "item_bottle", "item_magic_wand", "item_null_talisman",
        "item_ultimate_scepter", "item_black_king_bar", "item_shivas_guard", "item_octarine_core", "item_heart", "item_sheepstick",
        "item_aeon_disk", "item_cyclone", "item_rod_of_atos", "item_lesser_crit", "item_consecrated_wraps", "item_lotus_orb",
        "item_wind_waker", "item_nullifier", "item_aghanims_shard"];
    internal static readonly string[] Skills = ["wisp_spirits", "wisp_tether", "wisp_spirits", "wisp_overcharge", "wisp_spirits",
        "wisp_relocate", "wisp_spirits", "wisp_overcharge", "wisp_overcharge", "wisp_overcharge"];
    internal static readonly string[] Talents = ["special_bonus_unique_wisp_overcharge_duration", "special_bonus_unique_wisp",
        "special_bonus_unique_wisp_6", "special_bonus_unique_wisp_relocate_delay"];
    private static readonly string[] TalentLabels = ["+1.5s Overcharge Duration", "+40% Spirits Damage", "-30s Relocate Cooldown", "-2s Relocate Cast Delay"];
    private static readonly string[] Categories = ["#DOTA_Item_Build_Starting_Items", "#DOTA_Item_Build_Early_Game", "#DOTA_Item_Build_Core_Items", "#DOTA_Item_Build_Late_Items", "#DOTA_Item_Build_Luxury"];
    internal static InstalledDotaGuideCatalog Catalog()
    {
        var nodes = new List<ValveDataNode> { new("HeroID", "91", []) };
        var abilities = new[] { "wisp_tether", "wisp_spirits", "wisp_overcharge", "wisp_relocate" };
        for (var i = 0; i < abilities.Length; i++) nodes.Add(new("Ability" + (i + 1), abilities[i], []));
        for (var i = 0; i < Talents.Length; i++)
        {
            nodes.Add(new("Ability" + (10 + i * 2), Talents[i], []));
            nodes.Add(new("Ability" + (11 + i * 2), "other_talent_" + i, []));
        }
        nodes.Add(new("AbilityDefinitions", null, abilities.Select(a => new ValveDataNode(a, null,
            a == "wisp_relocate" ? [new("AbilityType", "ABILITY_TYPE_ULTIMATE", [])] : [])).ToArray()));
        var labels = Talents.ToDictionary(t => "DOTA_Tooltip_ability_" + t, t => t, StringComparer.OrdinalIgnoreCase);
        labels.Add("DOTA_Tooltip_ability_item_aghanims_shard", "Aghanim's Shard");
        labels.Add("DOTA_HeroGuide_Role_Core", "Core");
        foreach (var category in Categories) labels.Add(category[1..], category);
        return new() { Hero = new("npc_dota_hero_wisp", null, nodes), Items = Items.ToDictionary(i => i, i => new ValveDataNode(i, null, [])), Labels = labels, Evidence = "Deterministic test snapshot; never live Dota" };
    }
    internal static NormalizedHeroGuideBuild Build() => new()
    {
        HeroId = 91, HeroInternalName = "npc_dota_hero_wisp", Role = GuideRole.Mid, Source = "Dota2ProTracker",
        SourceClassification = "Live", RetrievedAtUtc = DateTimeOffset.UtcNow, GameplayVersion = "7.41f",
        Matches = 16, WinRate = 50, SourceBuildId = "io-pos2-1", SourceTitle = "Aghanim's Scepter Core",
        SkillOrder = Skills.ToList(), TalentChoices = Talents.Select((t, i) => new NormalizedGuideTalentChoice { Level = 10 + i * 5, TalentId = t, SelectedLabel = TalentLabels[i] }).ToList(),
        ItemGroups = Enumerable.Range(0, 5).Select(i => new NormalizedGuideItemGroup { CategoryKey = Categories[i],
            ItemIds = i == 0 ? Enumerable.Repeat("item_branches", 5).ToList()
                : i == 4 ? Items.Skip(10).Select(t => t == "item_aghanims_shard" ? "Aghanim's Shard" : t).ToList()
                : Items.Skip(1 + (i - 1) * 3).Take(3).ToList() }).ToList()
    };
    internal static SteamAccount Account() => new() { AccountId = "123", SteamRootPath = "fixture", UserDataPath = "fixture", DotaConfigDirectory = "fixture" };
    internal static GuideSubscriptionRecord Subscription() => new() { HeroId = 91, HeroInternalName = "npc_dota_hero_wisp", Role = GuideRole.Mid, IsEnabled = true };
    internal static DotaGuideMappingResolver Resolver() => new((_, _, _) => Catalog());

    [Fact]
    public async Task AllTenSkillsAndFourTalents_PreserveExactLiveOrder()
    {
        var result = await Resolver().ResolveAsync(Account(), Subscription(), Build(), default);
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(Skills, result.Build!.SkillOrder);
        Assert.Equal(Talents, result.Build.TalentChoices.Select(t => t.TalentId));
        var serializer = new ValveGuideSerializer();
        Assert.Equal("6B2F610E91BD0D471C5EA13EE210500F66495CD2296AB3CC6F7E36B12681BC88", serializer.ComputeCanonicalSourceHash(Build()));
        var parsed = serializer.Parse(serializer.Serialize(result.Build, 1, DateTimeOffset.UtcNow).Text);
        Assert.Equal(23, parsed.ItemGroups.Sum(g => g.ItemIds.Count));
        Assert.Equal(Items.Order(), parsed.ItemGroups.SelectMany(g => g.ItemIds).Distinct().Order());
        Assert.Equal(Skills, parsed.SkillOrder);
        Assert.Equal(Talents, parsed.TalentChoices.Select(t => t.TalentId));
    }
    public static IEnumerable<object[]> ItemCases() => Items.Select(i => new object[] { i });
    [Theory]
    [MemberData(nameof(ItemCases))]
    public void EveryCurrentItem_ResolvesExactly(string id) => Assert.Equal(id, Catalog().ResolveItem(id));
    [Fact]
    public void Shard_UsesCurrentDisplayLookup() => Assert.Equal("item_aghanims_shard", Catalog().ResolveItem("Aghanim's Shard"));
    [Fact]
    public void AmbiguousDisplayLookup_RefusesMapping()
    {
        var data = Catalog();
        var labels = (Dictionary<string, string>)data.Labels;
        labels["DOTA_Tooltip_ability_item_bottle"] = "Aghanim's Shard";
        Assert.Throws<InvalidDataException>(() => data.ResolveItem("Aghanim's Shard"));
    }
    [Theory]
    [InlineData("item")]
    [InlineData("skill")]
    [InlineData("talent")]
    [InlineData("level")]
    public async Task StrictGate_RejectsAnyUnresolvedIncludedToken(string kind)
    {
        var build = Build();
        if (kind == "item") build.ItemGroups[4].ItemIds.Add("unknown_item");
        if (kind == "skill") build.SkillOrder[9] = "unknown_skill";
        if (kind == "talent") build.TalentChoices[3].TalentId = "unknown_talent";
        if (kind == "level") build.TalentChoices[3].Level = 20;
        var result = await Resolver().ResolveAsync(Account(), Subscription(), build, default);
        Assert.False(result.Succeeded);
        Assert.Null(result.Build);
    }
    [Theory]
    [InlineData("Cached live")]
    [InlineData("Live")]
    public async Task ExpiredSource_RejectsWrite(string classification)
    {
        var build = Build(); build.SourceClassification = classification; build.RetrievedAtUtc = DateTimeOffset.UtcNow.AddHours(-25);
        Assert.False((await Resolver().ResolveAsync(Account(), Subscription(), build, default)).Succeeded);
    }

    [Fact]
    public async Task IllegalUltimateLevel_RejectsWrite()
    {
        var build = Build(); build.SkillOrder[0] = "wisp_relocate";
        Assert.False((await Resolver().ResolveAsync(Account(), Subscription(), build, default)).Succeeded);
    }

    [Theory]
    [InlineData("Live", true)]
    [InlineData("Cached live", true)]
    [InlineData("Fixture", false)]
    [InlineData("Unavailable", false)]
    public void ProductionGate_ClassifiesSourceHonestly(string classification, bool expected)
    {
        var build = Build(); build.SourceClassification = classification;
        Assert.Equal(expected, DotaGuideMappingResolver.ResolveInstalled(Account(), Subscription(), build, default, (_, _, _) => Catalog()).Succeeded);
    }
}
