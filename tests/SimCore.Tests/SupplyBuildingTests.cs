using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class SupplyBuildingTests
{
    [Fact]
    public void SupplySilo_Provides_Supply_And_Does_Not_Train()
    {
        Assert.True(ReferenceSpecs.SupplySilo.SupplyProvided > 0);
        Assert.False(ReferenceSpecs.SupplySilo.IsDepot);
        Assert.False(ReferenceSpecs.SupplySilo.CanTrain);
    }

    [Fact]
    public void Reference_Has_A_Supply_Building_Def()
    {
        Assert.Contains(ReferenceFaction.Def.BuildingList, b => b.Id == "supply");
    }

    [Fact]
    public void Completing_A_Silo_Raises_Supply_Cap()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, playerCount: 2,
            faction: ReferenceFaction.Def);
        int before = w.Players[0].SupplyCap;
        w.AddCompletedBuilding(0, ReferenceSpecs.SupplySilo, 2, 2, "supply");
        Assert.Equal(before + ReferenceSpecs.SupplySilo.SupplyProvided, w.Players[0].SupplyCap);
    }
}
