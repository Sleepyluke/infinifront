namespace SimCore.Sim;

public static class StateHasher
{
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    /// <summary>CONVENTION: every mutable sim field MUST be folded in here. When you add sim
    /// state, add it to this method and re-pin GoldenTrajectoryHash in the same commit.
    /// Unit.Path and Unit.PathVersion are deliberately excluded: derived caches, recomputed
    /// from hashed state (MoveTarget + map).
    /// Other documented exclusions: SimWorld._fieldCache/_fieldCacheVersion (derived,
    /// version-guarded) and MapGrid passability (immutable during Step in plan 2a —
    /// MUST be hashed once build/destroy commands can mutate it mid-run).
    /// Patterns for new state: nullable objects need a presence marker before their
    /// fields; never hash collections by iterating a Dictionary (order is undefined).</summary>
    public static ulong Hash(SimWorld world)
    {
        var h = FnvOffset;
        h = Mix(h, (ulong)world.Tick);
        h = Mix(h, world.Rng.State);
        h = Mix(h, (ulong)world.NextIdForHashing);
        h = Mix(h, (ulong)world.Units.Count);
        foreach (var u in world.Units) // List order is stable → deterministic
        {
            h = Mix(h, (ulong)u.Id);
            h = Mix(h, (ulong)u.OwnerId);
            h = Mix(h, (ulong)u.Position.X.Raw);
            h = Mix(h, (ulong)u.Position.Y.Raw);
            h = Mix(h, (ulong)u.Hp);
            h = Mix(h, (ulong)u.SpeedPerTick.Raw);
            h = Mix(h, u.HasMoveOrder ? 1UL : 0UL);
            h = Mix(h, (ulong)u.MoveTarget.X.Raw);
            h = Mix(h, (ulong)u.MoveTarget.Y.Raw);
            h = Mix(h, u.IsAttackMoving ? 1UL : 0UL);
            h = Mix(h, (ulong)u.AttackMoveDest.X.Raw);
            h = Mix(h, (ulong)u.AttackMoveDest.Y.Raw);
            h = Mix(h, (ulong)u.AttackTargetId);
            h = Mix(h, u.Weapon is null ? 0UL : 1UL);
            if (u.Weapon is { } w)
            {
                h = Mix(h, (ulong)w.Damage);
                h = Mix(h, (ulong)w.Range.Raw);
                h = Mix(h, (ulong)w.CooldownTicks);
                h = Mix(h, (ulong)w.CooldownRemaining);
            }
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
