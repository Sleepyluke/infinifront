namespace SimCore.Sim;

public sealed partial class SimWorld
{
    /// <summary>The mechanic governing this unit: its OWNER's faction mechanic. Null when none.</summary>
    private MechanicDef? MechanicFor(Unit u) => FactionFor(u.OwnerId)?.Mechanic;

    private bool HasShields(Unit u) => MechanicFor(u) is { Kind: MechanicKind.RegeneratingShields };

    /// <summary>Initial shield pool for a unit spawned under its owner's faction.</summary>
    private int InitialShield(int ownerId) =>
        FactionFor(ownerId)?.Mechanic is { Kind: MechanicKind.RegeneratingShields } m ? m.MaxShield : 0;

    /// <summary>Applies combat damage to a unit, shield-first. Resets the damage timer
    /// so shield regen pauses. Works uniformly: a unit with no shields has ShieldHp 0,
    /// so all damage goes to Hp (identical to direct subtraction).</summary>
    private static void ApplyDamage(Unit target, int amount)
    {
        int toShield = System.Math.Min(target.ShieldHp, amount);
        target.ShieldHp -= toShield;
        target.Hp -= (amount - toShield);
        target.TicksSinceDamaged = 0;
    }

    /// <summary>Lifesteal: the attacker heals RegenPerTick HP each landed hit (capped at MaxHp).
    /// No-op for factions without the Lifesteal mechanic, so it never touches the golden scenario.</summary>
    private void ApplyLifesteal(Unit attacker)
    {
        if (MechanicFor(attacker) is not { Kind: MechanicKind.Lifesteal } m) return;
        if (attacker.Hp <= 0 || attacker.Hp >= attacker.MaxHp) return;
        attacker.Hp = System.Math.Min(attacker.MaxHp, attacker.Hp + m.RegenPerTick);
    }

    /// <summary>Per-tick faction-mechanic update (regenerating shields OR HP regeneration),
    /// keyed per unit off its owner's faction. Units whose owner has no mechanic are skipped
    /// (zero churn). See the TicksSinceDamaged note: ApplyDamage resets to 0 (UpdateCombat,
    /// before this), UpdateShields transitions 0->1 then increments; regen delay accounts for
    /// the +1. Both mechanics share RegenPerTick (per-tick recovery) and RegenDelayTicks
    /// (out-of-combat delay); RegeneratingShields refills ShieldHp toward MaxShield, while
    /// Regeneration heals Hp toward Unit.MaxHp (MaxShield unused).</summary>
    private void UpdateShields()
    {
        foreach (var u in _units)
        {
            if (MechanicFor(u) is not { } m || m.Kind == MechanicKind.None) continue;
            if (u.TicksSinceDamaged == 0) u.TicksSinceDamaged = 1; else u.TicksSinceDamaged++;
            switch (m.Kind)
            {
                case MechanicKind.RegeneratingShields:
                    if (u.TicksSinceDamaged >= m.RegenDelayTicks && u.ShieldHp < m.MaxShield)
                        u.ShieldHp = System.Math.Min(m.MaxShield, u.ShieldHp + m.RegenPerTick);
                    break;
                case MechanicKind.Regeneration:
                    if (u.Hp > 0 && u.TicksSinceDamaged >= m.RegenDelayTicks && u.Hp < u.MaxHp)
                        u.Hp = System.Math.Min(u.MaxHp, u.Hp + m.RegenPerTick);
                    break;
            }
        }
    }
}
