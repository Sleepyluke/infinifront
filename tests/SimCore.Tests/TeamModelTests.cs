using SimCore.Math;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class TeamModelTests
{
    private static SimWorld World(int players) =>
        new SimWorld(new MapGrid(16, 16), seed: 1, playerCount: players, faction: null);

    [Fact]
    public void Players_Default_To_Solo_Teams()
    {
        var w = World(4);
        Assert.Equal(0, w.Players[0].Team);
        Assert.Equal(1, w.Players[1].Team);
        Assert.Equal(2, w.Players[2].Team);
        Assert.Equal(3, w.Players[3].Team);
        Assert.True(w.SameTeam(2, 2));
        Assert.False(w.SameTeam(0, 1));
    }

    [Fact]
    public void SetTeam_Groups_Players()
    {
        var w = World(4);
        w.SetTeam(0, 0); w.SetTeam(1, 1); w.SetTeam(2, 0); w.SetTeam(3, 1);
        Assert.True(w.SameTeam(0, 2));   // team 0
        Assert.True(w.SameTeam(1, 3));   // team 1
        Assert.False(w.SameTeam(0, 1));
        Assert.False(w.SameTeam(2, 3));
    }
}
