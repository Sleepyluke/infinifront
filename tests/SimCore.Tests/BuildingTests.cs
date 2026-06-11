using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class BuildingTests
{
    public static readonly BuildingSpec Depot =
        new(MaxHp: 100, Width: 2, Height: 2, MineralCost: 100, BuildTimeTicks: 10, SupplyProvided: 8, IsDepot: true);

    private static (SimWorld w, int worker) WorldWithWorker()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        w.Players[0].Minerals = 500;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        return (w, worker);
    }

    [Fact]
    public void Build_Deducts_Minerals_And_Blocks_Footprint()
    {
        var (w, worker) = WorldWithWorker();
        w.Step(new Command[] { new BuildCommand(0, worker, Depot, 6, 5) });
        Assert.Equal(400, w.Players[0].Minerals);
        Assert.Single(w.Buildings);
        Assert.False(w.Map.IsPassable(6, 5));
        Assert.False(w.Map.IsPassable(7, 6));
        Assert.True(w.Map.IsPassable(8, 5)); // outside footprint
    }

    [Fact]
    public void Build_Rejected_When_Too_Poor()
    {
        var (w, worker) = WorldWithWorker();
        w.Players[0].Minerals = 50;
        w.Step(new Command[] { new BuildCommand(0, worker, Depot, 6, 5) });
        Assert.Empty(w.Buildings);
        Assert.Equal(50, w.Players[0].Minerals);
    }

    [Fact]
    public void Build_Rejected_When_Worker_Too_Far()
    {
        var (w, worker) = WorldWithWorker();
        w.Step(new Command[] { new BuildCommand(0, worker, Depot, 15, 15) });
        Assert.Empty(w.Buildings);
        Assert.Equal(500, w.Players[0].Minerals);
    }

    [Fact]
    public void Build_Rejected_On_Blocked_Or_Occupied_Footprint()
    {
        var (w, worker) = WorldWithWorker();
        w.Map.SetPassable(7, 5, false); // pre-blocked cell inside footprint
        w.Step(new Command[] { new BuildCommand(0, worker, Depot, 6, 5) });
        Assert.Empty(w.Buildings);

        // unit standing in footprint also blocks (worker itself at (5,5) is outside 6..7 x 5..6)
        w.Map.SetPassable(7, 5, true);
        w.SpawnUnit(1, w.Map.CellCenter(6, 6), Fix.FromInt(1), 10);
        w.Step(new Command[] { new BuildCommand(0, worker, Depot, 6, 5) });
        Assert.Empty(w.Buildings);
    }

    [Fact]
    public void Destroyed_Building_Restores_Passability()
    {
        var (w, worker) = WorldWithWorker();
        w.Step(new Command[] { new BuildCommand(0, worker, Depot, 6, 5) });
        var b = w.Buildings[0];
        b.Hp = 0;
        w.Step(System.Array.Empty<Command>());
        Assert.Empty(w.Buildings);
        Assert.Null(w.GetBuilding(b.Id));
        Assert.True(w.Map.IsPassable(6, 5));
        Assert.True(w.Map.IsPassable(7, 6));
    }
}
