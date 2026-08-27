using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetaGrid.Infrastructure.Utilities;

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Storage = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static readonly JsonSerializerOptions StrictIndented = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
