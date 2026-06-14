using SimCore.Math;

namespace SimCore.Sim;

public sealed partial class SimWorld
{
    /// <summary>Sum of applicable upgrade deltas for a unit's owner on a given stat.
    /// Applicable = upgrade.Stat matches AND (target list contains the unit's DefId or "*").
    /// Iterates the player's sorted AppliedUpgrades (deterministic). Fast-paths empty sets.</summary>
    private Fix UpgradeDelta(Unit u, UpgradeStat stat)
    {
        var faction = Faction;
        if (faction is null) return Fix.Zero;
        var applied = _players[u.OwnerId].AppliedUpgrades;
        if (applied.Count == 0) return Fix.Zero;
        var sum = Fix.Zero;
        foreach (var id in applied)
        {
            var up = faction.GetUpgrade(id);
            if (up is null || up.Stat != stat) continue;
            if (Targets(up, u.DefId)) sum += up.Delta;
        }
        return sum;
    }

    private static bool Targets(UpgradeDef up, string unitDefId)
    {
        foreach (var t in up.TargetUnitDefIds)
            if (t == "*" || t == unitDefId) return true;
        return false;
    }

    public int EffectiveDamage(Unit u) =>
        u.Weapon is null ? 0 : System.Math.Max(0, u.Weapon.Damage + UpgradeDelta(u, UpgradeStat.Damage).ToInt());

    public Fix EffectiveRange(Unit u)
    {
        if (u.Weapon is null) return Fix.Zero;
        var r = u.Weapon.Range + UpgradeDelta(u, UpgradeStat.Range);
        return r.Raw < 0 ? Fix.Zero : r;
    }

    public int EffectiveCooldownTicks(Unit u) =>
        u.Weapon is null ? 1 : System.Math.Max(1, u.Weapon.CooldownTicks + UpgradeDelta(u, UpgradeStat.CooldownTicks).ToInt());

    public Fix EffectiveSpeed(Unit u)
    {
        var s = u.SpeedPerTick + UpgradeDelta(u, UpgradeStat.Speed);
        return s.Raw < 0 ? Fix.Zero : s;
    }

    public int EffectiveSight(Unit u) =>
        System.Math.Max(0, u.SightRange + UpgradeDelta(u, UpgradeStat.Sight).ToInt());
}
