namespace SimCore.Math;

/// <summary>Q48.16 fixed-point number. The only numeric type allowed in sim logic.</summary>
public readonly struct Fix : System.IComparable<Fix>, System.IEquatable<Fix>
{
    public const int FractionalBits = 16;
    public readonly long Raw;

    public static readonly Fix Zero = new(0);
    public static readonly Fix One = new(1L << FractionalBits);

    public Fix(long raw) => Raw = raw;

    public static Fix FromInt(int v) => new((long)v << FractionalBits);
    public static Fix FromFraction(int numerator, int denominator) =>
        new(((long)numerator << FractionalBits) / denominator);

    public int ToInt() => (int)(Raw >> FractionalBits); // floor

    public static Fix operator +(Fix a, Fix b) => new(a.Raw + b.Raw);
    public static Fix operator -(Fix a, Fix b) => new(a.Raw - b.Raw);
    public static Fix operator -(Fix a) => new(-a.Raw);
    public static Fix operator *(Fix a, Fix b) =>
        new((long)(((System.Int128)a.Raw * b.Raw) >> FractionalBits));
    public static Fix operator /(Fix a, Fix b) =>
        new((long)(((System.Int128)a.Raw << FractionalBits) / b.Raw));

    public static bool operator <(Fix a, Fix b) => a.Raw < b.Raw;
    public static bool operator >(Fix a, Fix b) => a.Raw > b.Raw;
    public static bool operator <=(Fix a, Fix b) => a.Raw <= b.Raw;
    public static bool operator >=(Fix a, Fix b) => a.Raw >= b.Raw;
    public static bool operator ==(Fix a, Fix b) => a.Raw == b.Raw;
    public static bool operator !=(Fix a, Fix b) => a.Raw != b.Raw;

    /// <summary>Integer Newton's method on the raw value; deterministic.</summary>
    public static Fix Sqrt(Fix v)
    {
        if (v.Raw <= 0) return Zero;
        // sqrt(raw * 2^16) gives the Q48.16 root of the Q48.16 input
        var n = (System.UInt128)(ulong)v.Raw << FractionalBits;
        System.UInt128 x = n, y = (x + 1) / 2;
        while (y < x) { x = y; y = (x + n / x) / 2; }
        return new Fix((long)(ulong)x);
    }

    public int CompareTo(Fix other) => Raw.CompareTo(other.Raw);
    public bool Equals(Fix other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fix f && Equals(f);
    public override int GetHashCode() => Raw.GetHashCode();
    public override string ToString() => ((double)Raw / (1 << FractionalBits)).ToString("0.####");
}
