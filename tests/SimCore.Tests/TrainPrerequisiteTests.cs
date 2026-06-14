using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class TrainPrerequisiteTests
{
    private static (SimWorld w, int barracks) ReadyBarracks()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: TestFactions.Standard);
        w.Players[0].Minerals = 1000;
        w.Players[0].SupplyCap = 20;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(0, worker, "barracks", 7, 5) });
        for (int i = 0; i < TestFactions.BarracksSpec.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        return (w, w.Buildings[0].Id);
    }

    [Fact]
    public void Train_By_Id_Enqueues_And_Spawns()
    {
        var (w, b) = ReadyBarracks();
        var before = w.Units.Count;
        w.Step(new Command[] { new TrainCommand(0, b, "marine") });
        for (int i = 0; i < TestFactions.MarineSpec.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(before + 1, w.Units.Count);
        Assert.Equal(6, w.Units[^1].Weapon!.Damage); // marine stats from the def spec
    }

    [Fact]
    public void Train_Rejected_When_ProducedBy_Mismatch()
    {
        // marine is produced_by "barracks"; a depot cannot train it.
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: TestFactions.Standard);
        w.Players[0].Minerals = 1000; w.Players[0].SupplyCap = 20;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(0, worker, "depot", 7, 5) });
        for (int i = 0; i < TestFactions.DepotSpec.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        var depotId = w.Buildings[0].Id;
        w.Step(new Command[] { new TrainCommand(0, depotId, "marine") }); // depot can't train (not CanTrain, wrong producer)
        Assert.Empty(w.GetBuilding(depotId)!.Queue);
    }

    [Fact]
    public void Train_Rejected_When_Tier2_Prerequisite_Missing()
    {
        // tank requires "depot"; barracks alone can't train it.
        var (w, b) = ReadyBarracks(); // only a barracks exists
        w.Step(new Command[] { new TrainCommand(0, b, "tank") });
        Assert.Empty(w.GetBuilding(b)!.Queue);
    }

    [Fact]
    public void Train_Allowed_When_Tier2_Prerequisite_Present()
    {
        var (w, b) = ReadyBarracks();
        var worker = w.SpawnUnit(0, w.Map.CellCenter(15, 17), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(0, worker, "depot", 14, 15) });
        for (int i = 0; i < TestFactions.DepotSpec.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        w.Step(new Command[] { new TrainCommand(0, b, "tank") });
        Assert.Single(w.GetBuilding(b)!.Queue); // depot present → tank accepted
    }

    [Fact]
    public void Train_Rejected_For_Unknown_Unit_Id()
    {
        var (w, b) = ReadyBarracks();
        w.Step(new Command[] { new TrainCommand(0, b, "griffon") });
        Assert.Empty(w.GetBuilding(b)!.Queue);
    }
}
