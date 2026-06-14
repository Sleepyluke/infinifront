using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class BuildingTests
{
    // Kept as a public constant so ConstructionTests/HarvestTests can read spec fields.
    public static readonly BuildingSpec Depot = TestFactions.DepotSpec;

    private static (SimWorld w, int worker) WorldWithWorker()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: TestFactions.Standard);
        w.Players[0].Minerals = 500;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        return (w, worker);
    }

    [Fact]
    public void Build_Deducts_Minerals_And_Blocks_Footprint()
    {
        var (w, worker) = WorldWithWorker();
        w.Step(new Command[] { new BuildCommand(0, worker, "depot", 6, 5) });
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
        w.Step(new Command[] { new BuildCommand(0, worker, "depot", 6, 5) });
        Assert.Empty(w.Buildings);
        Assert.Equal(50, w.Players[0].Minerals);
    }

    [Fact]
    public void Build_Rejected_When_Worker_Too_Far()
    {
        var (w, worker) = WorldWithWorker();
        w.Step(new Command[] { new BuildCommand(0, worker, "depot", 15, 15) });
        Assert.Empty(w.Buildings);
        Assert.Equal(500, w.Players[0].Minerals);
    }

    [Fact]
    public void Build_Rejected_On_Blocked_Or_Occupied_Footprint()
    {
        var (w, worker) = WorldWithWorker();
        w.Map.SetPassable(7, 5, false); // pre-blocked cell inside footprint
        w.Step(new Command[] { new BuildCommand(0, worker, "depot", 6, 5) });
        Assert.Empty(w.Buildings);

        // unit standing in footprint also blocks (worker itself at (5,5) is outside 6..7 x 5..6)
        w.Map.SetPassable(7, 5, true);
        w.SpawnUnit(1, w.Map.CellCenter(6, 6), Fix.FromInt(1), 10);
        w.Step(new Command[] { new BuildCommand(0, worker, "depot", 6, 5) });
        Assert.Empty(w.Buildings);
    }

    [Fact]
    public void Destroyed_Building_Restores_Passability()
    {
        var (w, worker) = WorldWithWorker();
        w.Step(new Command[] { new BuildCommand(0, worker, "depot", 6, 5) });
        var b = w.Buildings[0];
        b.Hp = 0;
        w.Step(System.Array.Empty<Command>());
        Assert.Empty(w.Buildings);
        Assert.Null(w.GetBuilding(b.Id));
        Assert.True(w.Map.IsPassable(6, 5));
        Assert.True(w.Map.IsPassable(7, 6));
    }

    [Fact]
    public void Unit_Walking_Through_Site_Reroutes_When_Building_Placed()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: TestFactions.Standard);
        w.Players[0].Minerals = 500;
        // walker heads east along y=5, straight through the future site at (8,5)-(9,6)
        var walker = w.SpawnUnit(0, w.Map.CellCenter(2, 5), Fix.FromFraction(1, 2), 30);
        var target = w.Map.CellCenter(15, 5);
        w.Step(new Command[] { new MoveCommand(0, new[] { walker }, target) });

        // a worker near the site drops a building across the walker's path mid-walk
        var builderUnit = w.SpawnUnit(0, w.Map.CellCenter(8, 8), Fix.FromFraction(1, 2), 30);
        for (int i = 0; i < 4; i++) w.Step(System.Array.Empty<Command>());
        w.Step(new Command[] { new BuildCommand(0, builderUnit, "depot", 8, 5) });

        for (int i = 0; i < 300 && w.GetUnit(walker)!.HasMoveOrder; i++)
        {
            w.Step(System.Array.Empty<Command>());
            var (px, py) = w.Map.WorldToCell(w.GetUnit(walker)!.Position);
            Assert.True(w.Map.IsPassable(px, py), $"tick {i}: walker inside impassable cell ({px},{py})");
        }
        Assert.False(w.GetUnit(walker)!.HasMoveOrder);
        Assert.Equal(target, w.GetUnit(walker)!.Position);
    }
}
