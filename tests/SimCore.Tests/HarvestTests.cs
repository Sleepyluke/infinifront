using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class HarvestTests
{
    private static readonly UnitSpec WorkerSpec =
        new(MaxHp: 30, Speed: Fix.FromFraction(1, 2), MineralCost: 50, SupplyCost: 1, BuildTimeTicks: 10,
            Harvester: new HarvesterSpec(CarryCapacity: 5, GatherTicks: 4));

    private static (SimWorld w, int worker, int nodeId) EconomyWorld()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        w.Players[0].Minerals = 200;
        w.Players[0].SupplyCap = 10;
        var nodeId = w.AddResourceNode(12, 5, amount: 12);
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), WorkerSpec);
        // completed depot at (3,4): place + finish construction
        w.Step(new Command[] { new BuildCommand(0, worker, BuildingTests.Depot, 3, 4) });
        for (int i = 0; i < BuildingTests.Depot.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        return (w, worker, nodeId);
    }

    [Fact]
    public void Node_Blocks_Passability()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        w.AddResourceNode(12, 5, 100);
        Assert.False(w.Map.IsPassable(12, 5));
    }

    [Fact]
    public void Full_Harvest_Cycle_Deposits_Minerals()
    {
        var (w, worker, _) = EconomyWorld();
        var start = w.Players[0].Minerals;
        w.Step(new Command[] { new HarvestCommand(0, new[] { worker }, w.Nodes[0].Id) });
        for (int i = 0; i < 300 && w.Players[0].Minerals == start; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(start + 5, w.Players[0].Minerals); // one full carry deposited
    }

    [Fact]
    public void Depleted_Node_Is_Removed_And_Cell_Unblocked()
    {
        var (w, worker, nodeId) = EconomyWorld();
        w.Step(new Command[] { new HarvestCommand(0, new[] { worker }, nodeId) });
        // 12 minerals at 5/trip = 3 trips
        for (int i = 0; i < 900 && w.GetNode(nodeId) is not null; i++) w.Step(System.Array.Empty<Command>());
        Assert.Null(w.GetNode(nodeId));
        Assert.True(w.Map.IsPassable(12, 5));
        // let the worker finish its last return trip
        for (int i = 0; i < 300 && w.GetUnit(worker)!.HarvestPhase != HarvestPhase.None; i++)
            w.Step(System.Array.Empty<Command>());
        // 200 start - 100 depot + 12 fully harvested = 112
        Assert.Equal(112, w.Players[0].Minerals);
    }

    [Fact]
    public void NonHarvester_Ignores_Harvest_Command()
    {
        var (w, _, nodeId) = EconomyWorld();
        var soldier = w.SpawnUnit(0, w.Map.CellCenter(6, 6), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new HarvestCommand(0, new[] { soldier }, nodeId) });
        Assert.Equal(HarvestPhase.None, w.GetUnit(soldier)!.HarvestPhase);
    }
}
