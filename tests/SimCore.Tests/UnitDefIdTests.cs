using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class UnitDefIdTests
{
    [Fact]
    public void Legacy_Spawn_Has_Empty_DefId()
    {
        var w = new SimWorld(new MapGrid(8, 8), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(1, 1), Fix.FromInt(1), 10);
        Assert.Equal("", w.GetUnit(id)!.DefId);
    }

    [Fact]
    public void Spec_Spawn_With_DefId_Sets_It()
    {
        var w = new SimWorld(new MapGrid(8, 8), seed: 1);
        var spec = new UnitSpec(40, Fix.FromFraction(1, 2), 50, 1, 5);
        var id = w.SpawnUnit(0, w.Map.CellCenter(1, 1), spec, "marine");
        Assert.Equal("marine", w.GetUnit(id)!.DefId);
    }

    [Fact]
    public void Trained_Unit_Carries_Def_Id()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: TestFactions.Standard);
        w.Players[0].Minerals = 1000; w.Players[0].SupplyCap = 20;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(0, worker, "barracks", 7, 5) });
        for (int i = 0; i < TestFactions.BarracksSpec.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        var b = w.Buildings[0].Id;
        w.Step(new Command[] { new TrainCommand(0, b, "marine") });
        for (int i = 0; i < TestFactions.MarineSpec.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal("marine", w.Units[^1].DefId);
    }
}
