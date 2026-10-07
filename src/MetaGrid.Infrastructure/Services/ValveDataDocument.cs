using System.Text.RegularExpressions;

namespace MetaGrid.Infrastructure.Services;

public sealed record ValveDataNode(string Key, string? Value, IReadOnlyList<ValveDataNode> Children)
{
    public string? Get(string key) => Children.FirstOrDefault(n => n.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;
    public ValveDataNode Child(string key) => Children.Single(n => n.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
}

public static class ValveDataDocument
{
    public static IReadOnlyList<ValveDataNode> Parse(string text)
    {
        var tokens = Regex.Matches(text.TrimStart('\uFEFF'), "//[^\\r\\n]*|\"(?:\\\\.|[^\"\\\\])*\"|[{}]|[^\\s{}\"]+")
            .Select(m => m.Value).Where(t => !t.StartsWith("//")).ToArray();
        var index = 0;
        string Read()
        {
            if (index >= tokens.Length) throw new InvalidDataException("Unexpected end of Valve document.");
            var token = tokens[index++];
            return token.StartsWith('"') ? token[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\") : token;
        }
        List<ValveDataNode> Children(bool nested)
        {
            var result = new List<ValveDataNode>();
            while (index < tokens.Length && tokens[index] != "}")
            {
                var key = Read();
                if (index < tokens.Length && tokens[index] == "{")
                {
                    index++;
                    result.Add(new(key, null, Children(true)));
                }
                else result.Add(new(key, Read(), []));
            }
            if (nested)
            {
                if (index >= tokens.Length || tokens[index++] != "}") throw new InvalidDataException("Unbalanced Valve document.");
            }
            return result;
        }
        return Children(false);
    }
}
