using SimCore.Sim;
using Xunit;

public class FlowFieldTests
{
    [Fact]
    public void Open_Map_Points_Toward_Target()
    {
        var g = new MapGrid(10, 10);
        var f = FlowField.Compute(g, targetX: 9, targetY: 5);
        var (dx, dy) = f.DirectionAt(0, 5);
        Assert.Equal(1, dx);  // straight east
        Assert.Equal(0, dy);
    }

    [Fact]
    public void Target_Cell_Has_Zero_Direction()
    {
        var g = new MapGrid(10, 10);
        var f = FlowField.Compute(g, 9, 5);
        Assert.Equal((0, 0), f.DirectionAt(9, 5));
    }

    [Fact]
    public void Flow_Routes_Around_Wall()
    {
        var g = new MapGrid(10, 10);
        for (int y = 0; y < 9; y++) g.SetPassable(5, y, false); // wall with gap at y=9
        var f = FlowField.Compute(g, 9, 0);
        // west of the wall at (4,0): direct east is blocked, flow must head south toward the gap
        var (_, dy) = f.DirectionAt(4, 0);
        Assert.Equal(1, dy);
    }

    [Fact]
    public void Unreachable_Cell_Has_Zero_Direction()
    {
        var g = new MapGrid(10, 10);
        for (int y = 0; y < 10; y++) g.SetPassable(5, y, false); // full wall
        var f = FlowField.Compute(g, 9, 5);
        Assert.Equal((0, 0), f.DirectionAt(0, 5));
    }

    [Fact]
    public void Same_Inputs_Produce_Identical_Fields()
    {
        var g = new MapGrid(20, 20);
        g.SetPassable(10, 10, false);
        var a = FlowField.Compute(g, 15, 15);
        var b = FlowField.Compute(g, 15, 15);
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 20; x++)
                Assert.Equal(a.DirectionAt(x, y), b.DirectionAt(x, y));
    }
}
