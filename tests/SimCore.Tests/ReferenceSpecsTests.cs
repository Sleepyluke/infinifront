using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class ReferenceSpecsTests
{
    [Fact]
    public void All_Units_Have_Positive_Costs_And_Hp()
    {
        foreach (var s in new[] { ReferenceSpecs.Fabber, ReferenceSpecs.Trooper, ReferenceSpecs.Outrider, ReferenceSpecs.Tank })
        {
            Assert.True(s.MaxHp > 0);
            Assert.True(s.MineralCost > 0);
            Assert.True(s.SupplyCost > 0);
            Assert.True(s.BuildTimeTicks > 0);
            Assert.True(s.Speed > Fix.Zero);
        }
    }

    [Fact]
    public void Fabber_Is_The_Only_Harvester()
    {
        Assert.NotNull(ReferenceSpecs.Fabber.Harvester);
        Assert.Null(ReferenceSpecs.Trooper.Harvester);
        Assert.Null(ReferenceSpecs.Outrider.Harvester);
        Assert.Null(ReferenceSpecs.Tank.Harvester);
    }

    [Fact]
    public void Combat_Units_Have_Weapons_Fabber_Does_Not()
    {
        Assert.Null(ReferenceSpecs.Fabber.Weapon);
        Assert.NotNull(ReferenceSpecs.Trooper.Weapon);
        Assert.NotNull(ReferenceSpecs.Outrider.Weapon);
        Assert.NotNull(ReferenceSpecs.Tank.Weapon);
    }

    [Fact]
    public void Depot_Provides_Supply_And_Is_Depot()
    {
        Assert.True(ReferenceSpecs.Depot.SupplyProvided > 0);
        Assert.True(ReferenceSpecs.Depot.IsDepot);
        Assert.False(ReferenceSpecs.Depot.CanTrain);
    }

    [Fact]
    public void Barracks_Trains_And_Is_Not_A_Depot()
    {
        Assert.True(ReferenceSpecs.Barracks.CanTrain);
        Assert.False(ReferenceSpecs.Barracks.IsDepot);
    }

    [Fact]
    public void Trained_Unit_Can_Be_Afforded_From_One_Depot_Of_Supply()
    {
        foreach (var s in new[] { ReferenceSpecs.Trooper, ReferenceSpecs.Outrider, ReferenceSpecs.Tank })
            Assert.True(s.SupplyCost <= ReferenceSpecs.Depot.SupplyProvided);
    }
}
