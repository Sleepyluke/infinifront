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
                u.AttackTargetId = 0;

            // Attack-move: acquire a target, or resume/finish the march.
            if (u.IsAttackMoving && u.Weapon is not null && u.AttackTargetId == 0)
            {
                var acquired = AcquireTarget(u, u.Weapon.Range + AcquireBonus);
                if (acquired != 0)
                {
                    u.AttackTargetId = acquired;
                }
                else if (!u.HasMoveOrder || !u.MoveTarget.Equals(u.AttackMoveDest))
                {
                    if (u.Position.Equals(u.AttackMoveDest))
                    {
                        u.IsAttackMoving = false; // arrived
                    }
                    else
                    {
                        // resume march toward original destination
                        var (rdx, rdy) = Map.WorldToCell(u.AttackMoveDest);
                        var f = GetField(rdx, rdy);
                        var (ccx, ccy) = Map.WorldToCell(u.Position);
                        var inDestCell = ccx == rdx && ccy == rdy;
                        if (!inDestCell && f.DirectionAt(ccx, ccy) == (0, 0))
                        {
                            u.IsAttackMoving = false; // destination unreachable — give up
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

            if (u.Weapon is null || u.AttackTargetId == 0) continue;
            if (!TryResolveTarget(u.AttackTargetId, out var targetPos, out var targetUnit, out var targetBuilding))
            {
                u.AttackTargetId = 0;
                continue;
            }

            var delta = targetPos - u.Position;

            // Leash: attack-movers abandon targets that kite beyond Range + LeashBonus
            // (explicit AttackCommand orders have no leash — the player asked for that chase).
            if (u.IsAttackMoving)
            {
                var leash = u.Weapon.Range + LeashBonus;
                if (delta.LengthSquared() > leash * leash) { u.AttackTargetId = 0; continue; }
            }

            if (delta.LengthSquared() <= u.Weapon.Range * u.Weapon.Range)
            {
                u.HasMoveOrder = false;
                u.Path = null;
                if (u.Weapon.CooldownRemaining == 0)
                {
                    if (targetUnit is not null) targetUnit.Hp -= u.Weapon.Damage;
                    else targetBuilding!.Hp -= u.Weapon.Damage;
                    u.Weapon.CooldownRemaining = u.Weapon.CooldownTicks;
                }
            }
            else
            {
                // chase: follow a (cached) field toward the target's current cell.
                // Building targets: the footprint cell is impassable, so the field is empty
                // until Task 7 adds approach seeding — acceptable for stationary in-range attacks.
                var (tx, ty) = Map.WorldToCell(targetPos);
                u.HasMoveOrder = true;
                u.MoveTarget = targetPos;
                u.Path = GetField(tx, ty);
                u.PathVersion = Map.Version;
            }
        }
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
    /// strict less-than keeps the earliest). Units strictly preferred over buildings. Deterministic.</summary>
    private int AcquireTarget(Unit u, Fix acquireRange)
    {
        var rangeSq = acquireRange * acquireRange;
        int best = 0;
        Fix bestDist = default;
        foreach (var e in _units)
        {
            if (e.OwnerId == u.OwnerId || e.Hp <= 0) continue;
            var d = (e.Position - u.Position).LengthSquared();
            if (d > rangeSq) continue;
            if (best == 0 || d < bestDist) { best = e.Id; bestDist = d; }
        }
        if (best != 0) return best;
        foreach (var b in _buildings)
        {
            if (b.OwnerId == u.OwnerId || b.Hp <= 0) continue;
            var d = (CenterOf(b) - u.Position).LengthSquared();
            if (d > rangeSq) continue;
            if (best == 0 || d < bestDist) { best = b.Id; bestDist = d; }
        }
        return best;
    }
}
