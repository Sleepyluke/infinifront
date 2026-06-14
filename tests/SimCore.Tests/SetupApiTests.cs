using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class SetupApiTests
{
    [Fact]
    public void Completed_Building_Is_Complete_With_Full_Hp_And_Supply()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var capBefore = w.Players[0].SupplyCap;
        var id = w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 5, 5);
        var b = w.GetBuilding(id)!;
        Assert.True(b.IsComplete);
        Assert.Equal(ReferenceSpecs.Depot.MaxHp, b.Hp);
        Assert.Equal(capBefore + ReferenceSpecs.Depot.SupplyProvided, w.Players[0].SupplyCap);
        Assert.False(w.Map.IsPassable(5, 5)); // footprint blocked
    }

    [Fact]
    public void Completed_Barracks_Can_Train_Immediately()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: ReferenceFaction.Def);
        w.Players[0].Minerals = 500;
        w.Players[0].SupplyCap = 10;
        var id = w.AddCompletedBuilding(0, ReferenceSpecs.Barracks, 5, 5, "barracks");
        w.Step(new Command[] { new TrainCommand(0, id, "trooper") });
        Assert.Single(w.GetBuilding(id)!.Queue);
    }

    [Fact]
    public void Determinism_Holds_With_Setup_Buildings()
    {
        ulong Run()
        {
            var w = new SimWorld(new MapGrid(20, 20), seed: 7);
            w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 3, 3);
            w.AddCompletedBuilding(1, ReferenceSpecs.Depot, 14, 14);
            for (int i = 0; i < 50; i++) w.Step(System.Array.Empty<Command>());
            return StateHasher.Hash(w);
        }
        Assert.Equal(Run(), Run());
    }
}
