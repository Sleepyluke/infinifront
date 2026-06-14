using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimCore.Packs;

/// <summary>Shared serializer options for faction packs: camelCase, indented, enums by name,
/// the decimal-exact Fix converter, and omit-defaults on write (so booleans/zeros that match
/// engine defaults stay out of the JSON and round-trip via the DTO ctor defaults).</summary>
public static class PackJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        };
        o.Converters.Add(new FixJsonConverter());
        // Names only: reject numeric enum values (e.g. "stat": 99) so a degenerate, undefined
        // enum can never reach the engine. Unknown names are likewise rejected (JsonException).
        o.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        return o;
    }
}
