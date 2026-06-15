using SimCore.Math;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class TeamVisionTests
{
    private static SimWorld World()
    {
        var w = new SimWorld(new MapGrid(32, 32), seed: 1, playerCount: 4, faction: null);
        return w;
    }

    [Fact]
    public void Ally_Sees_Through_Teammates_Vision()
    {
        var w = World();
        w.SetTeam(0, 5); w.SetTeam(1, 5);                 // 0 and 1 allied
        // Only player 1 has a unit, far from origin; player 0 has none there.
        int cx = 25, cy = 25;
        w.SpawnUnit(1, w.Map.CellCenter(cx, cy), Fix.FromInt(1), hp: 10);
        w.Step(System.Array.Empty<Command>());            // UpdateVision runs in Step
        Assert.True(w.IsVisibleTo(1, cx, cy));            // the unit's owner sees it
        Assert.True(w.IsVisibleTo(0, cx, cy));            // the ALLY sees it too (shared vision)
        Assert.False(w.IsVisibleTo(2, cx, cy));           // an enemy (different team) does not
    }

    [Fact]
    public void Solo_Players_Do_Not_Share_Vision()
    {
        var w = World();                                  // default solo teams
        int cx = 25, cy = 25;
        w.SpawnUnit(1, w.Map.CellCenter(cx, cy), Fix.FromInt(1), hp: 10);
        w.Step(System.Array.Empty<Command>());
        Assert.True(w.IsVisibleTo(1, cx, cy));
        Assert.False(w.IsVisibleTo(0, cx, cy));           // solo → no sharing (today's behavior)
    }
}
