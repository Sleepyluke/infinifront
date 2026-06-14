using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class AppliedUpgradeTests
{
    [Fact]
    public void Add_Upgrade_Is_Sorted_And_Queryable()
    {
        var ps = new PlayerState();
        ps.AddUpgrade("zeta");
        ps.AddUpgrade("alpha");
        ps.AddUpgrade("alpha"); // idempotent
        Assert.True(ps.HasUpgrade("alpha"));
        Assert.True(ps.HasUpgrade("zeta"));
        Assert.False(ps.HasUpgrade("mu"));
        Assert.Equal(new[] { "alpha", "zeta" }, ps.AppliedUpgrades); // sorted, deduped
    }

    [Fact]
    public void Prerequisite_Can_Be_An_Applied_Upgrade()
    {
        var none = System.Array.Empty<string>();
        var armory = new BuildingSpec(120, 2, 2, 120, 3, CanTrain: true);
        var depot = new BuildingSpec(100, 2, 2, 100, 3, IsDepot: true);
        var faction = new FactionDef("f", "F",
            units: System.Array.Empty<UnitDef>(),
            buildings: new[]
            {
                new BuildingDef("depot", 1, none, depot),
                new BuildingDef("armory", 2, new[] { "permit" }, armory),
            },
            upgrades: new[]
            {
                new UpgradeDef("permit", 1, "depot", none, new[] { "*" }, UpgradeStat.Damage, Fix.Zero, 0, 1),
            });

        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: faction);
        w.Players[0].Minerals = 1000;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(0, worker, "armory", 7, 5) });
        Assert.Empty(w.Buildings); // blocked — needs "permit"
        w.Players[0].AddUpgrade("permit");
        w.Step(new Command[] { new BuildCommand(0, worker, "armory", 7, 5) });
        Assert.Single(w.Buildings); // now allowed
    }
}
