using System.Text.Json;
using System.Text.RegularExpressions;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Utilities;

namespace MetaGrid.Infrastructure.Services;

public sealed class DotaGuideMappingResolver(Func<SteamAccount, NormalizedHeroGuideBuild, CancellationToken, InstalledDotaGuideCatalog>? catalogLoader = null,
    bool allowReferenceData = false) : IDotaGuideMappingResolver
{
    private readonly Lazy<GuideMappingManifest> _manifest = new(LoadManifest);

    public Task<DotaGuideMappingResult> ResolveAsync(SteamAccount account, GuideSubscriptionRecord subscription, NormalizedHeroGuideBuild build, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The bundled manifest is reference data, not proof of compatibility with installed Dota.
        if (build.SourceClassification is "Live" or "Cached live")
            return Task.Run(() => ResolveInstalled(account, subscription, build, cancellationToken, catalogLoader), cancellationToken);
        if (!allowReferenceData)
            return Task.FromResult(DotaGuideMappingResult.Failure("Only Live or valid Cached live data can pass production mapping. No Steam write was performed."));

        if (!_manifest.Value.Heroes.TryGetValue(build.HeroInternalName, out var hero))
        {
            return Task.FromResult(DotaGuideMappingResult.Failure($"Current Dota mapping did not contain hero '{build.HeroInternalName}'."));
        }

        if (!hero.SupportedRoles.TryGetValue(build.Role.ToString(), out var role))
        {
            return Task.FromResult(DotaGuideMappingResult.Failure($"Current Dota mapping did not contain role '{build.Role}' for '{build.HeroInternalName}'."));
        }

        foreach (var item in build.ItemGroups.SelectMany(group => group.ItemIds))
        {
            if (!hero.Items.Contains(item))
            {
                return Task.FromResult(DotaGuideMappingResult.Failure($"Current Dota mapping could not resolve item '{item}'."));
            }
        }

        foreach (var ability in build.SkillOrder)
        {
            if (!hero.Abilities.Contains(ability))
            {
                return Task.FromResult(DotaGuideMappingResult.Failure($"Current Dota mapping could not resolve ability '{ability}'."));
            }
        }

        var resolvedTalents = new List<ResolvedGuideTalentChoice>();
        foreach (var talent in build.TalentChoices.OrderBy(choice => choice.Level))
        {
            if (!hero.Talents.Contains(talent.TalentId))
            {
                return Task.FromResult(DotaGuideMappingResult.Failure($"Current Dota mapping could not resolve talent '{talent.TalentId}' at level {talent.Level}."));
            }

            resolvedTalents.Add(new ResolvedGuideTalentChoice
            {
                Level = talent.Level,
                TalentId = talent.TalentId,
                SelectedLabel = talent.SelectedLabel
            });
        }

        if (!long.TryParse(account.AccountId, out var accountId))
        {
            return Task.FromResult(DotaGuideMappingResult.Failure($"Steam account id '{account.AccountId}' was not numeric and could not be converted into OriginalCreatorID."));
        }

        var resolved = new ResolvedHeroGuideBuild
        {
            SourceBuild = build,
            HeroToken = hero.GuideHeroToken,
            RoleToken = role.GuideRoleToken,
            GuideTitle = "MetaGrid D2PT - Io Mid",
            Overview = "MetaGrid owner=IoMid source=Dota2ProTracker strategy=CurrentReferenceArtifact role=Mid",
            OriginalCreatorIdHex = $"0x{accountId:X16}",
            ItemGroups = build.ItemGroups,
            SkillOrder = build.SkillOrder,
            TalentChoices = resolvedTalents,
            EffectiveGuideHash = string.Empty
        };

        return Task.FromResult(DotaGuideMappingResult.Success(resolved));
    }

    private static GuideMappingManifest LoadManifest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Guides", "current-dota-guide-mappings.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<GuideMappingManifest>(json, JsonDefaults.Storage)
            ?? throw new InvalidOperationException("The current Dota guide mapping manifest could not be loaded.");
    }

    public static DotaGuideMappingResult ResolveInstalled(SteamAccount account, GuideSubscriptionRecord subscription,
        NormalizedHeroGuideBuild build, CancellationToken token,
        Func<SteamAccount, NormalizedHeroGuideBuild, CancellationToken, InstalledDotaGuideCatalog>? loader = null)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (subscription.HeroId != build.HeroId || subscription.Role != build.Role || subscription.HeroInternalName != build.HeroInternalName
                || build.Source != "Dota2ProTracker" || build.SourceClassification is not ("Live" or "Cached live")
                || build.RetrievedAtUtc > DateTimeOffset.UtcNow || DateTimeOffset.UtcNow - build.RetrievedAtUtc > TimeSpan.FromHours(24))
                throw new InvalidDataException("Subscription/source identity or live-source freshness validation failed.");
            var data = loader is null ? InstalledDotaGuideCatalog.Load(account.SteamRootPath, build.HeroInternalName, token) : loader(account, build, token);
            if (data.Hero.Get("HeroID") != build.HeroId.ToString()) throw new InvalidDataException("Installed hero ID mismatch.");
            D2ptCurrentTalentTree.Reconstruct(build, data);
            var roleToken = build.Role is GuideRole.Support or GuideRole.HardSupport ? "DOTA_HeroGuide_Role_Support" : "DOTA_HeroGuide_Role_Core";
            if (!data.Labels.ContainsKey(roleToken)) throw new InvalidDataException("Installed Dota role token missing: " + roleToken);
            foreach (var group in build.ItemGroups.Where(g => g.CategoryKey.StartsWith('#')))
                if (!data.Labels.ContainsKey(group.CategoryKey[1..])) throw new InvalidDataException("Installed Dota category token missing: " + group.CategoryKey);
            if (string.IsNullOrWhiteSpace(build.GameplayVersion) || build.ItemGroups.Select(g => g.CategoryKey).Distinct().Count() != build.ItemGroups.Count
                || build.ItemGroups.Sum(g => g.ItemIds.Count) + build.SkillOrder.Count + build.TalentChoices.Count == 0 || build.SkillOrder.Count > 26)
                throw new GuideSourceDataException(GuideFailureKind.SourceIncomplete, "Guide has invalid metadata, duplicate groups, no useful recommendations or too many skill slots.");
            var abilities = data.Hero.Children.Where(n => Regex.IsMatch(n.Key, "^Ability[0-9]+$") && n.Value?.StartsWith("special_bonus_") != true).Select(n => n.Value).ToHashSet();
            var ranks = new Dictionary<string, int>();
            var selectedSkills = new List<string>();
            var legalSourceOrder = build.SkillCandidates.Count == build.SkillOrder.Count
                ? SelectLegalSourceOrder(build, data, abilities, token) : build.SkillOrder;
            for (var i = 0; i < build.SkillOrder.Count; i++)
            {
                var skill = legalSourceOrder[i];
                if (!abilities.Contains(skill)) throw new InvalidDataException($"Unknown hero skill at level {i + 1}: {skill}");
                var definition = data.Hero.Child("AbilityDefinitions").Child(skill);
                if (definition.Get("AbilityBehavior")?.Contains("NOT_LEARNABLE") == true) throw new InvalidDataException("Unlearnable skill: " + skill);
                ranks.TryGetValue(skill, out var rank);
                rank++;
                if (!IsLegalRank(definition, rank, i + 1))
                    throw new InvalidDataException($"Illegal current-Dota skill rank at level {i + 1}: {skill}");
                ranks[skill] = rank;
                selectedSkills.Add(skill);
            }
            if (build.TalentChoices.Any(t => t.Level is not (10 or 15 or 20 or 25))
                || build.TalentChoices.Select(t => t.Level).Distinct().Count() != build.TalentChoices.Count)
                throw new GuideSourceDataException(GuideFailureKind.SourceIncomplete, "Provided talent tiers must be valid and unique.");
            var resolvedTalents = new List<ResolvedGuideTalentChoice>();
            foreach (var talent in build.TalentChoices)
            {
                resolvedTalents.Add(CurrentValveTalentResolver.Resolve(data, talent));
            }
            if (!uint.TryParse(account.AccountId, out var accountId)) throw new InvalidDataException("Invalid Steam account ID.");
            var expectedStarting = build.StartingItemsVersion > 0 ? StartingItemsIntegrity.ResolveExpected(build, data) : null;
            var resolvedGroups = build.ItemGroups.Select(g => new NormalizedGuideItemGroup { CategoryKey = g.CategoryKey, DisplayName = g.DisplayName,
                ItemIds = g.ItemIds.Select(data.ResolveItem).ToList() }).ToList();
            if (expectedStarting is not null)
            {
                resolvedGroups.Single(g => g.CategoryKey == StartingItemsIntegrity.Category).ItemIds = expectedStarting.ToList();
                StartingItemsIntegrity.RequireMatch(expectedStarting, StartingItemsIntegrity.GroupItems(resolvedGroups), "Valve resolution");
            }
            var resolved = new ResolvedHeroGuideBuild
            {
                ExpectedStartingItems = expectedStarting,
                SourceBuild = build, HeroToken = build.HeroInternalName.Replace("npc_dota_hero_", ""),
                RoleToken = "#" + roleToken, GuideTitle = "MetaGrid D2PT - " + subscription.HeroDisplayName + " " + build.Role,
                Overview = $"MetaGrid owner={subscription.HeroDisplayName.Replace(" ", "")}{build.Role} heroId={build.HeroId} source=Dota2ProTracker role={build.Role}", OriginalCreatorIdHex = $"0x{(76561197960265728UL + accountId):X16}",
                SkillOrder = selectedSkills,
                ItemGroups = resolvedGroups,
                TalentChoices = resolvedTalents
            };
            if (expectedStarting is not null)
                _ = new ValveGuideSerializer().Serialize(resolved, 1, DateTimeOffset.UtcNow);
            return DotaGuideMappingResult.Success(resolved);
        }
        catch (GuideSourceDataException ex)
        {
            return DotaGuideMappingResult.Failure(ex.Message + " No Steam write was performed.", ex.Kind);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DotaGuideMappingResult.Failure(ex.Message + " No Steam write was performed.");
        }
    }

    private static bool IsLegalRank(ValveDataNode definition, int rank, int level)
    {
        if (definition.Get("AbilityBehavior")?.Contains("NOT_LEARNABLE") == true) return false;
        var ultimate = definition.Get("AbilityType") == "ABILITY_TYPE_ULTIMATE";
        var maxLevel = int.TryParse(definition.Get("MaxLevel"), out var declaredMax) ? declaredMax : ultimate ? 3 : 4;
        var requiredLevel = int.TryParse(definition.Get("RequiredLevel"), out var declaredRequired) ? declaredRequired : ultimate ? 6 : 1;
        var increment = int.TryParse(definition.Get("LevelsBetweenUpgrades"), out var declaredIncrement) ? declaredIncrement : ultimate ? 6 : 2;
        return rank > 0 && rank <= maxLevel && level >= requiredLevel + (rank - 1) * increment;
    }

    private static List<string> SelectLegalSourceOrder(NormalizedHeroGuideBuild build, InstalledDotaGuideCatalog data,
        HashSet<string?> abilities, CancellationToken token)
    {
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        var memo = new Dictionary<string, (double Score, List<string>? Path)>();
        (double Score, List<string>? Path) Search(int slot)
        {
            token.ThrowIfCancellationRequested();
            if (slot == build.SkillCandidates.Count) return (0, []);
            var key = slot + ":" + string.Join(",", ranks.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value));
            if (memo.TryGetValue(key, out var cached)) return cached;
            (double Score, List<string>? Path) best = (double.NegativeInfinity, null);
            foreach (var candidate in build.SkillCandidates[slot].OrderBy(c => c.Ability, StringComparer.Ordinal))
            {
                if (!abilities.Contains(candidate.Ability) || !double.IsFinite(candidate.PickRate) || candidate.PickRate <= 0) continue;
                var old = ranks.GetValueOrDefault(candidate.Ability);
                var definition = data.Hero.Child("AbilityDefinitions").Child(candidate.Ability);
                if (!IsLegalRank(definition, old + 1, slot + 1)) continue;
                ranks[candidate.Ability] = old + 1;
                var suffix = Search(slot + 1);
                if (old == 0) ranks.Remove(candidate.Ability); else ranks[candidate.Ability] = old;
                var score = Math.Log(candidate.PickRate) + suffix.Score;
                if (suffix.Path is not null && score > best.Score) best = (score, [candidate.Ability, .. suffix.Path]);
            }
            memo[key] = best;
            return best;
        }
        return Search(0).Path ?? throw new InvalidDataException("No legal complete source skill order: "
            + string.Join(",", build.SkillCandidates.SelectMany(s => s).Select(c => c.Ability).Distinct()));
    }

    private sealed class GuideMappingManifest
    {
        public Dictionary<string, GuideMappingHero> Heroes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class GuideMappingHero
    {
        public string GuideHeroToken { get; set; } = string.Empty;
        public Dictionary<string, GuideMappingRole> SupportedRoles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Abilities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Talents { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class GuideMappingRole
    {
        public string GuideRoleToken { get; set; } = string.Empty;
    }
}
