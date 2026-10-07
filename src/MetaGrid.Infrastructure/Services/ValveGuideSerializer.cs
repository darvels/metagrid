using System.Text;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Utilities;

namespace MetaGrid.Infrastructure.Services;

public sealed class ValveGuideSerializer : IValveGuideSerializer
{
    private static readonly IReadOnlyDictionary<int, string> TalentLevelKeys = new Dictionary<int, string>
    {
        [10] = "27",
        [15] = "28",
        [20] = "29",
        [25] = "30"
    };

    public string ComputeCanonicalSourceHash(NormalizedHeroGuideBuild build)
        => D2ptGuideProvider.ComputeCanonicalSourceHash(build);

    public string ComputeEffectiveGuideHash(ResolvedHeroGuideBuild build)
    {
        var normalized = string.Join("|", new[]
        {
            build.HeroToken,
            build.RoleToken,
            build.GuideTitle,
            build.SourceBuild.GameplayVersion,
            string.Join(";", build.ItemGroups.Select(group => $"{group.CategoryKey}={string.Join(",", group.ItemIds)}")),
            string.Join(",", build.SkillOrder),
            string.Join(";", build.TalentChoices.OrderBy(choice => choice.Level).Select(choice => $"{choice.Level}:{choice.TalentId}")),
            build.Overview
        });
        return HashUtilities.Sha256(normalized);
    }

    public GuideSerializationResult Serialize(ResolvedHeroGuideBuild build, int revision, DateTimeOffset updatedAtUtc)
    {
        Validate(build);
        var effectiveHash = ComputeEffectiveGuideHash(build);
        var text = SerializeInternal(build, revision, updatedAtUtc);
        if (build.ExpectedStartingItems is not null)
        {
            StartingItemsIntegrity.RequireMatch(build.ExpectedStartingItems, StartingItemsIntegrity.GroupItems(build.ItemGroups), "pre-serialization");
            StartingItemsIntegrity.RequireMatch(build.ExpectedStartingItems, StartingItemsIntegrity.GroupItems(Parse(text).ItemGroups), "serialized roundtrip");
        }
        return new GuideSerializationResult
        {
            Bytes = Encoding.UTF8.GetBytes(text),
            Text = text,
            EffectiveGuideHash = effectiveHash
        };
    }

    public ParsedValveGuideDocument Parse(string text)
    {
        var parser = new KeyValueParser(text);
        var root = parser.ParseDocument();
        if (!string.Equals(root.Key, "guidedata", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected a guidedata root node.");
        }

        var itemsRoot = root.Children.FirstOrDefault(n => n.Key == "ItemBuild");
        var groupsRoot = itemsRoot?.Children.FirstOrDefault(n => n.Key == "Items");
        var groups = (groupsRoot?.Children ?? [])
            .Select(group => new NormalizedGuideItemGroup
            {
                CategoryKey = group.Key,
                DisplayName = group.Key,
                ItemIds = group.Children.Where(child => child.Key == "item" && child.Value is not null).Select(child => child.Value!).ToList()
            })
            .ToList();

        var abilityRoot = root.Children.FirstOrDefault(n => n.Key == "AbilityBuild");
        var orderRoot = abilityRoot?.Children.FirstOrDefault(n => n.Key == "AbilityOrder");
        var orderedEntries = (orderRoot?.Children ?? [])
            .Where(child => child.Value is not null)
            .Select(child => new { Key = int.TryParse(child.Key, out var key) ? key : int.MaxValue, Value = child.Value! })
            .OrderBy(child => child.Key)
            .ToList();

        var skillOrder = orderedEntries
            .Where(entry => entry.Key is > 0 and < 27)
            .Select(entry => entry.Value)
            .ToList();

        var talents = orderedEntries
            .Where(entry => entry.Key >= 27)
            .Select(entry => new ResolvedGuideTalentChoice
            {
                Level = entry.Key switch
                {
                    27 => 10,
                    28 => 15,
                    29 => 20,
                    30 => 25,
                    _ => 0
                },
                TalentId = entry.Value,
                SelectedLabel = entry.Value
            })
            .Where(entry => entry.Level > 0)
            .ToList();

        return new ParsedValveGuideDocument
        {
            HeroToken = GetValue(root.Children, "Hero"),
            Title = GetValue(root.Children, "Title"),
            RoleToken = TryGetValue(root.Children, "Role") ?? string.Empty,
            GameplayVersion = GetValue(root.Children, "GameplayVersion"),
            Overview = TryGetValue(root.Children, "Overview") ?? string.Empty,
            GuideRevision = int.TryParse(TryGetValue(root.Children, "GuideRevision"), out var revision) ? revision : 0,
            OriginalCreatorIdHex = TryGetValue(root.Children, "OriginalCreatorID") ?? string.Empty,
            ItemGroups = groups,
            SkillOrder = skillOrder,
            TalentChoices = talents
        };
    }

    private static void Validate(ResolvedHeroGuideBuild build)
    {
        if (string.IsNullOrWhiteSpace(build.HeroToken) || string.IsNullOrWhiteSpace(build.GuideTitle)
            || string.IsNullOrWhiteSpace(build.SourceBuild.GameplayVersion))
            throw new InvalidDataException("Guide identity/title/gameplay metadata is required.");
        if (build.ItemGroups.Select(g => g.CategoryKey).Distinct().Count() != build.ItemGroups.Count
            || build.SkillOrder.Count > 26
            || build.TalentChoices.Any(t => t.Level is not (10 or 15 or 20 or 25))
            || build.TalentChoices.Select(t => t.Level).Distinct().Count() != build.TalentChoices.Count)
            throw new InvalidDataException("Guide has duplicate groups/talent tiers or unsupported ability slots.");
        if (build.ItemGroups.SelectMany(g => g.ItemIds).Any(i => !System.Text.RegularExpressions.Regex.IsMatch(i, "^item_[a-z0-9_]+$"))
            || build.SkillOrder.Any(s => !System.Text.RegularExpressions.Regex.IsMatch(s, "^[a-z0-9_]+$"))
            || build.TalentChoices.Any(t => !System.Text.RegularExpressions.Regex.IsMatch(t.TalentId, "^special_bonus_[a-z0-9_]+$")))
            throw new InvalidDataException("Only mapped internal item/ability/talent tokens may be serialized.");
        if (build.ItemGroups.Sum(g => g.ItemIds.Count) + build.SkillOrder.Count + build.TalentChoices.Count == 0)
            throw new InvalidDataException("Guide contains no useful recommendations.");
    }

    private static string SerializeInternal(ResolvedHeroGuideBuild build, int revision, DateTimeOffset updatedAtUtc)
    {
        var builder = new StringBuilder();
        builder.AppendLine("\"guidedata\"");
        builder.AppendLine("{");
        AppendPair(builder, 1, "Hero", build.HeroToken);
        AppendPair(builder, 1, "Title", build.GuideTitle);
        AppendPair(builder, 1, "Role", build.RoleToken);
        AppendPair(builder, 1, "GameplayVersion", build.SourceBuild.GameplayVersion);
        AppendPair(builder, 1, "Overview", build.Overview);
        AppendPair(builder, 1, "GuideRevision", revision.ToString());
        AppendPair(builder, 1, "AssociatedWorkshopItemID", "0x0000000000000000");
        AppendPair(builder, 1, "OriginalCreatorID", build.OriginalCreatorIdHex);
        AppendPair(builder, 1, "GuideFormatVersion", "2");
        AppendPair(builder, 1, "TimeUpdated", $"0x{updatedAtUtc.ToUnixTimeSeconds():X16}");
        AppendPair(builder, 1, "TimePublished", "0x0000000000000000");

        AppendBlockStart(builder, 1, "ItemBuild");
        AppendBlockStart(builder, 2, "Items");
        foreach (var group in build.ItemGroups)
        {
            AppendBlockStart(builder, 3, group.CategoryKey);
            foreach (var item in group.ItemIds)
            {
                AppendPair(builder, 4, "item", item);
            }
            AppendBlockEnd(builder, 3);
        }
        AppendBlockEnd(builder, 2);
        AppendBlockStart(builder, 2, "ItemTooltips");
        AppendBlockEnd(builder, 2);
        AppendBlockEnd(builder, 1);

        AppendBlockStart(builder, 1, "AbilityBuild");
        AppendBlockStart(builder, 2, "AbilityOrder");
        var skillLevel = 1;
        foreach (var skill in build.SkillOrder)
        {
            AppendPair(builder, 3, skillLevel.ToString(), skill);
            skillLevel++;
        }
        foreach (var talent in build.TalentChoices.OrderBy(choice => choice.Level))
        {
            if (TalentLevelKeys.TryGetValue(talent.Level, out var key))
            {
                AppendPair(builder, 3, key, talent.TalentId);
            }
        }
        AppendBlockEnd(builder, 2);
        AppendBlockStart(builder, 2, "AbilityTooltips");
        AppendBlockEnd(builder, 2);
        AppendBlockEnd(builder, 1);
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendPair(StringBuilder builder, int indent, string key, string value)
        => builder.Append('\t', indent).Append('"').Append(Escape(key)).Append("\"\t\t\"").Append(Escape(value)).AppendLine("\"");

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");

    private static void AppendBlockStart(StringBuilder builder, int indent, string key)
    {
        builder.Append('\t', indent).Append('"').Append(Escape(key)).AppendLine("\"");
        builder.Append('\t', indent).AppendLine("{");
    }

    private static void AppendBlockEnd(StringBuilder builder, int indent)
        => builder.Append('\t', indent).AppendLine("}");

    private static KeyValueNode GetChild(IReadOnlyList<KeyValueNode> nodes, string key)
        => nodes.FirstOrDefault(node => string.Equals(node.Key, key, StringComparison.Ordinal))
           ?? throw new InvalidOperationException($"Expected a '{key}' node.");

    private static string GetValue(IReadOnlyList<KeyValueNode> nodes, string key)
        => TryGetValue(nodes, key) ?? throw new InvalidOperationException($"Expected a '{key}' value.");

    private static string? TryGetValue(IReadOnlyList<KeyValueNode> nodes, string key)
        => nodes.FirstOrDefault(node => string.Equals(node.Key, key, StringComparison.Ordinal))?.Value;

    private sealed record KeyValueNode(string Key, string? Value, IReadOnlyList<KeyValueNode> Children);

    private sealed class KeyValueParser(string text)
    {
        private readonly List<Token> _tokens = Tokenize(text);
        private int _index;

        public KeyValueNode ParseDocument()
        {
            var key = ReadString();
            ReadSymbol('{');
            var children = ParseChildren();
            ReadSymbol('}');
            return new KeyValueNode(key, null, children);
        }

        private List<KeyValueNode> ParseChildren()
        {
            var children = new List<KeyValueNode>();
            while (!IsSymbol('}'))
            {
                var key = ReadString();
                if (IsSymbol('{'))
                {
                    ReadSymbol('{');
                    var nested = ParseChildren();
                    ReadSymbol('}');
                    children.Add(new KeyValueNode(key, null, nested));
                    continue;
                }

                var value = ReadString();
                children.Add(new KeyValueNode(key, value, []));
            }

            return children;
        }

        private bool IsSymbol(char symbol)
            => _index < _tokens.Count && _tokens[_index] is SymbolToken token && token.Symbol == symbol;

        private string ReadString()
        {
            if (_index >= _tokens.Count || _tokens[_index] is not StringToken token)
            {
                throw new InvalidOperationException("Expected a string token.");
            }

            _index++;
            return token.Value;
        }

        private void ReadSymbol(char symbol)
        {
            if (_index >= _tokens.Count || _tokens[_index] is not SymbolToken token || token.Symbol != symbol)
            {
                throw new InvalidOperationException($"Expected symbol '{symbol}'.");
            }

            _index++;
        }

        private static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            for (var index = 0; index < text.Length; index++)
            {
                var current = text[index];
                if (char.IsWhiteSpace(current))
                {
                    continue;
                }

                if (current is '{' or '}')
                {
                    tokens.Add(new SymbolToken(current));
                    continue;
                }

                if (current != '"')
                {
                    continue;
                }

                var builder = new StringBuilder();
                index++;
                while (index < text.Length)
                {
                    current = text[index];
                    if (current == '\\' && index + 1 < text.Length)
                    {
                        builder.Append(text[index + 1]);
                        index += 2;
                        continue;
                    }

                    if (current == '"')
                    {
                        break;
                    }

                    builder.Append(current);
                    index++;
                }

                tokens.Add(new StringToken(builder.ToString()));
            }

            return tokens;
        }

        private abstract record Token;
        private sealed record StringToken(string Value) : Token;
        private sealed record SymbolToken(char Symbol) : Token;
    }
}
