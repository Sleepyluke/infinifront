using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class ShieldHashTests
{
    private static SimWorld World()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 5, faction: TestFactions.Standard);
        w.SpawnUnit(0, FixVec.FromInts(1, 1), Fix.FromFraction(1, 2), 50);
        w.SpawnUnit(1, FixVec.FromInts(5, 5), Fix.FromFraction(1, 2), 80);
        return w;
    }

    [Fact]
    public void ShieldHp_Change_Changes_Hash()
    {
        var a = World();
        var b = World();
        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
        b.Units[0].ShieldHp = 7;
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void TicksSinceDamaged_Change_Changes_Hash()
    {
        var a = World();
        var b = World();
        b.Units[0].TicksSinceDamaged = 3;
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }
}
