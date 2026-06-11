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
}
