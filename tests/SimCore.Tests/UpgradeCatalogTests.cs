using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class UpgradeCatalogTests
{
    private static UnitSpec U() => new(40, Fix.FromFraction(1, 2), 50, 1, 20);
    private static BuildingSpec B() => new(100, 2, 2, 100, 10, CanTrain: true);

    [Fact]
    public void Upgrades_Are_Catalogued_And_Looked_Up()
    {
        var faction = new FactionDef("f", "F",
            units: new[] { new UnitDef("trooper", 1, "rax", new string[0], U()) },
            buildings: new[] { new BuildingDef("rax", 1, new string[0], B()) },
            upgrades: new[]
            {
                new UpgradeDef("dmg1", 1, "rax", new string[0], new[] { "trooper" },
                    UpgradeStat.Damage, Fix.FromInt(2), 50, 20),
            });

        var up = faction.GetUpgrade("dmg1")!;
        Assert.Equal("dmg1", up.Id);
        Assert.Equal("rax", up.ResearchedAt);
        Assert.Equal(UpgradeStat.Damage, up.Stat);
        Assert.Equal(Fix.FromInt(2), up.Delta);
        Assert.Single(faction.UpgradeList);
        Assert.Null(faction.GetUpgrade("nope"));
    }

    [Fact]
    public void Faction_Without_Upgrades_Constructor_Still_Works()
    {
        var faction = new FactionDef("f", "F",
            units: new[] { new UnitDef("trooper", 1, "rax", new string[0], U()) },
            buildings: new[] { new BuildingDef("rax", 1, new string[0], B()) });
        Assert.Empty(faction.UpgradeList);
        Assert.Null(faction.GetUpgrade("x"));
    }
}
