using System.Linq;
using SimCore.Sim;

namespace SimCore.Tests.Packs;

internal static class FactionDefAssert
{
    public static void DeepEqual(FactionDef a, FactionDef b)
    {
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(a.Name, b.Name);
        Assert.Equal(a.Mechanic, b.Mechanic); // record value equality (null == null too)

        Assert.Equal(a.UnitList.Count, b.UnitList.Count);
        foreach (var (x, y) in a.UnitList.Zip(b.UnitList))
        {
            Assert.Equal(x.Id, y.Id);
            Assert.Equal(x.Tier, y.Tier);
            Assert.Equal(x.ProducedBy, y.ProducedBy);
            Assert.Equal(x.Requires, y.Requires);  // xUnit IEnumerable sequence compare
            Assert.Equal(x.Spec, y.Spec);          // UnitSpec record value equality
        }

        Assert.Equal(a.BuildingList.Count, b.BuildingList.Count);
        foreach (var (x, y) in a.BuildingList.Zip(b.BuildingList))
        {
            Assert.Equal(x.Id, y.Id);
            Assert.Equal(x.Tier, y.Tier);
            Assert.Equal(x.Requires, y.Requires);
            Assert.Equal(x.Spec, y.Spec);          // BuildingSpec record value equality
        }

        Assert.Equal(a.UpgradeList.Count, b.UpgradeList.Count);
        foreach (var (x, y) in a.UpgradeList.Zip(b.UpgradeList))
        {
            Assert.Equal(x.Id, y.Id);
            Assert.Equal(x.Tier, y.Tier);
            Assert.Equal(x.ResearchedAt, y.ResearchedAt);
            Assert.Equal(x.Requires, y.Requires);
            Assert.Equal(x.TargetUnitDefIds, y.TargetUnitDefIds);
            Assert.Equal(x.Stat, y.Stat);
            Assert.Equal(x.Delta, y.Delta);        // Fix value equality
            Assert.Equal(x.MineralCost, y.MineralCost);
            Assert.Equal(x.ResearchTicks, y.ResearchTicks);
        }
    }
}
