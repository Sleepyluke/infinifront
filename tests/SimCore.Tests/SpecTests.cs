using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class SpecTests
{
    private static SimWorld NewWorld() => new(new MapGrid(16, 16), seed: 1);

    [Fact]
    public void Spec_Spawn_Sets_All_Fields_And_Instantiates_Weapon()
    {
        var w = NewWorld();
        var spec = new UnitSpec(MaxHp: 40, Speed: Fix.FromFraction(1, 2), MineralCost: 50,
            SupplyCost: 2, BuildTimeTicks: 20, Weapon: new WeaponSpec(7, Fix.FromInt(3), 6));
        var id = w.SpawnUnit(0, FixVec.FromInts(2, 2), spec);
        var u = w.GetUnit(id)!;
        Assert.Equal(40, u.Hp);
        Assert.Equal(Fix.FromFraction(1, 2), u.SpeedPerTick);
        Assert.Equal(2, u.SupplyCost);
        Assert.Equal(7, u.Weapon!.Damage);
        Assert.Equal(Fix.FromInt(3), u.Weapon.Range);
        Assert.Equal(6, u.Weapon.CooldownTicks);
        Assert.Equal(0, u.Weapon.CooldownRemaining);
    }

    [Fact]
    public void Same_Spec_Produces_Distinct_Weapon_Instances()
    {
        var w = NewWorld();
        var spec = new UnitSpec(40, Fix.FromFraction(1, 2), 50, 2, 20, new WeaponSpec(7, Fix.FromInt(3), 6));
        var a = w.SpawnUnit(0, FixVec.FromInts(2, 2), spec);
        var b = w.SpawnUnit(0, FixVec.FromInts(3, 3), spec);
        Assert.NotSame(w.GetUnit(a)!.Weapon, w.GetUnit(b)!.Weapon);
    }

    [Fact]
    public void Legacy_Spawn_Clones_Weapon_Instance()
    {
        var w = NewWorld();
        var shared = new Weapon { Damage = 5, Range = Fix.FromInt(2), CooldownTicks = 4 };
        var a = w.SpawnUnit(0, FixVec.FromInts(2, 2), Fix.FromInt(1), 30, shared);
        var b = w.SpawnUnit(0, FixVec.FromInts(3, 3), Fix.FromInt(1), 30, shared);
        Assert.NotSame(w.GetUnit(a)!.Weapon, w.GetUnit(b)!.Weapon);
        Assert.NotSame(shared, w.GetUnit(a)!.Weapon);
        w.GetUnit(a)!.Weapon!.CooldownRemaining = 3;
        Assert.Equal(0, w.GetUnit(b)!.Weapon!.CooldownRemaining); // no shared cooldown state
    }
}
