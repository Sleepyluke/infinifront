using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class ShieldSpawnTests
{
    private static FactionDef ShieldFaction(int max) => new("f", "F",
        units: System.Array.Empty<UnitDef>(),
        buildings: System.Array.Empty<BuildingDef>(),
        upgrades: System.Array.Empty<UpgradeDef>(),
        mechanic: new MechanicDef(MechanicKind.RegeneratingShields, max, 1, 10));

    [Fact]
    public void Shield_Faction_Units_Spawn_With_Full_Shield()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, faction: ShieldFaction(25));
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 2), Fix.FromInt(1), 50);
        Assert.Equal(25, w.GetUnit(id)!.ShieldHp);
    }

    [Fact]
    public void Non_Shield_Faction_Units_Spawn_With_Zero_Shield()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, faction: TestFactions.Standard);
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 2), Fix.FromInt(1), 50);
        Assert.Equal(0, w.GetUnit(id)!.ShieldHp);
    }

    [Fact]
    public void No_Faction_Units_Spawn_With_Zero_Shield()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 2), Fix.FromInt(1), 50);
        Assert.Equal(0, w.GetUnit(id)!.ShieldHp);
        Assert.Equal(0, w.GetUnit(id)!.TicksSinceDamaged);
    }
}
