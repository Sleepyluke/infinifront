namespace SimCore.Sim;

public sealed partial class SimWorld
{
    /// <summary>The mechanic governing this unit. Today: the shared world Faction's mechanic.
    /// Structured as a per-unit accessor so it becomes per-player when packs let each player
    /// pick a faction (plan 5). Returns null when there is no mechanic.</summary>
    private MechanicDef? MechanicFor(Unit u) => Faction?.Mechanic;

    private bool HasShields(Unit u) => MechanicFor(u) is { Kind: MechanicKind.RegeneratingShields };

    /// <summary>Initial shield pool for a unit spawned under the current faction.</summary>
    private int InitialShield() =>
        Faction?.Mechanic is { Kind: MechanicKind.RegeneratingShields } m ? m.MaxShield : 0;

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

    /// <summary>Per-tick faction-mechanic update. Today: regenerating shields.
    /// Future mechanics dispatch here on MechanicKind. Early-returns when the faction has no
    /// shield mechanic, so non-mechanic factions see zero state churn (golden-safe).
    /// TicksSinceDamaged: transitions 0->1 on first step (or post-damage reset), then increments.
    /// When ApplyDamage (UpdateCombat, before UpdateShields) resets to 0, UpdateShields
    /// transitions it back to 1, then it's 1 at tick end (not 0). This is acceptable: the
    /// countdown started fresh that tick. Regen delay must account for this (+1 expected).</summary>
    private void UpdateShields()
    {
        if (Faction?.Mechanic is not { Kind: MechanicKind.RegeneratingShields } m) return;
        foreach (var u in _units)
        {
            if (!HasShields(u)) continue;
            if (u.TicksSinceDamaged == 0)
                u.TicksSinceDamaged = 1;
            else
                u.TicksSinceDamaged++;
            if (u.TicksSinceDamaged >= m.RegenDelayTicks && u.ShieldHp < m.MaxShield)
                u.ShieldHp = System.Math.Min(m.MaxShield, u.ShieldHp + m.RegenPerTick);
        }
    }
}
