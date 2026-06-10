using SimCore.Math;

namespace SimCore.Sim;

public sealed partial class SimWorld
{
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
            // Attack-move: acquire a target, or resume/finish the march.
            if (u.IsAttackMoving && u.Weapon is not null && u.AttackTargetId == 0)
            {
                var acquired = AcquireTarget(u, u.Weapon.Range + Fix.FromInt(2));
                if (acquired != 0)
                {
                    u.AttackTargetId = acquired;
                }
                else if (!u.HasMoveOrder)
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
            var target = GetUnit(u.AttackTargetId);
            if (target is null || target.Hp <= 0) { u.AttackTargetId = 0; continue; }

            var delta = target.Position - u.Position;
            if (delta.LengthSquared() <= u.Weapon.Range * u.Weapon.Range)
            {
                u.HasMoveOrder = false;
                u.Path = null;
                if (u.Weapon.CooldownRemaining == 0)
                {
                    target.Hp -= u.Weapon.Damage;
                    u.Weapon.CooldownRemaining = u.Weapon.CooldownTicks;
                }
            }
            else
            {
                // chase: follow a (cached) field toward the target's current cell
                var (tx, ty) = Map.WorldToCell(target.Position);
                u.HasMoveOrder = true;
                u.MoveTarget = target.Position;
                u.Path = GetField(tx, ty);
                u.PathVersion = Map.Version;
            }
        }
    }

    /// <summary>Nearest living enemy within range; ties broken by spawn order (stable list iteration,
    /// strict less-than keeps the earliest). Deterministic.</summary>
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
        return best;
    }
}
