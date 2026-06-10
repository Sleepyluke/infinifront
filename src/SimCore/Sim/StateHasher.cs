namespace SimCore.Sim;

public static class StateHasher
{
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    public static ulong Hash(SimWorld world)
    {
        var h = FnvOffset;
        h = Mix(h, (ulong)world.Tick);
        foreach (var u in world.Units) // List order is stable → deterministic
        {
            h = Mix(h, (ulong)u.Id);
            h = Mix(h, (ulong)u.OwnerId);
            h = Mix(h, (ulong)u.Position.X.Raw);
            h = Mix(h, (ulong)u.Position.Y.Raw);
            h = Mix(h, (ulong)u.Hp);
        }
        return h;
    }

    private static ulong Mix(ulong h, ulong value)
    {
        for (int i = 0; i < 8; i++)
        {
            h ^= (value >> (i * 8)) & 0xFF;
            h *= FnvPrime;
        }
        return h;
    }
}
