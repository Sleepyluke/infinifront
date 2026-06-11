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
    /// version-guarded); SimWorld._swappedThisTick (transient per-tick scratch set, cleared
    /// at the top of MoveUnits before any state is read — zero net effect on observable state);
    /// SimWorld._occupancy (derived from unit positions — incrementally maintained via claim/release, excluded as a cache);
    /// SimWorld._visible/_explored (derived from unit/building positions+specs every tick — excluded);
    /// SimWorld.FogEnabled (debug-only toggle; never toggled mid-match in any lockstep context).
    /// MapGrid passability IS hashed (mutable mid-run since buildings
    /// and node depletion), packed 64 cells per Mix, along with Map.Version.
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
            h = Mix(h, (ulong)u.SupplyCost);
            h = Mix(h, (ulong)u.HarvestPhase);
            h = Mix(h, (ulong)u.HarvestNodeId);
            h = Mix(h, (ulong)u.CarriedMinerals);
            h = Mix(h, (ulong)u.GatherTicksRemaining);
            h = Mix(h, u.Harvester is null ? 0UL : 1UL);
            if (u.Harvester is { } hv)
            {
                h = Mix(h, (ulong)hv.CarryCapacity);
                h = Mix(h, (ulong)hv.GatherTicks);
            }
            h = Mix(h, (ulong)u.SightRange);
            h = Mix(h, (ulong)u.Stance);
            h = Mix(h, u.HasAnchor ? 1UL : 0UL);
            h = Mix(h, (ulong)u.Anchor.X.Raw);
            h = Mix(h, (ulong)u.Anchor.Y.Raw);
            h = Mix(h, u.IsPatrolling ? 1UL : 0UL);
            h = Mix(h, (ulong)u.PatrolA.X.Raw);
            h = Mix(h, (ulong)u.PatrolA.Y.Raw);
            h = Mix(h, (ulong)u.PatrolB.X.Raw);
            h = Mix(h, (ulong)u.PatrolB.Y.Raw);
        }

        foreach (var p in world.Players)
        {
            h = Mix(h, (ulong)p.Minerals);
            h = Mix(h, (ulong)p.SupplyUsed);
            h = Mix(h, (ulong)p.SupplyCap);
        }

        h = Mix(h, (ulong)world.Buildings.Count);
        foreach (var b in world.Buildings)
        {
            h = Mix(h, (ulong)b.Id);
            h = Mix(h, (ulong)b.OwnerId);
            h = Mix(h, (ulong)b.CellX);
            h = Mix(h, (ulong)b.CellY);
            h = Mix(h, (ulong)b.Hp);
            h = Mix(h, b.IsComplete ? 1UL : 0UL);
            h = Mix(h, (ulong)b.BuildProgress);
            h = Mix(h, (ulong)b.Spec.MaxHp);
            h = Mix(h, (ulong)b.Spec.Width);
            h = Mix(h, (ulong)b.Spec.Height);
            h = Mix(h, (ulong)b.Spec.SupplyProvided);
            h = Mix(h, b.Spec.IsDepot ? 1UL : 0UL);
            h = Mix(h, b.Spec.CanTrain ? 1UL : 0UL);
            h = Mix(h, (ulong)b.Queue.Count);
            foreach (var item in b.Queue)
            {
                h = Mix(h, (ulong)item.RemainingTicks);
                h = Mix(h, (ulong)item.Spec.MaxHp);
                h = Mix(h, (ulong)item.Spec.Speed.Raw);
                h = Mix(h, (ulong)item.Spec.SupplyCost);
            }
            h = Mix(h, b.HasRally ? 1UL : 0UL);
            h = Mix(h, (ulong)b.RallyPoint.X.Raw);
            h = Mix(h, (ulong)b.RallyPoint.Y.Raw);
        }

        h = Mix(h, (ulong)world.Nodes.Count);
        foreach (var n in world.Nodes)
        {
            h = Mix(h, (ulong)n.Id);
            h = Mix(h, (ulong)n.CellX);
            h = Mix(h, (ulong)n.CellY);
            h = Mix(h, (ulong)n.Remaining);
        }

        // Map passability — mutable mid-run since buildings/nodes; packed 64 cells per Mix.
        h = Mix(h, (ulong)world.Map.Version);
        ulong acc = 0;
        int bits = 0;
        for (int y = 0; y < world.Map.Height; y++)
            for (int x = 0; x < world.Map.Width; x++)
            {
                acc = (acc << 1) | (world.Map.IsPassable(x, y) ? 1UL : 0UL);
                if (++bits == 64) { h = Mix(h, acc); acc = 0; bits = 0; }
            }
        if (bits > 0) h = Mix(h, acc);
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
