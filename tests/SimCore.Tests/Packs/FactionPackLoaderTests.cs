using SimCore.Packs;
using SimCore.Sim;

namespace SimCore.Tests.Packs;

public class FactionPackLoaderTests
{
    [Fact]
    public void Roundtrips_reference_faction_through_json()
    {
        string json = FactionPackLoader.ToJson(ReferenceFaction.Def);
        var result = FactionPackLoader.LoadFromJson(json);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Faction);
        FactionDefAssert.DeepEqual(ReferenceFaction.Def, result.Faction!);
    }

    [Fact]
    public void Malformed_json_returns_error_not_throw()
    {
        var result = FactionPackLoader.LoadFromJson("{ not valid json ");
        Assert.Null(result.Faction);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Unknown_enum_name_returns_error()
    {
        // valid JSON but Stat is an unknown enum name
        string json = """
        { "id": "x", "name": "X",
          "units": [ { "id": "u", "tier": 1, "producedBy": "b",
                       "maxHp": 10, "speed": 1, "mineralCost": 1, "supplyCost": 1, "buildTimeTicks": 1 } ],
          "buildings": [ { "id": "b", "tier": 1, "maxHp": 10, "width": 1, "height": 1, "mineralCost": 1, "buildTimeTicks": 1 } ],
          "upgrades": [ { "id": "g", "tier": 1, "researchedAt": "b",
                          "targetUnitDefIds": ["u"], "stat": "Telekinesis", "delta": 1, "mineralCost": 1, "researchTicks": 1 } ] }
        """;
        var result = FactionPackLoader.LoadFromJson(json);
        Assert.Null(result.Faction);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Numeric_enum_value_returns_error()
    {
        // Valid JSON but an out-of-range numeric enum value -> must be rejected, not silently cast.
        string json = """
        { "id": "x", "name": "X",
          "units": [],
          "buildings": [ { "id": "b", "tier": 1, "maxHp": 10, "width": 1, "height": 1, "mineralCost": 1, "buildTimeTicks": 1 } ],
          "upgrades": [ { "id": "g", "tier": 1, "researchedAt": "b", "targetUnitDefIds": ["*"],
                          "stat": 99, "delta": 1, "mineralCost": 1, "researchTicks": 1 } ] }
        """;
        var result = FactionPackLoader.LoadFromJson(json);
        Assert.Null(result.Faction);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Referentially_broken_pack_returns_def_plus_validate_errors()
    {
        // Parses fine, but the unit's producer building does not exist -> Validate() catches it.
        string json = """
        { "id": "x", "name": "X",
          "units": [ { "id": "u", "tier": 1, "producedBy": "ghost",
                       "maxHp": 10, "speed": 1, "mineralCost": 1, "supplyCost": 1, "buildTimeTicks": 1 } ],
          "buildings": [] }
        """;
        var result = FactionPackLoader.LoadFromJson(json);
        Assert.NotNull(result.Faction);           // structurally parseable -> def returned
        Assert.NotEmpty(result.Errors);           // Validate() flagged the dangling producer
        Assert.Contains(result.Errors, e => e.Contains("ghost"));
    }

    [Fact]
    public void Out_of_range_fix_returns_error()
    {
        string json = """
        { "id": "x", "name": "X",
          "units": [ { "id": "u", "tier": 1, "producedBy": "b",
                       "maxHp": 10, "speed": 1e30, "mineralCost": 1, "supplyCost": 1, "buildTimeTicks": 1 } ],
          "buildings": [ { "id": "b", "tier": 1, "maxHp": 10, "width": 1, "height": 1, "mineralCost": 1, "buildTimeTicks": 1 } ] }
        """;
        var result = FactionPackLoader.LoadFromJson(json);
        Assert.Null(result.Faction);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void ToJson_is_indented_and_human_readable()
    {
        string json = FactionPackLoader.ToJson(ReferenceFaction.Def);
        Assert.Contains("\n", json);              // indented (multi-line)
        Assert.Contains("\"reference\"", json);
    }

    [Fact]
    public void Null_input_returns_error_not_throw()
    {
        var result = FactionPackLoader.LoadFromJson(null!);
        Assert.Null(result.Faction);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Whitespace_input_returns_error_not_throw()
    {
        var result = FactionPackLoader.LoadFromJson("   ");
        Assert.Null(result.Faction);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Null_element_in_requires_returns_error_not_throw()
    {
        // Valid JSON, but a null inside an inner string list -> Validate()'s dict lookup must not escape.
        string json = """
        { "id": "x", "name": "X",
          "units": [ { "id": "u", "tier": 1, "producedBy": "b", "requires": [null],
                       "maxHp": 10, "speed": 1, "mineralCost": 1, "supplyCost": 1, "buildTimeTicks": 1 } ],
          "buildings": [ { "id": "b", "tier": 1, "maxHp": 10, "width": 1, "height": 1, "mineralCost": 1, "buildTimeTicks": 1 } ] }
        """;
        var result = FactionPackLoader.LoadFromJson(json);
        Assert.Null(result.Faction);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Null_researched_at_returns_error_not_throw()
    {
        string json = """
        { "id": "x", "name": "X",
          "units": [],
          "buildings": [ { "id": "b", "tier": 1, "maxHp": 10, "width": 1, "height": 1, "mineralCost": 1, "buildTimeTicks": 1 } ],
          "upgrades": [ { "id": "g", "tier": 1, "researchedAt": null, "targetUnitDefIds": ["*"],
                          "stat": "Damage", "delta": 1, "mineralCost": 1, "researchTicks": 1 } ] }
        """;
        var result = FactionPackLoader.LoadFromJson(json);
        Assert.Null(result.Faction);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void LoadAndValidate_reference_pack_is_clean()
    {
        string json = FactionPackLoader.ToJson(ReferenceFaction.Def);
        var report = FactionPackLoader.LoadAndValidate(json);
        Assert.NotNull(report.Faction);
        Assert.Empty(report.LoadErrors);
        Assert.DoesNotContain(report.Findings, x => x.Severity == Severity.Error);
    }

    [Fact]
    public void LoadAndValidate_hard_failure_has_no_findings()
    {
        var report = FactionPackLoader.LoadAndValidate("{ not valid json ");
        Assert.Null(report.Faction);
        Assert.NotEmpty(report.LoadErrors);
        Assert.Empty(report.Findings);
    }
}
