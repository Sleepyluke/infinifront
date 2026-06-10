using SimCore.Math;
using Xunit;

public class FixVecTests
{
    [Fact]
    public void Add_Works()
    {
        var v = new FixVec(Fix.FromInt(1), Fix.FromInt(2)) + new FixVec(Fix.FromInt(3), Fix.FromInt(4));
        Assert.Equal(4, v.X.ToInt());
        Assert.Equal(6, v.Y.ToInt());
    }

    [Fact]
    public void Length_Of_3_4_Is_5()
    {
        var v = new FixVec(Fix.FromInt(3), Fix.FromInt(4));
        Assert.Equal(Fix.FromInt(5), v.Length());
    }

    [Fact]
    public void Normalized_Times_Length_Restores_Vector_Approximately()
    {
        var v = new FixVec(Fix.FromInt(10), Fix.FromInt(0));
        var n = v.Normalized();
        Assert.Equal(Fix.One, n.X);
        Assert.Equal(Fix.Zero, n.Y);
    }

    [Fact]
    public void Normalized_Zero_Is_Zero()
    {
        var n = FixVec.Zero.Normalized();
        Assert.Equal(Fix.Zero, n.X);
        Assert.Equal(Fix.Zero, n.Y);
    }
}
