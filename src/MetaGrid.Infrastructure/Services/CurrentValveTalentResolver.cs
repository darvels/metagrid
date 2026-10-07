using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public static class CurrentValveTalentResolver
{
    public static ResolvedGuideTalentChoice Resolve(InstalledDotaGuideCatalog data, NormalizedGuideTalentChoice choice)
    {
        if (choice.Level is not (10 or 15 or 20 or 25))
            throw new GuideSourceDataException(GuideFailureKind.SourceIncomplete, "Invalid source talent tier: " + choice.Level);
        var start = int.TryParse(data.Hero.Get("AbilityTalentStart"), out var declared) ? declared : 10;
        var offset = (choice.Level - 10) / 5 * 2;
        var ids = new[] { data.Hero.Get("Ability" + (start + offset)), data.Hero.Get("Ability" + (start + offset + 1)) };
        var candidates = ids.Where(id => id?.StartsWith("special_bonus_", StringComparison.Ordinal) == true && data.FormatTalentLabel(id) is not null)
            .Select(id => (Id: id!, Label: data.FormatTalentLabel(id!)!)).ToArray();
        // An exact current tier ID preserves the source's chosen talent despite balance-value changes.
        var selected = candidates.FirstOrDefault(c => c.Id == choice.TalentId);
        if (selected.Id is null)
        {
            if (candidates.Length != 2 || candidates.Select(c => c.Id).Distinct().Count() != 2)
                throw new GuideSourceDataException(GuideFailureKind.TalentMapping, "Current Valve talent tier could not be verified at level " + choice.Level);
            var equivalent = candidates.Where(c => !c.Label.Contains("{s:", StringComparison.Ordinal)
                && NormalizeLabel(c.Label) == NormalizeLabel(choice.SelectedLabel)).ToArray();
            if (equivalent.Length > 1)
                throw new GuideSourceDataException(GuideFailureKind.TalentMapping, "Ambiguous current talent label at level " + choice.Level + ": " + choice.SelectedLabel);
            if (equivalent.Length == 0)
                throw new GuideSourceDataException(GuideFailureKind.SourcePatchIncompatible,
                    $"D2PT/current-patch incompatibility at talent level {choice.Level}: source {choice.TalentId} '{choice.SelectedLabel}'; current [{string.Join("; ", candidates.Select(c => c.Id + " '" + c.Label + "'"))}].");
            selected = equivalent[0];
        }
        return new ResolvedGuideTalentChoice { Level = choice.Level, TalentId = selected.Id, SelectedLabel = choice.SelectedLabel };
    }

    public static string NormalizeLabel(string label)
    {
        var text = WebUtility.HtmlDecode(label).ToLowerInvariant().Replace('\u2212', '-').Replace('\u2013', '-').Replace('\u2014', '-');
        text = Regex.Replace(text, @"(?<=\d),(?=\d)", ".");
        text = Regex.Replace(text, @"[+\-]?\d+(?:\.\d+)?", m => decimal.TryParse(m.Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value) ? value.ToString("0.################", CultureInfo.InvariantCulture) : m.Value);
        return new string(text.Where(c => char.IsLetterOrDigit(c) || c is '%' or '.' or '-' or '+').ToArray());
    }
}
