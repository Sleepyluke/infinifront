using SimCore;
using Xunit;

public class DeterministicRandomTests
{
    [Fact]
    public void Same_Seed_Same_Sequence()
    {
        var a = new DeterministicRandom(42);
        var b = new DeterministicRandom(42);
        for (int i = 0; i < 100; i++)
            Assert.Equal(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void Different_Seeds_Differ()
    {
        var a = new DeterministicRandom(1);
        var b = new DeterministicRandom(2);
        Assert.NotEqual(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void NextInt_Respects_Bounds()
    {
        var r = new DeterministicRandom(7);
        for (int i = 0; i < 1000; i++)
            Assert.InRange(r.NextInt(5, 10), 5, 9); // max exclusive
    }
}
