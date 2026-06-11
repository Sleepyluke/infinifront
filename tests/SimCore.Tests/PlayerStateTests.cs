using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class PlayerStateTests
{
    [Fact]
    public void World_Has_Player_States()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, playerCount: 3);
        Assert.Equal(3, w.Players.Count);
        Assert.Equal(0, w.Players[0].Minerals);
    }

    [Fact]
    public void Spawn_And_Death_Track_Supply()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1);
        var spec = new UnitSpec(MaxHp: 10, Speed: Fix.FromInt(1), MineralCost: 0, SupplyCost: 2, BuildTimeTicks: 1);
        var id = w.SpawnUnit(0, FixVec.FromInts(2, 2), spec);
        Assert.Equal(2, w.Players[0].SupplyUsed);

        w.GetUnit(id)!.Hp = 0;
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.Players[0].SupplyUsed);
    }

    [Fact]
    public void Legacy_Spawn_Costs_No_Supply()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1);
        w.SpawnUnit(0, FixVec.FromInts(2, 2), Fix.FromInt(1), 10);
        Assert.Equal(0, w.Players[0].SupplyUsed);
    }
}
