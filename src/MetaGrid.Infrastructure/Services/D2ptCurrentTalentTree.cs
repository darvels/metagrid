using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public static class D2ptCurrentTalentTree
{
    public static void Reconstruct(NormalizedHeroGuideBuild build, InstalledDotaGuideCatalog data)
    {
        if (build.RawTalentCandidates.Count == 0) return;
        if (build.TalentEvidence.Count == 0 && build.TalentChoices.Any(choice =>
            !build.RawTalentCandidates.Any(c => c.SourceLevel == choice.Level && c.TalentId == choice.TalentId && c.Label == choice.SelectedLabel)))
            throw new GuideSourceDataException(GuideFailureKind.TalentMapping, "Normalized talent is not present in the captured D2PT evidence.");
        if (build.RawTalentCandidates.Any(c => c.Matches < 0 || c.Wins < 0 || c.Wins > c.Matches
            || !double.IsFinite(c.PickRate) || c.PickRate is < 0 or > 1))
            throw new GuideSourceDataException(GuideFailureKind.SourceIncomplete, "Invalid D2PT talent sample counts/rates.");
        var start = int.TryParse(data.Hero.Get("AbilityTalentStart"), out var declared) ? declared : 10;
        var source = build.RawTalentCandidates.GroupBy(c => c.TalentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.SourceLevel is 10 or 15 or 20 or 25 ? 0 : 1)
                .ThenBy(c => c.SourceLevel).First(), StringComparer.Ordinal);
        var selected = new List<NormalizedGuideTalentChoice>();
        var evidence = new List<GuideTalentRowEvidence>();
        foreach (var level in new[] { 10, 15, 20, 25 })
        {
            var offset = (level - 10) / 5 * 2;
            var pair = new[] { data.Hero.Get("Ability" + (start + offset)), data.Hero.Get("Ability" + (start + offset + 1)) };
            var available = pair.Where(id => id is not null && source.ContainsKey(id)).Select(id => source[id!]).ToArray();
            if (available.Length == 0)
            {
                if (build.RawTalentCandidates.Any(c => c.SourceLevel == level))
                    throw new GuideSourceDataException(GuideFailureKind.SourcePatchIncompatible,
                        "No current Valve talent pair has source statistics at level " + level);
                continue;
            }
            if (pair.Any(id => id?.StartsWith("special_bonus_", StringComparison.Ordinal) != true || data.FormatTalentLabel(id) is null))
                throw new GuideSourceDataException(GuideFailureKind.TalentMapping, "Cannot verify current talent pair at level " + level);
            // D2PT's public build component remaps by talent identity, keeps the first
            // canonical observation, and computes percentages from the grouped counts.
            // Stale IDs may affect the denominator but cannot become a displayed candidate.
            var group = source.Values.Where(c => pair.Contains(c.TalentId)
                || (c.SourceLevel == level && !IsCurrentTalent(c.TalentId))).ToArray();
            var total = group.Sum(c => c.Matches ?? 0);
            var candidates = pair.Select(id => source.TryGetValue(id!, out var entry)
                ? entry with { PickRate = total > 0 && entry.Matches is not null ? (double)entry.Matches.Value / total : entry.PickRate }
                : new GuideTalentCandidate(level, id!, data.FormatTalentLabel(id!)!, 0, null, 0, 0)).ToArray();
            var winner = candidates.OrderByDescending(c => c.PickRate).ThenBy(c => c.TalentId, StringComparer.Ordinal).First();
            // Verify the D2PT recommendation, not a guessed replacement for its label.
            CurrentValveTalentResolver.Resolve(data, new() { Level = level, TalentId = winner.TalentId, SelectedLabel = winner.Label });
            selected.Add(new() { Level = level, TalentId = winner.TalentId, SelectedLabel = winner.Label });
            evidence.Add(new(level, candidates, winner.TalentId, "Installed Valve explicit current pair; D2PT raw ability identity and sample counts"));
        }
        build.TalentChoices = selected;
        build.TalentEvidence = evidence;
        build.AbsentOptionalSections.RemoveAll(s => s.StartsWith("Talent ", StringComparison.Ordinal));
        foreach (var level in new[] { 10, 15, 20, 25 }.Except(selected.Select(t => t.Level)))
            build.AbsentOptionalSections.Add("Talent " + level);
        build.CanonicalSourceHash = D2ptGuideProvider.ComputeCanonicalSourceHash(build);

        bool IsCurrentTalent(string id) => Enumerable.Range(start, 8).Any(slot => data.Hero.Get("Ability" + slot) == id);
    }
}
