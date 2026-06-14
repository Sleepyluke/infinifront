using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class BuildPrerequisiteTests
{
    // A faction where "factory" requires "depot".
    private static FactionDef Faction()
    {
        var none = System.Array.Empty<string>();
        var depot = new BuildingSpec(100, 2, 2, 100, 5, IsDepot: true);
        var factory = new BuildingSpec(120, 2, 2, 120, 5, CanTrain: true);
        return new FactionDef("f", "F",
            units: System.Array.Empty<UnitDef>(),
            buildings: new[]
            {
                new BuildingDef("depot", 1, none, depot),
                new BuildingDef("factory", 2, new[] { "depot" }, factory),
            });
    }

    private static (SimWorld w, int worker) WorldWithWorker()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: Faction());
        w.Players[0].Minerals = 500;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        return (w, worker);
    }

    [Fact]
    public void Build_By_Id_Places_Building()
    {
        var (w, worker) = WorldWithWorker();
        w.Step(new Command[] { new BuildCommand(0, worker, "depot", 6, 5) });
        Assert.Single(w.Buildings);
        Assert.Equal(400, w.Players[0].Minerals);
    }

    [Fact]
    public void Build_Rejected_When_Prerequisite_Missing()
    {
        var (w, worker) = WorldWithWorker();
        w.Step(new Command[] { new BuildCommand(0, worker, "factory", 6, 5) }); // needs depot
        Assert.Empty(w.Buildings);
        Assert.Equal(500, w.Players[0].Minerals);
    }

    [Fact]
    public void Build_Allowed_Once_Prerequisite_Complete()
    {
        var (w, worker) = WorldWithWorker();
        w.Step(new Command[] { new BuildCommand(0, worker, "depot", 6, 5) });
        for (int i = 0; i < 5; i++) w.Step(System.Array.Empty<Command>()); // depot completes (BuildTime 5)
        Assert.True(w.Buildings[0].IsComplete);
        // Factory at (8,4): center (9,5), distance² from worker at (5.5,5.5) = 12.5 ≤ 16 (within range).
        w.Step(new Command[] { new BuildCommand(0, worker, "factory", 8, 4) });
        Assert.Equal(2, w.Buildings.Count);
    }

    [Fact]
    public void Build_Rejected_When_Prerequisite_Incomplete()
    {
        var (w, worker) = WorldWithWorker();
        w.Step(new Command[] { new BuildCommand(0, worker, "depot", 6, 5) }); // not yet complete
        // Factory at (8,4): center (9,5), within range of worker — rejected only due to incomplete prereq.
        w.Step(new Command[] { new BuildCommand(0, worker, "factory", 8, 4) });
        Assert.Single(w.Buildings); // factory rejected — depot incomplete
    }

    [Fact]
    public void Build_Rejected_For_Unknown_Def_Id()
    {
        var (w, worker) = WorldWithWorker();
        w.Step(new Command[] { new BuildCommand(0, worker, "nonesuch", 6, 5) });
        Assert.Empty(w.Buildings);
        Assert.Equal(500, w.Players[0].Minerals);
    }
}
