using System.Text;
using System.Text.Json;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public static class D2ptPageDataExtractor
{
    public static string ExtractHydrationJson(string html)
    {
        if (LooksLikeCloudflareChallenge(html)) throw new InvalidOperationException("D2PT blocked by Cloudflare.");
        var script = ExtractHydrationScript(html) ?? throw new InvalidOperationException("SvelteKit hydration script missing.");
        var data = ExtractBracketedArray(script, "data:") ?? throw new InvalidOperationException("SvelteKit page data missing.");
        return NormalizeJavaScriptLiteralToJson(data);
    }
    public static D2ptPageDataExtractionResult ExtractMatchesWr(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return D2ptPageDataExtractionResult.Failure("UnexpectedResponse", "The D2PT response body was empty.");
        }

        if (LooksLikeCloudflareChallenge(html))
        {
            return D2ptPageDataExtractionResult.Failure("CloudflareChallenge", "Cloudflare challenge page was returned instead of the Meta Hero Grid page.");
        }

        var hydrationScript = ExtractHydrationScript(html);
        if (hydrationScript is null)
        {
            return D2ptPageDataExtractionResult.Failure("PageDataMissing", "Could not locate the inline SvelteKit hydration script.");
        }

        var dataArrayLiteral = ExtractBracketedArray(hydrationScript, "data:");
        if (dataArrayLiteral is null)
        {
            return D2ptPageDataExtractionResult.Failure("PageDataMissing", "Could not locate the SvelteKit data:[...] payload.");
        }

        string normalizedJson;
        try
        {
            normalizedJson = NormalizeJavaScriptLiteralToJson(dataArrayLiteral);
        }
        catch (Exception ex)
        {
            return D2ptPageDataExtractionResult.Failure("PageDataDecodeFailed", $"The embedded page-data payload could not be normalized into JSON: {ex.Message}");
        }

        try
        {
            using var payloadDocument = JsonDocument.Parse(normalizedJson);
            if (!TryFindObjectWithProperty(payloadDocument.RootElement, "grids", out var pageDataObject))
            {
                return D2ptPageDataExtractionResult.Failure("PageDataMissing", "The decoded page-data payload does not contain a grids object.");
            }

            return ExtractFromPageDataObject(pageDataObject, hydrationScript.Length, dataArrayLiteral.Length);
        }
        catch (JsonException ex)
        {
            return D2ptPageDataExtractionResult.Failure("PageDataDecodeFailed", $"The normalized page-data payload could not be parsed as JSON: {ex.Message}");
        }
    }

    public static D2ptPageDataExtractionResult ExtractMatchesWrFromPageDataObjectJson(string pageDataObjectJson)
    {
        if (string.IsNullOrWhiteSpace(pageDataObjectJson))
        {
            return D2ptPageDataExtractionResult.Failure("PageDataMissing", "The runtime page-data JSON was empty.");
        }

        try
        {
            using var document = JsonDocument.Parse(pageDataObjectJson);
            return ExtractFromPageDataObject(document.RootElement, 0, 0);
        }
        catch (JsonException ex)
        {
            return D2ptPageDataExtractionResult.Failure("PageDataDecodeFailed", $"The runtime page-data object could not be parsed as JSON: {ex.Message}");
        }
    }

    public static bool LooksLikeCloudflareChallenge(string html)
        => html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
           html.Contains("Enable JavaScript and cookies to continue", StringComparison.OrdinalIgnoreCase) ||
           html.Contains("Performing security verification", StringComparison.OrdinalIgnoreCase) ||
           html.Contains("cf-turnstile-response", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractHydrationScript(string html)
    {
        const string anchor = "__sveltekit";
        var anchorIndex = html.IndexOf(anchor, StringComparison.Ordinal);
        if (anchorIndex < 0)
        {
            return null;
        }

        var scriptStart = html.LastIndexOf("<script", anchorIndex, StringComparison.OrdinalIgnoreCase);
        if (scriptStart < 0)
        {
            return null;
        }

        var contentStart = html.IndexOf('>', scriptStart);
        if (contentStart < 0)
        {
            return null;
        }

        var scriptEnd = html.IndexOf("</script>", contentStart, StringComparison.OrdinalIgnoreCase);
        if (scriptEnd < 0)
        {
            return null;
        }

        return html[(contentStart + 1)..scriptEnd];
    }

    private static string? ExtractBracketedArray(string source, string marker)
    {
        var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        var arrayStart = source.IndexOf('[', markerIndex + marker.Length);
        if (arrayStart < 0)
        {
            return null;
        }

        var state = JsLiteralState.Code;
        var depth = 0;
        for (var index = arrayStart; index < source.Length; index++)
        {
            var current = source[index];
            switch (state)
            {
                case JsLiteralState.Code:
                    if (current == '\'' )
                    {
                        state = JsLiteralState.SingleQuotedString;
                        continue;
                    }

                    if (current == '"')
                    {
                        state = JsLiteralState.DoubleQuotedString;
                        continue;
                    }

                    if (current == '/' && index + 1 < source.Length && source[index + 1] == '/')
                    {
                        state = JsLiteralState.LineComment;
                        index++;
                        continue;
                    }

                    if (current == '/' && index + 1 < source.Length && source[index + 1] == '*')
                    {
                        state = JsLiteralState.BlockComment;
                        index++;
                        continue;
                    }

                    if (current == '[')
                    {
                        depth++;
                    }
                    else if (current == ']')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            return source[arrayStart..(index + 1)];
                        }
                    }

                    break;
                case JsLiteralState.SingleQuotedString:
                    if (current == '\\')
                    {
                        index++;
                    }
                    else if (current == '\'')
                    {
                        state = JsLiteralState.Code;
                    }

                    break;
                case JsLiteralState.DoubleQuotedString:
                    if (current == '\\')
                    {
                        index++;
                    }
                    else if (current == '"')
                    {
                        state = JsLiteralState.Code;
                    }

                    break;
                case JsLiteralState.LineComment:
                    if (current is '\r' or '\n')
                    {
                        state = JsLiteralState.Code;
                    }

                    break;
                case JsLiteralState.BlockComment:
                    if (current == '*' && index + 1 < source.Length && source[index + 1] == '/')
                    {
                        state = JsLiteralState.Code;
                        index++;
                    }

                    break;
            }
        }

        return null;
    }

    private static string NormalizeJavaScriptLiteralToJson(string jsLiteral)
    {
        var builder = new StringBuilder(jsLiteral.Length + 128);
        var state = JsLiteralState.Code;
        char lastSignificant = '\0';

        for (var index = 0; index < jsLiteral.Length; index++)
        {
            var current = jsLiteral[index];
            switch (state)
            {
                case JsLiteralState.Code:
                    if (current == '\'' )
                    {
                        builder.Append('"');
                        state = JsLiteralState.SingleQuotedString;
                        continue;
                    }

                    if (current == '"')
                    {
                        builder.Append(current);
                        state = JsLiteralState.DoubleQuotedString;
                        lastSignificant = '"';
                        continue;
                    }

                    if (current == '/' && index + 1 < jsLiteral.Length && jsLiteral[index + 1] == '/')
                    {
                        state = JsLiteralState.LineComment;
                        index++;
                        continue;
                    }

                    if (current == '/' && index + 1 < jsLiteral.Length && jsLiteral[index + 1] == '*')
                    {
                        state = JsLiteralState.BlockComment;
                        index++;
                        continue;
                    }

                    if (current == '.' && index + 1 < jsLiteral.Length && char.IsDigit(jsLiteral[index + 1]) &&
                        (lastSignificant is ':' or ',' or '[' or '-' or '\0'))
                    {
                        builder.Append('0').Append(current);
                        lastSignificant = current;
                        continue;
                    }

                    if (char.IsLetter(current) || current is '_' or '$')
                    {
                        var endIndex = index + 1;
                        while (endIndex < jsLiteral.Length && (char.IsLetterOrDigit(jsLiteral[endIndex]) || jsLiteral[endIndex] is '_' or '$'))
                        {
                            endIndex++;
                        }

                        var token = jsLiteral[index..endIndex];
                        if (token == "new")
                        {
                            var date = System.Text.RegularExpressions.Regex.Match(jsLiteral[index..], @"^new\s+Date\((\d{1,16})\)",
                                System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                            if (!date.Success) throw new InvalidDataException("Unsupported executable hydration expression.");
                            builder.Append(date.Groups[1].Value);
                            index += date.Length - 1;
                            lastSignificant = '0';
                            continue;
                        }
                        var colonIndex = endIndex;
                        while (colonIndex < jsLiteral.Length && char.IsWhiteSpace(jsLiteral[colonIndex]))
                        {
                            colonIndex++;
                        }

                        if (colonIndex < jsLiteral.Length && jsLiteral[colonIndex] == ':' &&
                            (lastSignificant == '{' || lastSignificant == ',' || lastSignificant == '['))
                        {
                            builder.Append('"').Append(token).Append('"');
                        }
                        else if (token is "undefined" or "NaN" or "Infinity")
                        {
                            builder.Append("null");
                            lastSignificant = '0';
                        }
                        else
                        {
                            builder.Append(token);
                            lastSignificant = '0';
                        }

                        index = endIndex - 1;
                        continue;
                    }

                    builder.Append(current);
                    if (!char.IsWhiteSpace(current))
                    {
                        lastSignificant = current;
                    }

                    break;

                case JsLiteralState.SingleQuotedString:
                    if (current == '\\' && index + 1 < jsLiteral.Length)
                    {
                        builder.Append('\\').Append(jsLiteral[index + 1]);
                        index++;
                        continue;
                    }

                    if (current == '\'')
                    {
                        builder.Append('"');
                        state = JsLiteralState.Code;
                        lastSignificant = '"';
                        continue;
                    }

                    if (current == '"')
                    {
                        builder.Append("\\\"");
                        continue;
                    }

                    builder.Append(current);
                    break;

                case JsLiteralState.DoubleQuotedString:
                    builder.Append(current);
                    if (current == '\\' && index + 1 < jsLiteral.Length)
                    {
                        builder.Append(jsLiteral[index + 1]);
                        index++;
                        continue;
                    }

                    if (current == '"')
                    {
                        state = JsLiteralState.Code;
                        lastSignificant = '"';
                    }

                    break;

                case JsLiteralState.LineComment:
                    if (current is '\r' or '\n')
                    {
                        state = JsLiteralState.Code;
                        builder.Append(current);
                    }

                    break;

                case JsLiteralState.BlockComment:
                    if (current == '*' && index + 1 < jsLiteral.Length && jsLiteral[index + 1] == '/')
                    {
                        state = JsLiteralState.Code;
                        index++;
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static bool TryFindObjectWithProperty(JsonElement element, string propertyName, out JsonElement foundObject)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(propertyName, out _))
            {
                foundObject = element;
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (TryFindObjectWithProperty(property.Value, propertyName, out foundObject))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindObjectWithProperty(item, propertyName, out foundObject))
                {
                    return true;
                }
            }
        }

        foundObject = default;
        return false;
    }

    private static D2ptPageDataExtractionResult ExtractFromPageDataObject(JsonElement pageDataObject, int hydrationScriptLength, int dataArrayLength)
    {
        if (!pageDataObject.TryGetProperty("grids", out var gridsObject) ||
            !gridsObject.TryGetProperty("matches_wr", out var matchesWrElement))
        {
            return D2ptPageDataExtractionResult.Failure("MatchesWrMissing", "The decoded page-data payload does not contain grids.matches_wr.");
        }

        var extractedJson = matchesWrElement.GetRawText();
        var extractedGrid = JsonSerializer.Deserialize<DotaHeroGridFile>(extractedJson);
        if (extractedGrid?.Configs is null || extractedGrid.Version < 1)
        {
            return D2ptPageDataExtractionResult.Failure("GridSchemaInvalid", "The extracted grids.matches_wr object could not be deserialized as a Dota hero-grid config.");
        }

        var availableModes = pageDataObject.TryGetProperty("availableModes", out var availableModesElement)
            ? availableModesElement.EnumerateArray()
                .Select(mode => mode.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty)
                .Where(value => value.Length > 0)
                .ToArray()
            : [];

        return new D2ptPageDataExtractionResult(
            Success: true,
            Classification: "ExtractionSuccess",
            Details: "Successfully decoded page data and extracted grids.matches_wr.",
            HydrationScriptLength: hydrationScriptLength,
            DataArrayLength: dataArrayLength,
            AvailableModes: availableModes,
            LastUpdated: pageDataObject.TryGetProperty("lastUpdated", out var lastUpdatedElement) ? lastUpdatedElement.GetString() : null,
            PatchLabel: pageDataObject.TryGetProperty("version", out var versionElement) ? versionElement.GetString() : null,
            ExtractedJson: extractedJson,
            ExtractedGrid: extractedGrid);
    }
}

public sealed record D2ptPageDataExtractionResult(
    bool Success,
    string Classification,
    string Details,
    int HydrationScriptLength,
    int DataArrayLength,
    IReadOnlyList<string> AvailableModes,
    string? LastUpdated,
    string? PatchLabel,
    string? ExtractedJson,
    DotaHeroGridFile? ExtractedGrid)
{
    public static D2ptPageDataExtractionResult Failure(string classification, string details)
        => new(false, classification, details, 0, 0, [], null, null, null, null);
}

internal enum JsLiteralState
{
    Code,
    SingleQuotedString,
    DoubleQuotedString,
    LineComment,
    BlockComment
}
