namespace SimCore;

/// <summary>xorshift64* — deterministic across platforms. Never use System.Random in sim code.</summary>
public sealed class DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(ulong seed) => _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    /// <summary>Internal state, exposed for state hashing (desync detection). Never use for logic.</summary>
    public ulong State => _state;

    public uint NextUInt()
    {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return (uint)((_state * 0x2545F4914F6CDD1DUL) >> 32);
    }

    /// <summary>Returns value in [minInclusive, maxExclusive). Throws on empty/inverted range.</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new System.ArgumentOutOfRangeException(nameof(maxExclusive),
                $"Range [{minInclusive}, {maxExclusive}) is empty or inverted.");
        return minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
    }
}
