using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class RallyDestroyTests
{
    private static (SimWorld w, int rax) TrainerWorld()
    {
        var w = new SimWorld(new MapGrid(30, 30), seed: 1);
        w.Players[0].Minerals = 500;
        w.Players[0].SupplyCap = 10;
        var rax = w.AddCompletedBuilding(0, ReferenceSpecs.Barracks, 5, 5);
        return (w, rax);
    }

    [Fact]
    public void Trained_Unit_Moves_To_Rally()
    {
        var (w, rax) = TrainerWorld();
        w.Step(new Command[] {
            new SetRallyCommand(0, rax, w.Map.CellCenter(20, 20)),
            new TrainCommand(0, rax, ReferenceSpecs.Trooper),
        });
        for (int i = 0; i < ReferenceSpecs.Trooper.BuildTimeTicks + 200; i++) w.Step(System.Array.Empty<Command>());
        var trooper = w.Units[^1];
        var (cx, cy) = w.Map.WorldToCell(trooper.Position);
        Assert.Equal((20, 20), (cx, cy));
    }

    [Fact]
    public void Clear_Rally_Stops_Auto_Move()
    {
        var (w, rax) = TrainerWorld();
        w.Step(new Command[] { new SetRallyCommand(0, rax, w.Map.CellCenter(20, 20)) });
        w.Step(new Command[] { new SetRallyCommand(0, rax, default, Clear: true) });
        Assert.False(w.GetBuilding(rax)!.HasRally);
        w.Step(new Command[] { new TrainCommand(0, rax, ReferenceSpecs.Trooper) });
        for (int i = 0; i < ReferenceSpecs.Trooper.BuildTimeTicks + 5; i++) w.Step(System.Array.Empty<Command>());
        Assert.False(w.Units[^1].HasMoveOrder); // spawned idle at the perimeter
    }

    [Fact]
    public void Enemy_Cannot_Set_My_Rally()
    {
        var (w, rax) = TrainerWorld();
        w.Step(new Command[] { new SetRallyCommand(1, rax, w.Map.CellCenter(20, 20)) });
        Assert.False(w.GetBuilding(rax)!.HasRally);
    }

    [Fact]
    public void Destroy_Kills_Own_Units_And_Releases_Supply()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        w.Players[0].SupplyCap = 10;
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), ReferenceSpecs.Trooper);
        var used = w.Players[0].SupplyUsed;
        w.Step(new Command[] { new DestroyCommand(0, new[] { id }) });
        Assert.Null(w.GetUnit(id));
        Assert.Equal(used - ReferenceSpecs.Trooper.SupplyCost, w.Players[0].SupplyUsed);
        Assert.Equal(0, w.OccupantAt(5, 5)); // occupancy released
    }

    [Fact]
    public void Destroy_Kills_Own_Building_And_Restores_Passability()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var b = w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 5, 5);
        w.Step(new Command[] { new DestroyCommand(0, new[] { b }) });
        Assert.Null(w.GetBuilding(b));
        Assert.True(w.Map.IsPassable(5, 5));
    }

    [Fact]
    public void Destroy_Ignores_Enemy_Ids()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var theirs = w.SpawnUnit(1, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new DestroyCommand(0, new[] { theirs }) });
        Assert.NotNull(w.GetUnit(theirs));
    }
}
