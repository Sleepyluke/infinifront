using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class UnitDeathTests
{
    private static SimWorld NewWorld() => new(new MapGrid(16, 16), seed: 1);

    [Fact]
    public void Dead_Units_Are_Removed_After_Step()
    {
        var w = NewWorld();
        var id = w.SpawnUnit(0, FixVec.FromInts(1, 1), Fix.FromInt(1), 10);
        w.GetUnit(id)!.Hp = 0;
        w.Step(System.Array.Empty<Command>());
        Assert.Null(w.GetUnit(id));
        Assert.Empty(w.Units);
    }

    [Fact]
    public void Survivor_Order_Is_Preserved()
    {
        var w = NewWorld();
        var a = w.SpawnUnit(0, FixVec.FromInts(1, 1), Fix.FromInt(1), 10);
        var b = w.SpawnUnit(0, FixVec.FromInts(2, 2), Fix.FromInt(1), 10);
        var c = w.SpawnUnit(0, FixVec.FromInts(3, 3), Fix.FromInt(1), 10);
        w.GetUnit(b)!.Hp = -5;
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(new[] { a, c }, System.Linq.Enumerable.Select(w.Units, u => u.Id));
    }

    [Fact]
    public void Commands_To_Dead_Ids_Are_Ignored()
    {
        var w = NewWorld();
        var id = w.SpawnUnit(0, FixVec.FromInts(1, 1), Fix.FromInt(1), 10);
        w.GetUnit(id)!.Hp = 0;
        w.Step(System.Array.Empty<Command>());
        // must not throw
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, FixVec.FromInts(5, 5)) });
        Assert.Equal(2, w.Tick);
    }
}
