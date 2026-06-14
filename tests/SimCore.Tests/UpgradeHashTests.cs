using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class UpgradeHashTests
{
    private static SimWorld World()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 5, faction: TestFactions.Standard);
        w.SpawnUnit(0, FixVec.FromInts(1, 1), Fix.FromFraction(1, 2), 50);
        w.SpawnUnit(1, FixVec.FromInts(5, 5), Fix.FromFraction(1, 2), 80);
        return w;
    }

    [Fact]
    public void Applied_Upgrade_Changes_Hash()
    {
        var a = World();
        var b = World();
        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
        b.Players[0].AddUpgrade("dmg1");
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Research_Slot_Changes_Hash()
    {
        var a = World();
        var b = World();
        a.PlaceBuilding(0, TestFactions.BarracksSpec, 8, 8, "barracks");
        b.PlaceBuilding(0, TestFactions.BarracksSpec, 8, 8, "barracks");
        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
        b.Buildings[^1].ResearchingId = "dmg1";
        b.Buildings[^1].ResearchTicksRemaining = 7;
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }
}
