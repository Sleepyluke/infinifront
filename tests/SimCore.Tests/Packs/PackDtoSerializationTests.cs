using System.Collections.Generic;
using System.Text.Json;
using SimCore.Math;
using SimCore.Packs;
using SimCore.Sim;

namespace SimCore.Tests.Packs;

public class PackDtoSerializationTests
{
    [Fact]
    public void Enums_serialize_by_name()
    {
        var up = new UpgradeDto("u", 1, "lab", null, new[] { "trooper" },
            UpgradeStat.Damage, Fix.FromInt(2), 50, 100);
        string json = JsonSerializer.Serialize(up, PackJson.Options);
        Assert.Contains("\"Damage\"", json);
        Assert.DoesNotContain("\"stat\": 0", json);
    }

    [Fact]
    public void Mechanic_kind_serializes_by_name()
    {
        var m = new MechanicDto(MechanicKind.RegeneratingShields, 15, 1, 10);
        string json = JsonSerializer.Serialize(m, PackJson.Options);
        Assert.Contains("RegeneratingShields", json);
    }

    [Fact]
    public void Null_nested_dtos_are_omitted()
    {
        var u = new UnitDto("fabber", 1, "depot", null,
            40, Fix.FromFraction(1, 4), 50, 1, 100, 6, Weapon: null, Harvester: null);
        string json = JsonSerializer.Serialize(u, PackJson.Options);
        Assert.DoesNotContain("weapon", json);
        Assert.DoesNotContain("harvester", json);
    }

    [Fact]
    public void Building_false_and_zero_defaults_are_omitted_on_write()
    {
        var b = new BuildingDto("barracks", 1, null,
            350, 2, 2, 150, 200, SupplyProvided: 0, IsDepot: false, CanTrain: true, SightRange: 8);
        string json = JsonSerializer.Serialize(b, PackJson.Options);
        Assert.DoesNotContain("supplyProvided", json); // 0 omitted
        Assert.DoesNotContain("isDepot", json);        // false omitted
        Assert.Contains("canTrain", json);             // true written
    }

    [Fact]
    public void Building_defaults_are_restored_when_json_omits_them()
    {
        // JSON omits supplyProvided, isDepot, canTrain, sightRange entirely.
        string json = """
        { "id": "barracks", "tier": 1, "maxHp": 350, "width": 2, "height": 2,
          "mineralCost": 150, "buildTimeTicks": 200 }
        """;
        var b = JsonSerializer.Deserialize<BuildingDto>(json, PackJson.Options)!;
        Assert.Equal(0, b.SupplyProvided);
        Assert.False(b.IsDepot);
        Assert.False(b.CanTrain);
        Assert.Equal(8, b.SightRange); // ctor default honored
        Assert.Null(b.Requires);
    }

    [Fact]
    public void Property_names_are_camelCase()
    {
        var u = new UnitDto("fabber", 1, "depot", null,
            40, Fix.FromFraction(1, 4), 50, 1, 100, 6);
        string json = JsonSerializer.Serialize(u, PackJson.Options);
        Assert.Contains("\"maxHp\"", json);
        Assert.Contains("\"producedBy\"", json);
        Assert.DoesNotContain("\"MaxHp\"", json);
    }
}
