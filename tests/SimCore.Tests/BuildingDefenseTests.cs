using SimCore.Math;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

// Building-as-attacker (defense tower) combat. The existing BuildingCombatTests covers the
// inverse (units attacking buildings); this file covers weaponed buildings firing at enemies.
public class BuildingDefenseTests
{
    private static BuildingSpec Turret() => new(
        MaxHp: 250, Width: 2, Height: 2, MineralCost: 150, BuildTimeTicks: 1, SightRange: 9,
        Weapon: new WeaponSpec(Damage: 12, Range: Fix.FromInt(6), CooldownTicks: 8));

    private static SimWorld World()
    {
        var w = new SimWorld(new MapGrid(24, 24), seed: 1, playerCount: 4, faction: null);
        w.FogEnabled = false;   // isolate combat from vision
        return w;
    }

    [Fact]
    public void Tower_Damages_An_Enemy_In_Range()
    {
        var w = World();
        w.AddCompletedBuilding(0, Turret(), 4, 4, "tower");
        int e = w.SpawnUnit(1, w.Map.CellCenter(6, 6), Fix.FromInt(1), hp: 50);  // ~2-3 cells away, player 1
        for (int i = 0; i < 20; i++) w.Step(System.Array.Empty<Command>());
        Assert.True(w.GetUnit(e)!.Hp < 50, "an enemy in range should be shot by the tower");
    }

    [Fact]
    public void Tower_Ignores_Allies()
    {
        var w = World();
        w.SetTeam(0, 7); w.SetTeam(1, 7);                 // tower owner 0 and unit owner 1 same team
        w.AddCompletedBuilding(0, Turret(), 4, 4, "tower");
        int ally = w.SpawnUnit(1, w.Map.CellCenter(6, 6), Fix.FromInt(1), hp: 50);
        for (int i = 0; i < 20; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(50, w.GetUnit(ally)!.Hp);
    }

    [Fact]
    public void Tower_Ignores_Enemy_Out_Of_Range()
    {
        var w = World();
        w.AddCompletedBuilding(0, Turret(), 2, 2, "tower");          // range 6
        int far = w.SpawnUnit(1, w.Map.CellCenter(20, 20), Fix.FromInt(1), hp: 50);  // far away
        for (int i = 0; i < 20; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(50, w.GetUnit(far)!.Hp);
    }

    [Fact]
    public void Incomplete_Tower_Does_Not_Fire()
    {
        var w = World();
        // place via the construction path so it starts incomplete (BuildTimeTicks high)
        var spec = new BuildingSpec(250, 2, 2, 150, BuildTimeTicks: 10_000, SightRange: 9,
            Weapon: new WeaponSpec(12, Fix.FromInt(6), 8));
        w.PlaceBuilding(0, spec, 4, 4, "tower");   // internal + InternalsVisibleTo — places INCOMPLETE
        int e = w.SpawnUnit(1, w.Map.CellCenter(6, 6), Fix.FromInt(1), hp: 50);
        for (int i = 0; i < 20; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(50, w.GetUnit(e)!.Hp);
    }
}
