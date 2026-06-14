using SimCore.Sim;
using Xunit;

public class SimWorldFactionTests
{
    [Fact]
    public void World_Exposes_Its_Faction()
    {
        var w = new SimWorld(new MapGrid(8, 8), seed: 1, faction: TestFactions.Standard);
        Assert.Same(TestFactions.Standard, w.Faction);
        Assert.Equal("depot", w.Faction!.GetBuilding("depot")!.Id);
    }

    [Fact]
    public void Faction_Defaults_Null_For_Legacy_Construction()
    {
        var w = new SimWorld(new MapGrid(8, 8), seed: 1);
        Assert.Null(w.Faction);
    }
}
