using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class ProductionTests
{
    private static readonly BuildingSpec Barracks =
        new(MaxHp: 150, Width: 2, Height: 2, MineralCost: 150, BuildTimeTicks: 5, CanTrain: true);

    private static readonly UnitSpec Marine =
        new(MaxHp: 40, Speed: Fix.FromFraction(1, 2), MineralCost: 50, SupplyCost: 1, BuildTimeTicks: 8,
            Weapon: new WeaponSpec(6, Fix.FromInt(2), 5));

    private static (SimWorld w, int barracksId) ReadyWorld()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        w.Players[0].Minerals = 1000;
        w.Players[0].SupplyCap = 10;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(0, worker, Barracks, 7, 5) });
        for (int i = 0; i < Barracks.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        return (w, w.Buildings[0].Id);
    }

    [Fact]
    public void Train_Spawns_Unit_With_Spec_After_BuildTime()
    {
        var (w, bid) = ReadyWorld();
        var unitsBefore = w.Units.Count;
        var mineralsBefore = w.Players[0].Minerals;

        w.Step(new Command[] { new TrainCommand(0, bid, Marine) });
        Assert.Equal(mineralsBefore - 50, w.Players[0].Minerals);
        Assert.Equal(1, w.Players[0].SupplyUsed);

        for (int i = 0; i < Marine.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(unitsBefore + 1, w.Units.Count);
        var trained = w.Units[^1];
        Assert.Equal(40, trained.Hp);
        Assert.Equal(6, trained.Weapon!.Damage);
        // spawned adjacent to footprint
        var (cx, cy) = w.Map.WorldToCell(trained.Position);
        Assert.InRange(cx, 6, 9);
        Assert.InRange(cy, 4, 7);
        Assert.True(w.Map.IsPassable(cx, cy));
    }

    [Fact]
    public void Train_Rejected_On_Insufficient_Supply()
    {
        var (w, bid) = ReadyWorld();
        w.Players[0].SupplyCap = w.Players[0].SupplyUsed; // no headroom
        var minerals = w.Players[0].Minerals;
        w.Step(new Command[] { new TrainCommand(0, bid, Marine) });
        Assert.Equal(minerals, w.Players[0].Minerals);
        Assert.Empty(w.GetBuilding(bid)!.Queue);
    }

    [Fact]
    public void Queue_Caps_At_Five()
    {
        var (w, bid) = ReadyWorld();
        var cmds = new Command[7];
        for (int i = 0; i < 7; i++) cmds[i] = new TrainCommand(0, bid, Marine);
        w.Step(cmds);
        Assert.Equal(5, w.GetBuilding(bid)!.Queue.Count);
        Assert.Equal(1000 - 150 - 5 * 50, w.Players[0].Minerals); // barracks + 5 marines paid
    }

    [Fact]
    public void Incomplete_Or_NonTrainer_Building_Rejects_Train()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        w.Players[0].Minerals = 1000;
        w.Players[0].SupplyCap = 10;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(0, worker, Barracks, 7, 5) });
        var bid = w.Buildings[0].Id; // still under construction
        w.Step(new Command[] { new TrainCommand(0, bid, Marine) });
        Assert.Empty(w.GetBuilding(bid)!.Queue);
    }
}
