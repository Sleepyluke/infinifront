namespace SimCore.Math;

public readonly struct FixVec : System.IEquatable<FixVec>
{
    public readonly Fix X;
    public readonly Fix Y;

    public static readonly FixVec Zero = new(Fix.Zero, Fix.Zero);

    public FixVec(Fix x, Fix y) { X = x; Y = y; }
    public static FixVec FromInts(int x, int y) => new(Fix.FromInt(x), Fix.FromInt(y));

    public static FixVec operator +(FixVec a, FixVec b) => new(a.X + b.X, a.Y + b.Y);
    public static FixVec operator -(FixVec a, FixVec b) => new(a.X - b.X, a.Y - b.Y);
    public static FixVec operator *(FixVec a, Fix s) => new(a.X * s, a.Y * s);

    public Fix LengthSquared() => X * X + Y * Y;

    /// <summary>Exact length via 128-bit sum of squared raws — no fractional truncation.</summary>
    public Fix Length()
    {
        var sum = (System.Int128)X.Raw * X.Raw + (System.Int128)Y.Raw * Y.Raw;
        return new Fix(Fix.IntegerSqrt((System.UInt128)sum));
    }

    public FixVec Normalized()
    {
        var len = Length();
        return len == Fix.Zero ? Zero : new FixVec(X / len, Y / len);
    }

    public bool Equals(FixVec other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is FixVec v && Equals(v);
    public override int GetHashCode() => System.HashCode.Combine(X, Y);
    public override string ToString() => $"({X}, {Y})";
}
