using SimCore.Math;
using Xunit;

public class FixTests
{
    [Fact]
    public void FromInt_RoundTrips() => Assert.Equal(5, Fix.FromInt(5).ToInt());

    [Fact]
    public void Add_And_Sub_Work()
    {
        var a = Fix.FromInt(3);
        var b = Fix.FromInt(2);
        Assert.Equal(5, (a + b).ToInt());
        Assert.Equal(1, (a - b).ToInt());
    }

    [Fact]
    public void Mul_Works_With_Fractions()
    {
        var half = Fix.FromFraction(1, 2);
        Assert.Equal(3, (Fix.FromInt(6) * half).ToInt());
    }

    [Fact]
    public void Div_Works() => Assert.Equal(4, (Fix.FromInt(12) / Fix.FromInt(3)).ToInt());

    [Fact]
    public void Sqrt_Of_PerfectSquare() => Assert.Equal(9, Fix.Sqrt(Fix.FromInt(81)).ToInt());

    [Fact]
    public void Sqrt_Of_Two_Is_Close()
    {
        var r = Fix.Sqrt(Fix.FromInt(2));
        // 1.41421 in Q48.16 ≈ raw 92681; accept ±2 raw units
        Assert.InRange(r.Raw, 92679, 92683);
    }

    [Fact]
    public void Comparisons_Work()
    {
        Assert.True(Fix.FromInt(1) < Fix.FromInt(2));
        Assert.True(Fix.FromInt(2) >= Fix.FromInt(2));
    }
}
