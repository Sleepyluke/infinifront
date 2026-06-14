using System.Collections.Generic;
using SimCore.Math;
using SimCore.Packs;
using SimCore.Sim;

namespace SimCore.Tests.Packs;

public class PackMapperTests
{
    // A synthetic faction exercising every mappable field: weapon, harvester,
    // upgrades (Fix delta, enum, targets), and a mechanic — none of which the
    // bare ReferenceFaction has.
    private static FactionDef Rich()
    {
        var none = System.Array.Empty<string>();
        return new FactionDef(
            id: "rich", name: "Rich",
            units: new[]
            {
                new UnitDef("fab", 1, "depot", none,
                    new UnitSpec(40, Fix.FromFraction(1, 4), 50, 1, 100,
                        Harvester: new HarvesterSpec(5, 10), SightRange: 6)),
                new UnitDef("troop", 1, "rax", new[] { "depot" },
                    new UnitSpec(45, Fix.FromFraction(1, 5), 50, 1, 80,
                        Weapon: new WeaponSpec(6, Fix.FromInt(4), 8), SightRange: 7)),
            },
            buildings: new[]
            {
                new BuildingDef("depot", 1, none, new BuildingSpec(400, 2, 2, 100, 150, SupplyProvided: 8, IsDepot: true, SightRange: 9)),
                new BuildingDef("rax", 1, none, new BuildingSpec(350, 2, 2, 150, 200, CanTrain: true)),
            },
            upgrades: new[]
            {
                new UpgradeDef("dmg1", 1, "rax", none, new[] { "troop" },
                    UpgradeStat.Damage, Fix.FromInt(2), 75, 120),
                new UpgradeDef("spd1", 2, "rax", new[] { "dmg1" }, new[] { "*" },
                    UpgradeStat.Speed, Fix.FromFraction(1, 10), 100, 150),
            },
            mechanic: new MechanicDef(MechanicKind.RegeneratingShields, 15, 1, 10));
    }

    [Fact]
    public void Roundtrips_through_dto_deep_equal()
    {
        var def = Rich();
        var dto = PackMapper.ToDto(def);
        var back = PackMapper.ToFactionDef(dto);
        FactionDefAssert.DeepEqual(def, back);
    }

    [Fact]
    public void Maps_weapon_and_harvester()
    {
        var dto = PackMapper.ToDto(Rich());
        var fab = System.Linq.Enumerable.First(dto.Units!, u => u.Id == "fab");
        var troop = System.Linq.Enumerable.First(dto.Units!, u => u.Id == "troop");
        Assert.NotNull(fab.Harvester);
        Assert.Null(fab.Weapon);
        Assert.NotNull(troop.Weapon);
        Assert.Equal(6, troop.Weapon!.Damage);
        Assert.Equal(Fix.FromInt(4), troop.Weapon.Range);
    }

    [Fact]
    public void Maps_upgrade_enum_and_fix_delta()
    {
        var dto = PackMapper.ToDto(Rich());
        var spd = System.Linq.Enumerable.First(dto.Upgrades!, u => u.Id == "spd1");
        Assert.Equal(UpgradeStat.Speed, spd.Stat);
        Assert.Equal(Fix.FromFraction(1, 10), spd.Delta);
        Assert.Equal(new[] { "*" }, spd.TargetUnitDefIds);
    }

    [Fact]
    public void Maps_mechanic()
    {
        var dto = PackMapper.ToDto(Rich());
        Assert.NotNull(dto.Mechanic);
        Assert.Equal(MechanicKind.RegeneratingShields, dto.Mechanic!.Kind);
        Assert.Equal(15, dto.Mechanic.MaxShield);
    }

    [Fact]
    public void Null_mechanic_maps_to_null()
    {
        var def = new FactionDef("x", "X",
            new[] { new UnitDef("u", 1, "b", System.Array.Empty<string>(), new UnitSpec(10, Fix.One, 1, 1, 1)) },
            new[] { new BuildingDef("b", 1, System.Array.Empty<string>(), new BuildingSpec(10, 1, 1, 1, 1)) });
        var dto = PackMapper.ToDto(def);
        Assert.Null(dto.Mechanic);
        var back = PackMapper.ToFactionDef(dto);
        Assert.Null(back.Mechanic);
    }
}
