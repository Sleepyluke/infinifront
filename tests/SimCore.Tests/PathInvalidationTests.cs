using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class PathInvalidationTests
{
    [Fact]
    public void Version_Increments_Only_On_Real_Change()
    {
        var g = new MapGrid(8, 8);
        var v0 = g.Version;
        g.SetPassable(3, 3, true);   // already true — no-op
        Assert.Equal(v0, g.Version);
        g.SetPassable(3, 3, false);  // real change
        Assert.Equal(v0 + 1, g.Version);
        g.SetPassable(-1, 0, false); // out of bounds — no-op
        Assert.Equal(v0 + 1, g.Version);
    }

    [Fact]
    public void Unit_Reroutes_When_Wall_Appears_Mid_Walk()
    {
        var map = new MapGrid(12, 12);
        var w = new SimWorld(map, seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(1, 5), Fix.FromFraction(1, 2), 50);
        var target = w.Map.CellCenter(10, 5);
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, target) });

        // after 4 ticks (unit ~2 cells in), drop a wall across its straight path, gap at y=11
        for (int i = 0; i < 4; i++) w.Step(System.Array.Empty<Command>());
        for (int y = 0; y < 11; y++) map.SetPassable(6, y, false);

        for (int i = 0; i < 300 && w.GetUnit(id)!.HasMoveOrder; i++)
        {
            w.Step(System.Array.Empty<Command>());
            var (px, py) = w.Map.WorldToCell(w.GetUnit(id)!.Position);
            Assert.True(map.IsPassable(px, py), $"tick {i}: unit inside impassable cell ({px},{py})");
        }
        Assert.False(w.GetUnit(id)!.HasMoveOrder);
        Assert.Equal(target, w.GetUnit(id)!.Position);
    }

    [Fact]
    public void Unit_Cancels_When_Target_Becomes_Unreachable()
    {
        var map = new MapGrid(12, 12);
        var w = new SimWorld(map, seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(1, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, w.Map.CellCenter(10, 5)) });
        for (int y = 0; y < 12; y++) map.SetPassable(6, y, false); // full wall
        for (int i = 0; i < 50 && w.GetUnit(id)!.HasMoveOrder; i++) w.Step(System.Array.Empty<Command>());
        Assert.False(w.GetUnit(id)!.HasMoveOrder);
    }
}
