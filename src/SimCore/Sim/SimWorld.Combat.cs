using SimCore.Math;

namespace SimCore.Sim;

public sealed partial class SimWorld
{
    // Hysteresis band: acquisition at Range + AcquireBonus, leash drop at Range + LeashBonus.
    // Invariant: LeashBonus > AcquireBonus, else attack-movers thrash at the boundary.
    private static readonly Fix AcquireBonus = Fix.FromInt(2);
    private static readonly Fix LeashBonus = Fix.FromInt(4);

    /// <summary>Runs after commands, before movement. Damage exchange is symmetric within a
    /// tick: a unit brought to 0 Hp earlier in the pass still fires (attacker Hp is not
    /// checked), so mutual kills are possible and spawn order grants no damage advantage.
    /// Spawn order only affects overkill suppression: once a target is at 0 Hp, later
    /// attackers see target.Hp <= 0, clear their order, and skip the wasted hit. All
    /// iteration is over _units (stable order) — deterministic.</summary>
    private void UpdateCombat()
    {
        // Pass 1: cooldowns tick down for everyone.
        foreach (var u in _units)
            if (u.Weapon is { CooldownRemaining: > 0 } w) w.CooldownRemaining--;

        // Pass 2: fight or chase.
        foreach (var u in _units)
        {
            // Clear dead/missing targets up front so re-acquisition happens this tick, not next.
            if (u.AttackTargetId != 0 && !TryResolveTarget(u.AttackTargetId, out _, out _, out _))
            {
                u.AttackTargetId = 0;
                Disengage(u); // no-ops when !HasAnchor
            }

            // Attack-move: acquire a target, or resume/finish the march.
            if (u.IsAttackMoving && u.Weapon is not null && u.AttackTargetId == 0)
            {
                var acquired = AcquireTarget(u, EffectiveRange(u) + AcquireBonus);
                if (acquired != 0)
                {
                    u.AttackTargetId = acquired;
                }
                else if (!u.HasMoveOrder || !u.MoveTarget.Equals(u.AttackMoveDest))
                {
                    var (rdx, rdy) = Map.WorldToCell(u.AttackMoveDest);
                    var (ccx, ccy) = Map.WorldToCell(u.Position);
                    // "Arrived" for patrol swap: exact position match OR move order was cleared
                    // (arrival-relaxation from a body-blocked endpoint) while within Chebyshev 2
                    // of the destination cell.
                    // Bound is 2, not 1: ShouldRelaxArrival Rule B can clear the move order when
                    // the unit is one step behind a stationary blocker that is itself Chebyshev 1
                    // of the target — putting the patroller at Chebyshev 2 of the target cell.
                    // Using ≤ 1 here caused a livelock: UpdateCombat re-issued the march every
                    // tick and MoveUnits immediately re-cleared it (Rule B fired again), forever.
                    bool exactArrival = u.Position.Equals(u.AttackMoveDest);
                    bool relaxedArrival = !u.HasMoveOrder &&
                        System.Math.Abs(ccx - rdx) <= 2 && System.Math.Abs(ccy - rdy) <= 2;
                    bool arrivedAtDest = exactArrival || relaxedArrival;

                    if (arrivedAtDest && u.IsPatrolling)
                    {
                        // Patrol leg swap: flip A↔B, march to new B, keep IsAttackMoving.
                        (u.PatrolA, u.PatrolB) = (u.PatrolB, u.PatrolA);
                        u.AttackMoveDest = u.PatrolB;
                        var (ndx, ndy) = Map.WorldToCell(u.PatrolB);
                        var nf = GetField(ndx, ndy);
                        u.HasMoveOrder = true;
                        u.MoveTarget = u.PatrolB;
                        u.Path = nf;
                        u.PathVersion = Map.Version;
                        // IsAttackMoving stays true
                    }
                    else if (arrivedAtDest)
                    {
                        u.IsAttackMoving = false; // arrived at non-patrol attack-move dest
                    }
                    else
                    {
                        // resume march toward original destination
                        var f = GetField(rdx, rdy);
                        var inDestCell = ccx == rdx && ccy == rdy;
                        if (!inDestCell && f.DirectionAt(ccx, ccy) == (0, 0))
                        {
                            if (u.IsPatrolling)
                            {
                                // Destination unreachable — still swap legs so the patroller
                                // doesn't stall forever against an impassable endpoint.
                                (u.PatrolA, u.PatrolB) = (u.PatrolB, u.PatrolA);
                                u.AttackMoveDest = u.PatrolB;
                                var (ndx2, ndy2) = Map.WorldToCell(u.PatrolB);
                                u.HasMoveOrder = true;
                                u.MoveTarget = u.PatrolB;
                                u.Path = GetField(ndx2, ndy2);
                                u.PathVersion = Map.Version;
                            }
                            else
                            {
                                u.IsAttackMoving = false; // destination unreachable — give up
                            }
                        }
                        else
                        {
                            u.HasMoveOrder = true;
                            u.MoveTarget = u.AttackMoveDest;
                            u.Path = f;
                            u.PathVersion = Map.Version;
                        }
                    }
                }
            }

            // Idle stance acquisition: units with no orders engage per stance.
            if (!u.IsAttackMoving && !u.HasMoveOrder && u.HarvestPhase == HarvestPhase.None
                && u.Weapon is not null && u.AttackTargetId == 0 && u.Stance != Stance.Passive)
            {
                var acquired = AcquireTarget(u, EffectiveRange(u) + AcquireBonus);
                if (acquired != 0)
                {
                    u.AttackTargetId = acquired;
                    u.Anchor = u.Position;
                    u.HasAnchor = true;
                }
            }

            if (u.Weapon is null || u.AttackTargetId == 0) continue;
            if (!TryResolveTarget(u.AttackTargetId, out var targetPos, out var targetUnit, out var targetBuilding))
            {
                u.AttackTargetId = 0;
                Disengage(u); // no-ops when !HasAnchor
                continue;
            }

            // Fog chase-drop: if the target has moved into a cell no longer visible to the
            // attacker's owner, lose track of it (applies to both attack-move and explicit orders).
            if (FogEnabled)
            {
                var (tcx, tcy) = targetUnit is not null
                    ? Map.WorldToCell(targetUnit.Position)
                    : Map.WorldToCell(CenterOf(targetBuilding!));
                if (!IsVisibleTo(u.OwnerId, tcx, tcy))
                {
                    u.AttackTargetId = 0;
                    Disengage(u); // no-ops when !HasAnchor
                    continue;
                }
            }

            var delta = targetPos - u.Position;

            // Leash: attack-movers abandon targets that kite beyond Range + LeashBonus
            // (explicit AttackCommand orders have no leash — the player asked for that chase).
            if (u.IsAttackMoving)
            {
                var leash = EffectiveRange(u) + LeashBonus;
                if (delta.LengthSquared() > leash * leash) { u.AttackTargetId = 0; continue; }
            }

            // Anchored leash: idle-acquired targets are abandoned when they kite beyond
            // anchor + Range + LeashBonus (measured from anchor, not current position).
            if (u.HasAnchor)
            {
                var leash = EffectiveRange(u) + LeashBonus;
                if ((targetPos - u.Anchor).LengthSquared() > leash * leash)
                {
                    u.AttackTargetId = 0;
                    Disengage(u);
                    continue;
                }
            }

            var effRange = EffectiveRange(u);
            if (delta.LengthSquared() <= effRange * effRange)
            {
                u.HasMoveOrder = false;
                u.Path = null;
                if (u.Weapon.CooldownRemaining == 0)
                {
                    if (targetUnit is not null) ApplyDamage(targetUnit, EffectiveDamage(u));
                    else targetBuilding!.Hp -= EffectiveDamage(u);
                    ApplyLifesteal(u);
                    u.Weapon.CooldownRemaining = EffectiveCooldownTicks(u);
                }
            }
            else
            {
                // chase: follow a (cached) flow field toward the target's current cell.
                // Building targets: the footprint is impassable, so the field is seeded
                // from the nearest passable neighbours of the building's footprint (approach
                // seeding, implemented in plan 2b) — the unit chases to the footprint edge.
                var (tx, ty) = Map.WorldToCell(targetPos);
                u.HasMoveOrder = true;
                u.MoveTarget = targetPos;
                u.Path = GetField(tx, ty);
                u.PathVersion = Map.Version;
            }
        }
    }

    /// <summary>Ends an anchored stance engagement. Defend walks home; AutoAttack
    /// stays put. Anchor cleared either way (Defend clears on arrival via the
    /// normal arrival logic — the move order is an ordinary move).</summary>
    private void Disengage(Unit u)
    {
        if (!u.HasAnchor) return;
        if (u.Stance == Stance.Defend && !u.Position.Equals(u.Anchor))
        {
            var (ax, ay) = Map.WorldToCell(u.Anchor);
            u.HasMoveOrder = true;
            u.MoveTarget = u.Anchor;
            u.Path = GetField(ax, ay);
            u.PathVersion = Map.Version;
        }
        else
        {
            // AutoAttack or Defend at-anchor: stop immediately, clearing residual chase order.
            u.HasMoveOrder = false;
            u.Path = null;
        }
        u.HasAnchor = false;
    }

    /// <summary>Resolves a target id to (position, alive) for unit or building targets.
    /// Building "position" is the footprint center — a v1 approximation for range checks.</summary>
    private bool TryResolveTarget(int targetId, out FixVec position, out Unit? unit, out Building? building)
    {
        unit = GetUnit(targetId);
        if (unit is not null && unit.Hp > 0) { position = unit.Position; building = null; return true; }
        building = GetBuilding(targetId);
        if (building is not null && building.Hp > 0) { position = CenterOf(building); unit = null; return true; }
        position = default;
        unit = null;
        building = null;
        return false;
    }

    /// <summary>Nearest living enemy within range; ties broken by spawn order (stable list iteration,
    /// strict less-than keeps the earliest). Units strictly preferred over buildings. Deterministic.
    /// When FogEnabled, skips enemies in cells not visible to the acquiring unit's owner.</summary>
    private int AcquireTarget(Unit u, Fix acquireRange) => AcquireTargetAt(u.Position, u.OwnerId, acquireRange);

    /// <summary>Nearest living enemy (units preferred, then buildings) within range of `from`,
    /// owned by a different TEAM than `ownerId`, visible to that owner. Deterministic (stable
    /// list iteration, strict-less-than tie-break). Shared by unit and building acquisition.</summary>
    private int AcquireTargetAt(FixVec from, int ownerId, Fix acquireRange)
    {
        var rangeSq = acquireRange * acquireRange;
        int best = 0;
        Fix bestDist = default;
        foreach (var e in _units)
        {
            if (SameTeam(e.OwnerId, ownerId) || e.Hp <= 0) continue;
            var (ecx, ecy) = Map.WorldToCell(e.Position);
            if (!IsVisibleTo(ownerId, ecx, ecy)) continue;
            var d = (e.Position - from).LengthSquared();
            if (d > rangeSq) continue;
            if (best == 0 || d < bestDist) { best = e.Id; bestDist = d; }
        }
        if (best != 0) return best;
        foreach (var b in _buildings)
        {
            if (SameTeam(b.OwnerId, ownerId) || b.Hp <= 0) continue;
            var bc = Map.WorldToCell(CenterOf(b));
            if (!IsVisibleTo(ownerId, bc.Item1, bc.Item2)) continue;
            var d = (CenterOf(b) - from).LengthSquared();
            if (d > rangeSq) continue;
            if (best == 0 || d < bestDist) { best = b.Id; bestDist = d; }
        }
        return best;
    }

    /// <summary>Static building defense: each complete, alive, WEAPONED building fires at the
    /// nearest enemy in range on cooldown. No-op for weaponless buildings (all but towers), so the
    /// towerless golden scenario is byte-identical. Deterministic (stable _buildings order).</summary>
    private void UpdateBuildingCombat()
    {
        foreach (var b in _buildings)
        {
            if (b.Weapon is not { } w || !b.IsComplete || b.Hp <= 0) continue;
            if (w.CooldownRemaining > 0) { w.CooldownRemaining--; continue; }
            int targetId = AcquireTargetAt(CenterOf(b), b.OwnerId, w.Range);
            if (targetId == 0) continue;
            if (!TryResolveTarget(targetId, out _, out var tu, out var tb)) continue;
            if (tu is not null) ApplyDamage(tu, w.Damage);
            else tb!.Hp -= w.Damage;
            w.CooldownRemaining = w.CooldownTicks;
        }
    }
}
