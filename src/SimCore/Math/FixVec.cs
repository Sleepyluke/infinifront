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
    public Fix Length() => Fix.Sqrt(LengthSquared());

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
