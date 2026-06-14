using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimCore.Math;

namespace SimCore.Packs;

/// <summary>Serializes <see cref="Fix"/> as a base-10 decimal. Every Q48.16 value is
/// exactly representable in decimal (1/2^16 terminates in base 10), so this round-trips
/// bit-for-bit using only <see cref="decimal"/> — no <c>double</c> anywhere.
/// NEVER serialize via Fix.ToString (it formats a lossy double).</summary>
public sealed class FixJsonConverter : JsonConverter<Fix>
{
    private const decimal Scale = 65536m; // 2^16 == 1 << Fix.FractionalBits

    public override Fix Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        decimal value;
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (!reader.TryGetDecimal(out value))
                throw new JsonException("Fix value is out of decimal range");
        }
        else if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
                throw new JsonException($"Fix value '{s}' is not a valid decimal");
        }
        else
        {
            throw new JsonException($"Fix expects a number or string, got {reader.TokenType}");
        }

        decimal scaled = value * Scale;
        if (scaled < long.MinValue || scaled > long.MaxValue)
            throw new JsonException($"Fix value {value} is out of Q48.16 range");
        long raw = (long)System.Math.Round(scaled, MidpointRounding.ToEven);
        return new Fix(raw);
    }

    public override void Write(Utf8JsonWriter writer, Fix value, JsonSerializerOptions options)
    {
        decimal d = (decimal)value.Raw / Scale;
        writer.WriteNumberValue(d);
    }
}
