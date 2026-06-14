using System.Text.Json;
using SimCore.Math;
using SimCore.Packs;

namespace SimCore.Tests.Packs;

public class FixJsonConverterTests
{
    private static JsonSerializerOptions Opts()
    {
        var o = new JsonSerializerOptions();
        o.Converters.Add(new FixJsonConverter());
        return o;
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(65536L)]      // 1
    [InlineData(32768L)]      // 0.5
    [InlineData(16384L)]      // 0.25
    [InlineData(13107L)]      // FromFraction(1,5) floor
    [InlineData(8192L)]       // 0.125
    [InlineData(-32768L)]     // -0.5
    [InlineData(1L)]          // smallest unit
    [InlineData(1L << 40)]    // large
    public void Roundtrips_raw_values_exactly(long raw)
    {
        var f = new Fix(raw);
        string json = JsonSerializer.Serialize(f, Opts());
        Fix back = JsonSerializer.Deserialize<Fix>(json, Opts());
        Assert.Equal(f, back);
    }

    [Fact]
    public void Quarter_serializes_as_clean_decimal()
    {
        string json = JsonSerializer.Serialize(Fix.FromFraction(1, 4), Opts());
        Assert.Equal("0.25", json);
    }

    [Fact]
    public void Whole_number_serializes_without_fraction()
    {
        string json = JsonSerializer.Serialize(Fix.FromInt(4), Opts());
        Assert.Equal("4", json);
    }

    [Fact]
    public void Parses_human_authored_decimal_number()
    {
        Fix f = JsonSerializer.Deserialize<Fix>("0.25", Opts());
        Assert.Equal(Fix.FromFraction(1, 4), f);
    }

    [Fact]
    public void Parses_one_fifth_to_same_raw_as_FromFraction()
    {
        Fix f = JsonSerializer.Deserialize<Fix>("0.2", Opts());
        Assert.Equal(Fix.FromFraction(1, 5), f); // both raw 13107
    }

    [Fact]
    public void Parses_decimal_supplied_as_json_string()
    {
        Fix f = JsonSerializer.Deserialize<Fix>("\"0.5\"", Opts());
        Assert.Equal(Fix.FromFraction(1, 2), f);
    }

    [Fact]
    public void Out_of_range_value_throws_JsonException()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Fix>("1e30", Opts()));
    }

    [Fact]
    public void Overflow_band_value_throws_JsonException()
    {
        // Passes TryGetDecimal but value*65536 overflows decimal -> must surface as JsonException, not OverflowException.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Fix>("5e24", Opts()));
    }
}
