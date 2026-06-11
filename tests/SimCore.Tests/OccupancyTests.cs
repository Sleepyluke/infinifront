using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class OccupancyTests
{
    [Fact]
    public void Spawned_Unit_Claims_Its_Cell()
    {
        var w = new SimWorld(new MapGrid(10, 10), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(3, 3), Fix.FromFraction(1, 2), 50);
        Assert.Equal(id, w.OccupantAt(3, 3));
        Assert.Equal(0, w.OccupantAt(4, 4)); // empty
    }

    [Fact]
    public void Dead_Unit_Releases_Its_Cell()
    {
        var w = new SimWorld(new MapGrid(10, 10), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(3, 3), Fix.FromFraction(1, 2), 50);
        w.GetUnit(id)!.Hp = 0;
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.OccupantAt(3, 3));
    }

    [Fact]
    public void Spawn_On_Occupied_Cell_Is_Rejected()
    {
        var w = new SimWorld(new MapGrid(10, 10), seed: 1);
        w.SpawnUnit(0, w.Map.CellCenter(3, 3), Fix.FromFraction(1, 2), 50);
        var second = w.SpawnUnit(0, w.Map.CellCenter(3, 3), Fix.FromFraction(1, 2), 50);
        Assert.Equal(0, second); // 0 = rejected (no unit id is ever 0)
        Assert.Single(w.Units);
    }
}
