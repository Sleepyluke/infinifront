using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class FactionDefValidateTests
{
    private static UnitSpec U() => new(40, Fix.FromFraction(1, 2), 50, 1, 20);
    private static BuildingSpec B() => new(100, 2, 2, 100, 10, CanTrain: true);

    [Fact]
    public void Good_Faction_Validates_Empty()
    {
        var f = new FactionDef("f", "F",
            units: new[] { new UnitDef("trooper", 1, "rax", new string[0], U()) },
            buildings: new[] { new BuildingDef("rax", 1, new string[0], B()) });
        Assert.Empty(f.Validate());
    }

    [Fact]
    public void Dangling_ProducedBy_Is_Flagged()
    {
        var f = new FactionDef("f", "F",
            units: new[] { new UnitDef("trooper", 1, "ghost", new string[0], U()) },
            buildings: new[] { new BuildingDef("rax", 1, new string[0], B()) });
        Assert.Contains(f.Validate(), e => e.Contains("trooper") && e.Contains("ghost"));
    }

    [Fact]
    public void Producerless_Unit_Is_Flagged()
    {
        var f = new FactionDef("f", "F",
            units: new[] { new UnitDef("trooper", 1, "", new string[0], U()) },
            buildings: new[] { new BuildingDef("rax", 1, new string[0], B()) });
        Assert.Contains(f.Validate(), e => e.Contains("trooper") && e.Contains("producer"));
    }

    [Fact]
    public void Dangling_Requires_Is_Flagged()
    {
        var f = new FactionDef("f", "F",
            units: new[] { new UnitDef("tank", 2, "rax", new[] { "ghostlab" }, U()) },
            buildings: new[] { new BuildingDef("rax", 1, new string[0], B()) });
        Assert.Contains(f.Validate(), e => e.Contains("tank") && e.Contains("ghostlab"));
    }

    [Fact]
    public void Unit_Requiring_Upgrade_Validates_Empty()
    {
        // A unit may require a researched upgrade (the runtime Has() gate accepts
        // building OR upgrade); Validate must not flag it as an unknown building.
        var f = new FactionDef("f", "F",
            units: new[] { new UnitDef("elite", 2, "rax", new[] { "combat-training" }, U()) },
            buildings: new[] { new BuildingDef("rax", 1, new string[0], B()) },
            upgrades: new[]
            {
                new UpgradeDef("combat-training", 1, "rax", new string[0], new[] { "*" },
                    UpgradeStat.Damage, Fix.FromInt(1), 50, 50),
            });
        Assert.Empty(f.Validate());
    }

    [Fact]
    public void Building_Requiring_Upgrade_Validates_Empty()
    {
        var f = new FactionDef("f", "F",
            units: new[] { new UnitDef("trooper", 1, "rax", new string[0], U()) },
            buildings: new[]
            {
                new BuildingDef("rax", 1, new string[0], B()),
                new BuildingDef("citadel", 2, new[] { "adv-construction" }, B()),
            },
            upgrades: new[]
            {
                new UpgradeDef("adv-construction", 1, "rax", new string[0], new[] { "*" },
                    UpgradeStat.Damage, Fix.FromInt(1), 50, 50),
            });
        Assert.Empty(f.Validate());
    }

    [Fact]
    public void Building_Dangling_Requires_Is_Flagged()
    {
        // A building requirement that is neither a building nor an upgrade is still an error.
        var f = new FactionDef("f", "F",
            units: new[] { new UnitDef("trooper", 1, "rax", new string[0], U()) },
            buildings: new[]
            {
                new BuildingDef("rax", 1, new string[0], B()),
                new BuildingDef("citadel", 2, new[] { "ghost" }, B()),
            });
        Assert.Contains(f.Validate(), e => e.Contains("citadel") && e.Contains("ghost"));
    }

    [Fact]
    public void Building_Prerequisite_Cycle_Is_Flagged()
    {
        var f = new FactionDef("f", "F",
            units: new UnitDef[0],
            buildings: new[]
            {
                new BuildingDef("a", 1, new[] { "b" }, B()),
                new BuildingDef("b", 1, new[] { "a" }, B()),
            });
        Assert.Contains(f.Validate(), e => e.Contains("cycle"));
    }
}
