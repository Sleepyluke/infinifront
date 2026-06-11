using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class ConstructionTests
{
    private static (SimWorld w, int buildingId) PlacedDepot()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        w.Players[0].Minerals = 500;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(0, worker, BuildingTests.Depot, 6, 5) });
        return (w, w.Buildings[0].Id);
    }

    [Fact]
    public void Building_Completes_After_BuildTime_And_Grants_Supply()
    {
        var (w, id) = PlacedDepot();
        Assert.False(w.GetBuilding(id)!.IsComplete);
        Assert.Equal(0, w.Players[0].SupplyCap);

        for (int i = 0; i < BuildingTests.Depot.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        Assert.True(w.GetBuilding(id)!.IsComplete);
        Assert.Equal(8, w.Players[0].SupplyCap);

        // supply granted exactly once
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(8, w.Players[0].SupplyCap);
    }

    [Fact]
    public void Destroyed_Incomplete_Building_Grants_No_Supply_And_Refunds_Nothing()
    {
        var (w, id) = PlacedDepot();
        w.GetBuilding(id)!.Hp = 0;
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.Players[0].SupplyCap);
        Assert.Equal(400, w.Players[0].Minerals);
    }

    [Fact]
    public void Destroyed_Complete_Building_Revokes_Supply()
    {
        var (w, id) = PlacedDepot();
        for (int i = 0; i < BuildingTests.Depot.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(8, w.Players[0].SupplyCap);
        w.GetBuilding(id)!.Hp = 0;
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.Players[0].SupplyCap);
    }

    [Fact]
    public void Same_Tick_Completion_And_Destruction_Nets_Zero_Supply()
    {
        var (w, id) = PlacedDepot();
        // advance to one tick before completion, then kill it on the completing tick
        for (int i = 0; i < BuildingTests.Depot.BuildTimeTicks - 1; i++) w.Step(System.Array.Empty<Command>());
        w.GetBuilding(id)!.Hp = 0;
        w.Step(System.Array.Empty<Command>()); // UpdateConstruction grants, RemoveDeadBuildings revokes
        Assert.Empty(w.Buildings);
        Assert.Equal(0, w.Players[0].SupplyCap);
    }
}
