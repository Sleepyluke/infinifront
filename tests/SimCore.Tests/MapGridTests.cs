using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class MapGridTests
{
    [Fact]
    public void New_Grid_Is_All_Passable()
    {
        var g = new MapGrid(8, 8);
        Assert.True(g.IsPassable(0, 0));
        Assert.True(g.IsPassable(7, 7));
    }

    [Fact]
    public void Blocked_Cell_Is_Impassable()
    {
        var g = new MapGrid(8, 8);
        g.SetPassable(3, 4, false);
        Assert.False(g.IsPassable(3, 4));
    }

    [Fact]
    public void Out_Of_Bounds_Is_Impassable()
    {
        var g = new MapGrid(8, 8);
        Assert.False(g.IsPassable(-1, 0));
        Assert.False(g.IsPassable(8, 0));
    }

    [Fact]
    public void WorldToCell_Floors()
    {
        var g = new MapGrid(8, 8);
        var (cx, cy) = g.WorldToCell(new FixVec(Fix.FromFraction(5, 2), Fix.FromFraction(7, 2)));
        Assert.Equal(2, cx);
        Assert.Equal(3, cy);
    }

    [Fact]
    public void CellCenter_Is_Cell_Plus_Half()
    {
        var g = new MapGrid(8, 8);
        var c = g.CellCenter(2, 3);
        Assert.Equal(Fix.FromFraction(5, 2), c.X);
        Assert.Equal(Fix.FromFraction(7, 2), c.Y);
    }
}
