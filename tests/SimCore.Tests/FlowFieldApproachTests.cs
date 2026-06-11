using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class FlowFieldApproachTests
{
    [Fact]
    public void Field_Toward_Impassable_Cell_Leads_To_Its_Neighbors()
    {
        var g = new MapGrid(10, 10);
        g.SetPassable(5, 5, false); // a "building" cell
        var f = FlowField.Compute(g, 5, 5);
        var (dx, dy) = f.DirectionAt(2, 5);
        Assert.Equal(1, dx); // flows east toward the neighbors of (5,5)
        Assert.Equal((0, 0), f.DirectionAt(4, 5)); // adjacent cell is a seed — terminal
    }

    [Fact]
    public void Unit_Ordered_Onto_Building_Cell_Stops_Adjacent()
    {
        var map = new MapGrid(12, 12);
        map.SetPassable(8, 5, false);
        var w = new SimWorld(map, seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, w.Map.CellCenter(8, 5)) });
        for (int i = 0; i < 200 && w.GetUnit(id)!.HasMoveOrder; i++) w.Step(System.Array.Empty<Command>());

        var (cx, cy) = w.Map.WorldToCell(w.GetUnit(id)!.Position);
        Assert.True(System.Math.Abs(cx - 8) <= 1 && System.Math.Abs(cy - 5) <= 1, $"unit at ({cx},{cy}) not adjacent to (8,5)");
        Assert.True(map.IsPassable(cx, cy));
        Assert.False(w.GetUnit(id)!.HasMoveOrder);
    }

    [Fact]
    public void Enclosed_Impassable_Target_Still_Unreachable()
    {
        var g = new MapGrid(10, 10);
        // 3x3 solid block: target at center, all neighbors also impassable
        for (int y = 4; y <= 6; y++)
            for (int x = 4; x <= 6; x++)
                g.SetPassable(x, y, false);
        var f = FlowField.Compute(g, 5, 5);
        Assert.Equal((0, 0), f.DirectionAt(0, 0)); // empty field — give-up semantics preserved
    }
}
