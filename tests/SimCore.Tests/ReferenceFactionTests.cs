using SimCore.Sim;
using Xunit;

public class ReferenceFactionTests
{
    [Fact]
    public void Reference_Faction_Is_Valid()
    {
        Assert.Empty(ReferenceFaction.Def.Validate());
    }

    [Fact]
    public void Reference_Faction_Has_Expected_Tech_Tree()
    {
        var f = ReferenceFaction.Def;
        Assert.Equal("depot", f.GetUnit("fabber")!.ProducedBy);
        Assert.Equal("barracks", f.GetUnit("trooper")!.ProducedBy);
        Assert.Equal("barracks", f.GetUnit("outrider")!.ProducedBy);
        Assert.Equal("barracks", f.GetUnit("tank")!.ProducedBy);
        Assert.Equal(2, f.GetUnit("tank")!.Tier);
        Assert.Contains("depot", f.GetUnit("tank")!.Requires);   // tank gated behind depot
        Assert.Empty(f.GetBuilding("depot")!.Requires);
        Assert.Empty(f.GetBuilding("barracks")!.Requires);
    }
}
