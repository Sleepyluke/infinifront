using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class ResearchTests
{
    private static readonly string[] None = System.Array.Empty<string>();

    private static FactionDef Faction() => new("f", "F",
        units: new[] { new UnitDef("marine", 1, "barracks", None,
            new UnitSpec(40, Fix.FromFraction(1, 2), 50, 1, 5, Weapon: new WeaponSpec(6, Fix.FromInt(2), 5))) },
        buildings: new[]
        {
            new BuildingDef("depot", 1, None, new BuildingSpec(100, 2, 2, 100, 3, IsDepot: true)),
            new BuildingDef("barracks", 1, None, new BuildingSpec(150, 2, 2, 150, 3, CanTrain: true)),
        },
        upgrades: new[]
        {
            new UpgradeDef("dmg1", 1, "barracks", None, new[] { "marine" }, UpgradeStat.Damage, Fix.FromInt(2), 50, 10),
            new UpgradeDef("dmg2", 2, "barracks", new[] { "dmg1" }, new[] { "marine" }, UpgradeStat.Damage, Fix.FromInt(2), 50, 10),
        });

    private static (SimWorld w, int rax) ReadyRax()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: Faction());
        w.Players[0].Minerals = 1000;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(0, worker, "barracks", 7, 5) });
        for (int i = 0; i < 3; i++) w.Step(System.Array.Empty<Command>());
        return (w, w.Buildings[0].Id);
    }

    [Fact]
    public void Research_Completes_And_Applies_Upgrade()
    {
        var (w, rax) = ReadyRax();
        var before = w.Players[0].Minerals;
        w.Step(new Command[] { new ResearchCommand(0, rax, "dmg1") });
        Assert.Equal(before - 50, w.Players[0].Minerals);
        Assert.Equal("dmg1", w.GetBuilding(rax)!.ResearchingId);
        Assert.False(w.Players[0].HasUpgrade("dmg1"));
        for (int i = 0; i < 10; i++) w.Step(System.Array.Empty<Command>());
        Assert.True(w.Players[0].HasUpgrade("dmg1"));
        Assert.Equal("", w.GetBuilding(rax)!.ResearchingId);
    }

    [Fact]
    public void Research_Rejected_When_ResearchedAt_Mismatch()
    {
        var (w, rax) = ReadyRax();
        var worker = w.SpawnUnit(0, w.Map.CellCenter(15, 15), Fix.FromFraction(1, 2), 30);
        // depot at (16,16): footprint (16,16)-(17,17) is off the worker's cell (15,15) but in build range
        w.Step(new Command[] { new BuildCommand(0, worker, "depot", 16, 16) });
        for (int i = 0; i < 3; i++) w.Step(System.Array.Empty<Command>());
        var depot = w.Buildings[^1].Id;
        w.Step(new Command[] { new ResearchCommand(0, depot, "dmg1") });
        Assert.Equal("", w.GetBuilding(depot)!.ResearchingId);
    }

    [Fact]
    public void Research_Rejected_When_Upgrade_Prereq_Missing()
    {
        var (w, rax) = ReadyRax();
        w.Step(new Command[] { new ResearchCommand(0, rax, "dmg2") });
        Assert.Equal("", w.GetBuilding(rax)!.ResearchingId);
    }

    [Fact]
    public void Research_Allowed_When_Upgrade_Prereq_Present()
    {
        var (w, rax) = ReadyRax();
        w.Step(new Command[] { new ResearchCommand(0, rax, "dmg1") });
        for (int i = 0; i < 10; i++) w.Step(System.Array.Empty<Command>());
        w.Step(new Command[] { new ResearchCommand(0, rax, "dmg2") });
        Assert.Equal("dmg2", w.GetBuilding(rax)!.ResearchingId);
    }

    [Fact]
    public void Cannot_Research_Same_Upgrade_Twice_Or_While_Busy()
    {
        var (w, rax) = ReadyRax();
        w.Step(new Command[] { new ResearchCommand(0, rax, "dmg1") });
        var mins = w.Players[0].Minerals;
        w.Step(new Command[] { new ResearchCommand(0, rax, "dmg1") });
        Assert.Equal(mins, w.Players[0].Minerals);
        for (int i = 0; i < 10; i++) w.Step(System.Array.Empty<Command>());
        w.Step(new Command[] { new ResearchCommand(0, rax, "dmg1") });
        Assert.Equal("", w.GetBuilding(rax)!.ResearchingId);
    }

    [Fact]
    public void Research_Rejected_For_Unknown_Upgrade()
    {
        var (w, rax) = ReadyRax();
        w.Step(new Command[] { new ResearchCommand(0, rax, "nope") });
        Assert.Equal("", w.GetBuilding(rax)!.ResearchingId);
    }
}
