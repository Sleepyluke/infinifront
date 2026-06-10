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
}
