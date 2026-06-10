using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class SimWorldTests
{
    private static SimWorld NewWorld() => new(new MapGrid(32, 32), seed: 1);

    [Fact]
    public void Spawn_Assigns_Sequential_Ids()
    {
        var w = NewWorld();
        var a = w.SpawnUnit(ownerId: 0, pos: FixVec.FromInts(1, 1), speedPerTick: Fix.FromFraction(1, 10), hp: 50);
        var b = w.SpawnUnit(ownerId: 1, pos: FixVec.FromInts(2, 2), speedPerTick: Fix.FromFraction(1, 10), hp: 50);
        Assert.Equal(1, a);
        Assert.Equal(2, b);
    }

    [Fact]
    public void Tick_Advances()
    {
        var w = NewWorld();
        w.Step(System.Array.Empty<Command>());
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(2, w.Tick);
    }

    [Fact]
    public void MoveCommand_Moves_Unit_Toward_Target()
    {
        var w = NewWorld();
        var id = w.SpawnUnit(0, FixVec.FromInts(0, 0), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new MoveCommand(PlayerId: 0, UnitIds: new[] { id }, Target: FixVec.FromInts(10, 0)) });
        // unit moves toward target via flow field (cell centers)
        Assert.True(w.GetUnit(id)!.Position.X > Fix.Zero);
    }

    [Fact]
    public void Unit_Stops_At_Target()
    {
        var w = NewWorld();
        var id = w.SpawnUnit(0, FixVec.FromInts(0, 0), Fix.FromInt(1), 50);
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, FixVec.FromInts(3, 0)) });
        for (int i = 0; i < 10; i++) w.Step(System.Array.Empty<Command>());
        var u = w.GetUnit(id)!;
        Assert.Equal(Fix.FromInt(3), u.Position.X);
        Assert.False(u.HasMoveOrder);
    }

    [Fact]
    public void Move_Ignores_Units_Not_Owned_By_Player()
    {
        var w = NewWorld();
        var id = w.SpawnUnit(ownerId: 1, FixVec.FromInts(0, 0), Fix.FromInt(1), 50);
        w.Step(new Command[] { new MoveCommand(PlayerId: 0, new[] { id }, FixVec.FromInts(5, 0)) });
        Assert.Equal(Fix.Zero, w.GetUnit(id)!.Position.X);
    }
}
