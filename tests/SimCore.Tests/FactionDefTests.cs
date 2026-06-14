using System.Collections.Generic;
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class FactionDefTests
{
    private static UnitSpec Spec() => new(40, Fix.FromFraction(1, 2), 50, 1, 20);
    private static BuildingSpec BSpec() => new(100, 2, 2, 100, 10, CanTrain: true);

    [Fact]
    public void Lookup_By_Id_Returns_Defs_And_Null_For_Unknown()
    {
        var faction = new FactionDef("ref", "Reference",
            units: new[] { new UnitDef("trooper", 1, "barracks", new string[0], Spec()) },
            buildings: new[] { new BuildingDef("barracks", 1, new string[0], BSpec()) });

        Assert.Equal("ref", faction.Id);
        Assert.Equal("Reference", faction.Name);
        Assert.Equal("trooper", faction.GetUnit("trooper")!.Id);
        Assert.Equal(1, faction.GetUnit("trooper")!.Tier);
        Assert.Equal("barracks", faction.GetUnit("trooper")!.ProducedBy);
        Assert.Equal("barracks", faction.GetBuilding("barracks")!.Id);
        Assert.Null(faction.GetUnit("nope"));
        Assert.Null(faction.GetBuilding("nope"));
        Assert.Single(faction.UnitList);
        Assert.Single(faction.BuildingList);
    }
}
