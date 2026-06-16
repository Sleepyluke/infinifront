using SimCore.Math;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class BuildingWeaponTests
{
    private static BuildingSpec Armed() => new(
        MaxHp: 250, Width: 2, Height: 2, MineralCost: 150, BuildTimeTicks: 1, SightRange: 9,
        Weapon: new WeaponSpec(Damage: 12, Range: Fix.FromInt(6), CooldownTicks: 8));

    [Fact]
    public void Weaponless_Building_Has_Null_Weapon()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, playerCount: 2, faction: null);
        int id = w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 2, 2, "depot");
        Assert.Null(w.GetBuilding(id)!.Weapon);
    }

    [Fact]
    public void Armed_Building_Gets_A_Cloned_Weapon()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, playerCount: 2, faction: null);
        int a = w.AddCompletedBuilding(0, Armed(), 2, 2, "tower");
        int b = w.AddCompletedBuilding(0, Armed(), 6, 6, "tower");
        var wa = w.GetBuilding(a)!.Weapon;
        var wb = w.GetBuilding(b)!.Weapon;
        Assert.NotNull(wa);
        Assert.NotNull(wb);
        Assert.NotSame(wa, wb);              // no aliasing — cooldown state is per-building
        Assert.Equal(12, wa!.Damage);
    }
}
