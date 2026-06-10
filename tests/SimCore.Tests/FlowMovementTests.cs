using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class FlowMovementTests
{
    [Fact]
    public void Unit_Walks_Around_Wall_To_Target()
    {
        var map = new MapGrid(10, 10);
        for (int y = 0; y < 9; y++) map.SetPassable(5, y, false); // wall, gap at y=9
        var w = new SimWorld(map, seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(1, 0), Fix.FromFraction(1, 2), 50);

        var target = w.Map.CellCenter(8, 0);
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, target) });
        for (int i = 0; i < 200 && w.GetUnit(id)!.HasMoveOrder; i++)
            w.Step(System.Array.Empty<Command>());

        var u = w.GetUnit(id)!;
        Assert.False(u.HasMoveOrder);                 // arrived
        Assert.Equal(target, u.Position);             // exactly at target
    }

    [Fact]
    public void Unit_With_Unreachable_Target_Gives_Up()
    {
        var map = new MapGrid(10, 10);
        for (int y = 0; y < 10; y++) map.SetPassable(5, y, false); // full wall
        var w = new SimWorld(map, seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(1, 5), Fix.FromFraction(1, 2), 50);

        w.Step(new Command[] { new MoveCommand(0, new[] { id }, w.Map.CellCenter(8, 5)) });
        w.Step(System.Array.Empty<Command>());

        var u = w.GetUnit(id)!;
        Assert.False(u.HasMoveOrder);                          // order cancelled
        Assert.Equal(w.Map.CellCenter(1, 5), u.Position);      // didn't move
    }

    [Fact]
    public void Unit_Does_Not_Cut_Corners()
    {
        // L-shaped wall around the corner cell (4,4): block east and south-east approach
        var map = new MapGrid(10, 10);
        map.SetPassable(5, 4, false);
        map.SetPassable(4, 5, false);
        var w = new SimWorld(map, seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(4, 4), Fix.FromFraction(1, 2), 50);

        var target = w.Map.CellCenter(5, 5); // diagonal neighbor, but corner is walled
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, target) });
        for (int i = 0; i < 200 && w.GetUnit(id)!.HasMoveOrder; i++)
            w.Step(System.Array.Empty<Command>());

        var u = w.GetUnit(id)!;
        Assert.False(u.HasMoveOrder);   // arrived (via a detour, not through the corner)
        Assert.Equal(target, u.Position);
    }
}
