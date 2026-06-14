using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class UpgradeValidateTests
{
    private static UnitSpec U() => new(40, Fix.FromFraction(1, 2), 50, 1, 20);
    private static BuildingSpec B() => new(100, 2, 2, 100, 10, CanTrain: true);
    private static UpgradeDef Up(string id, string at, string[] req, string[] targets) =>
        new(id, 1, at, req, targets, UpgradeStat.Damage, Fix.FromInt(1), 50, 20);

    private static FactionDef Faction(UpgradeDef[] ups) => new("f", "F",
        units: new[] { new UnitDef("trooper", 1, "rax", new string[0], U()) },
        buildings: new[] { new BuildingDef("rax", 1, new string[0], B()) },
        upgrades: ups);

    [Fact]
    public void Good_Upgrade_Validates_Empty()
        => Assert.Empty(Faction(new[] { Up("dmg1", "rax", new string[0], new[] { "trooper" }) }).Validate());

    [Fact]
    public void Star_Target_Is_Valid()
        => Assert.Empty(Faction(new[] { Up("dmg1", "rax", new string[0], new[] { "*" }) }).Validate());

    [Fact]
    public void Dangling_ResearchedAt_Is_Flagged()
        => Assert.Contains(Faction(new[] { Up("dmg1", "ghost", new string[0], new[] { "*" }) }).Validate(),
            e => e.Contains("dmg1") && e.Contains("ghost"));

    [Fact]
    public void Dangling_Target_Unit_Is_Flagged()
        => Assert.Contains(Faction(new[] { Up("dmg1", "rax", new string[0], new[] { "ghostunit" }) }).Validate(),
            e => e.Contains("dmg1") && e.Contains("ghostunit"));

    [Fact]
    public void Dangling_Requires_Resolves_Building_Or_Upgrade()
    {
        var f = Faction(new[]
        {
            Up("dmg1", "rax", new string[0], new[] { "*" }),
            Up("dmg2", "rax", new[] { "dmg1" }, new[] { "*" }),
        });
        Assert.Empty(f.Validate());
        var bad = Faction(new[] { Up("dmg2", "rax", new[] { "ghost" }, new[] { "*" }) });
        Assert.Contains(bad.Validate(), e => e.Contains("dmg2") && e.Contains("ghost"));
    }

    [Fact]
    public void Upgrade_Prerequisite_Cycle_Is_Flagged()
    {
        var f = Faction(new[]
        {
            Up("a", "rax", new[] { "b" }, new[] { "*" }),
            Up("b", "rax", new[] { "a" }, new[] { "*" }),
        });
        Assert.Contains(f.Validate(), e => e.Contains("cycle"));
    }
}
