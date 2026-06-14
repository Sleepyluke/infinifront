using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class EffectiveStatTests
{
    private static readonly string[] None = System.Array.Empty<string>();

    private static FactionDef Faction(params UpgradeDef[] ups) => new("f", "F",
        units: new[] { new UnitDef("marine", 1, "barracks", None,
            new UnitSpec(40, Fix.FromFraction(1, 2), 50, 1, 5, Weapon: new WeaponSpec(6, Fix.FromInt(2), 5), SightRange: 7)) },
        buildings: new[] { new BuildingDef("barracks", 1, None, new BuildingSpec(150, 2, 2, 150, 3, CanTrain: true)) },
        upgrades: ups);

    private static int SpawnMarine(SimWorld w, int x, int y)
    {
        var spec = new UnitSpec(40, Fix.FromFraction(1, 2), 50, 1, 5, Weapon: new WeaponSpec(6, Fix.FromInt(2), 5), SightRange: 7);
        return w.SpawnUnit(0, w.Map.CellCenter(x, y), spec, "marine");
    }

    [Fact]
    public void No_Upgrades_Effective_Equals_Base()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: Faction());
        var u = w.GetUnit(SpawnMarine(w, 2, 2))!;
        Assert.Equal(6, w.EffectiveDamage(u));
        Assert.Equal(Fix.FromInt(2), w.EffectiveRange(u));
        Assert.Equal(5, w.EffectiveCooldownTicks(u));
        Assert.Equal(Fix.FromFraction(1, 2), w.EffectiveSpeed(u));
        Assert.Equal(7, w.EffectiveSight(u));
    }

    [Fact]
    public void Applied_Damage_Upgrade_Raises_Effective_Damage_For_Targeted_Unit()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1,
            faction: Faction(new UpgradeDef("dmg1", 1, "barracks", None, new[] { "marine" }, UpgradeStat.Damage, Fix.FromInt(3), 50, 1)));
        var u = w.GetUnit(SpawnMarine(w, 2, 2))!;
        Assert.Equal(6, w.EffectiveDamage(u));
        w.Players[0].AddUpgrade("dmg1");
        Assert.Equal(9, w.EffectiveDamage(u));
    }

    [Fact]
    public void Star_Target_Affects_DefIdless_Units_And_Stacks()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1,
            faction: Faction(
                new UpgradeDef("dmg1", 1, "barracks", None, new[] { "*" }, UpgradeStat.Damage, Fix.FromInt(2), 50, 1),
                new UpgradeDef("dmg2", 1, "barracks", None, new[] { "*" }, UpgradeStat.Damage, Fix.FromInt(2), 50, 1)));
        var weapon = new Weapon { Damage = 6, Range = Fix.FromInt(2), CooldownTicks = 5 };
        var u = w.GetUnit(w.SpawnUnit(0, w.Map.CellCenter(2, 2), Fix.FromFraction(1, 2), 40, weapon))!;
        w.Players[0].AddUpgrade("dmg1");
        w.Players[0].AddUpgrade("dmg2");
        Assert.Equal(10, w.EffectiveDamage(u));
    }

    [Fact]
    public void Targeting_Excludes_Nonmatching_Unit_Def()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1,
            faction: Faction(new UpgradeDef("dmg1", 1, "barracks", None, new[] { "tank" }, UpgradeStat.Damage, Fix.FromInt(3), 50, 1)));
        var u = w.GetUnit(SpawnMarine(w, 2, 2))!;
        w.Players[0].AddUpgrade("dmg1");
        Assert.Equal(6, w.EffectiveDamage(u));
    }

    [Fact]
    public void Negative_Delta_Clamps_At_Floor()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1,
            faction: Faction(new UpgradeDef("slow", 1, "barracks", None, new[] { "*" }, UpgradeStat.CooldownTicks, Fix.FromInt(-100), 0, 1)));
        var u = w.GetUnit(SpawnMarine(w, 2, 2))!;
        w.Players[0].AddUpgrade("slow");
        Assert.Equal(1, w.EffectiveCooldownTicks(u));
    }

    [Fact]
    public void Damage_Upgrade_Makes_Combat_Kill_Faster()
    {
        FactionDef F(bool withUp) => new("f", "F",
            units: System.Array.Empty<UnitDef>(),
            buildings: System.Array.Empty<BuildingDef>(),
            upgrades: withUp
                ? new[] { new UpgradeDef("dmg", 1, "x", None, new[] { "*" }, UpgradeStat.Damage, Fix.FromInt(50), 0, 1) }
                : System.Array.Empty<UpgradeDef>());

        int TicksToKill(bool withUp)
        {
            var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: F(withUp));
            var weapon = new Weapon { Damage = 5, Range = Fix.FromInt(3), CooldownTicks = 2 };
            var atk = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, weapon);
            var vic = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 100);
            if (withUp) w.Players[0].AddUpgrade("dmg");
            w.Step(new Command[] { new AttackCommand(0, new[] { atk }, vic) });
            int t = 1;
            while (w.GetUnit(vic) is not null && t < 500) { w.Step(System.Array.Empty<Command>()); t++; }
            return t;
        }

        Assert.True(TicksToKill(true) < TicksToKill(false), "damage upgrade should kill faster");
    }
}
