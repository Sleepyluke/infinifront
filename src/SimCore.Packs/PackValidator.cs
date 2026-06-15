using System.Collections.Generic;
using SimCore.Sim;

namespace SimCore.Packs;

/// <summary>Author-facing pack quality report over a loaded FactionDef. SEPARATE from
/// FactionDef.Validate() (the structural/referential seed in SimCore): these are the
/// richer playability/tier/balance checks. Advisory only — never feeds the sim, so it
/// may use double math and lives in SimCore.Packs to keep SimCore deterministic.</summary>
public static class PackValidator
{
    public static IReadOnlyList<ValidationFinding> Validate(FactionDef faction, BudgetWeights? weights = null)
    {
        var findings = new List<ValidationFinding>();

        // Structural minimums.
        if (faction.BuildingList.Count == 0)
            findings.Add(new(Severity.Error, "NO_BUILDINGS", null, "faction has no buildings"));
        if (faction.UnitList.Count == 0)
            findings.Add(new(Severity.Error, "NO_UNITS", null, "faction has no units"));

        // Blank ids (defensive: a code-built def or a stripped pack could have them).
        foreach (var u in faction.UnitList)
            if (string.IsNullOrWhiteSpace(u.Id))
                findings.Add(new(Severity.Error, "ID_BLANK", u.Id, "a unit has a blank id"));
        foreach (var b in faction.BuildingList)
            if (string.IsNullOrWhiteSpace(b.Id))
                findings.Add(new(Severity.Error, "ID_BLANK", b.Id, "a building has a blank id"));
        foreach (var g in faction.UpgradeList)
            if (string.IsNullOrWhiteSpace(g.Id))
                findings.Add(new(Severity.Error, "ID_BLANK", g.Id, "an upgrade has a blank id"));

        // Reachability / playability.
        var reachB = ReachableBuildings(faction);
        var reachU = ReachableUpgrades(faction, reachB);

        foreach (var up in faction.UpgradeList)
            if (!reachU.Contains(up.Id))
                findings.Add(new(Severity.Error, "UPGRADE_UNREACHABLE", up.Id,
                    $"upgrade '{up.Id}' can never be researched (its building or prerequisites are unreachable)"));

        int buildable = 0;
        foreach (var u in faction.UnitList)
        {
            if (IsBuildable(u, reachB, reachU)) { buildable++; continue; }
            string why;
            if (!reachB.Contains(u.ProducedBy))
            {
                why = $"its producer '{u.ProducedBy}' is unreachable";
            }
            else
            {
                string? badReq = null;
                foreach (var req in u.Requires)
                    if (!reachB.Contains(req) && !reachU.Contains(req)) { badReq = req; break; }
                why = $"its prerequisite '{badReq}' is unreachable";
            }
            findings.Add(new(Severity.Error, "PRODUCER_UNREACHABLE", u.Id,
                $"unit '{u.Id}' can never be built ({why})"));
        }

        if (faction.UnitList.Count > 0 && buildable == 0)
            findings.Add(new(Severity.Error, "NO_SEED_UNIT", null,
                "no unit can be built from the starting tech (faction is unplayable)"));

        // Tier monotonicity (a prerequisite at a HIGHER tier is almost always a slip).
        void CheckTier(string dependentId, int dependentTier, string prereqId)
        {
            var pt = TierOf(faction, prereqId);
            if (pt is int t && t > dependentTier)
                findings.Add(new(Severity.Warning, "TIER_NONMONOTONIC", dependentId,
                    $"'{dependentId}' (tier {dependentTier}) requires '{prereqId}' (tier {t}), a higher tier"));
        }

        foreach (var u in faction.UnitList)
        {
            CheckTier(u.Id, u.Tier, u.ProducedBy);
            foreach (var req in u.Requires) CheckTier(u.Id, u.Tier, req);
        }
        foreach (var b in faction.BuildingList)
            foreach (var req in b.Requires) CheckTier(b.Id, b.Tier, req);
        foreach (var g in faction.UpgradeList)
        {
            CheckTier(g.Id, g.Tier, g.ResearchedAt);
            foreach (var req in g.Requires) CheckTier(g.Id, g.Tier, req);
        }

        // Point-budget balance: per-unit efficiency (power/cost) outliers vs the faction's
        // own mean. Free units (cost <= 0, e.g. a granted starting worker) are EXCLUDED —
        // their balance lives outside the cost economy and including them would skew the mean
        // and false-flag them. Most meaningful with >= 3 cost-bearing units (with exactly two
        // divergent units, both can fall outside the band).
        var w = weights ?? BudgetWeights.Default;
        var priced = new List<(string Id, double Eff)>();
        foreach (var u in faction.UnitList)
        {
            double cost = u.Spec.MineralCost + w.Supply * u.Spec.SupplyCost;
            if (cost <= 0) continue; // free unit: not balance-checked by cost-efficiency
            priced.Add((u.Id, UnitPower(u, faction, w) / cost));
        }
        if (priced.Count >= 2)
        {
            double mean = 0;
            foreach (var p in priced) mean += p.Eff;
            mean /= priced.Count;
            foreach (var p in priced)
            {
                if (p.Eff > mean * (1 + w.Tolerance))
                    findings.Add(new(Severity.Warning, "BUDGET_OVERPOWERED", p.Id,
                        $"unit '{p.Id}' efficiency {p.Eff:0.00} is well above the faction mean {mean:0.00}"));
                else if (p.Eff < mean * (1 - w.Tolerance))
                    findings.Add(new(Severity.Warning, "BUDGET_UNDERPOWERED", p.Id,
                        $"unit '{p.Id}' efficiency {p.Eff:0.00} is well below the faction mean {mean:0.00}"));
            }
        }

        return findings;
    }

    private static int? TierOf(FactionDef f, string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var b = f.GetBuilding(id);
        if (b is not null) return b.Tier;
        var u = f.GetUpgrade(id);
        if (u is not null) return u.Tier;
        return null; // unknown id: referential checks elsewhere handle it
    }

    /// <summary>Buildings constructible from t0: a monotonic least-fixpoint (only ever adds,
    /// over a finite set) so it always terminates; cycles never enter since a node is added
    /// only once all its building prerequisites are already reachable.</summary>
    private static HashSet<string> ReachableBuildings(FactionDef f)
    {
        var reach = new HashSet<string>();
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var b in f.BuildingList)
            {
                if (reach.Contains(b.Id)) continue;
                bool all = true;
                foreach (var req in b.Requires)
                    if (!reach.Contains(req)) { all = false; break; }
                if (all) { reach.Add(b.Id); changed = true; }
            }
        }
        return reach;
    }

    /// <summary>Upgrades researchable from t0: same monotonic least-fixpoint, terminating;
    /// an upgrade enters only when its ResearchedAt building is reachable and every prereq
    /// (building or earlier upgrade) is already reachable, so upgrade-requires-upgrade chains resolve.</summary>
    private static HashSet<string> ReachableUpgrades(FactionDef f, HashSet<string> reachB)
    {
        var reach = new HashSet<string>();
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var up in f.UpgradeList)
            {
                if (reach.Contains(up.Id)) continue;
                if (!reachB.Contains(up.ResearchedAt)) continue;
                bool all = true;
                foreach (var req in up.Requires)
                    if (!reachB.Contains(req) && !reach.Contains(req)) { all = false; break; }
                if (all) { reach.Add(up.Id); changed = true; }
            }
        }
        return reach;
    }

    /// <summary>True iff the unit can ever be produced: its producer building is reachable and
    /// every prerequisite (a building or an upgrade) is reachable in the respective set.</summary>
    private static bool IsBuildable(UnitDef u, HashSet<string> reachB, HashSet<string> reachU)
    {
        if (!reachB.Contains(u.ProducedBy)) return false;
        foreach (var req in u.Requires)
            if (!reachB.Contains(req) && !reachU.Contains(req)) return false;
        return true;
    }

    private static double UnitPower(UnitDef u, FactionDef f, BudgetWeights w)
    {
        double p = w.Hp * u.Spec.MaxHp;
        if (u.Spec.Weapon is { } wp)
        {
            p += w.Dps * (wp.Damage * (w.RefCooldown / System.Math.Max(1, wp.CooldownTicks)));
            p += w.Range * (wp.Range.Raw / 65536.0);
        }
        p += w.Speed * (u.Spec.Speed.Raw / 65536.0);
        p += w.Sight * u.Spec.SightRange;
        if (u.Spec.Harvester is not null) p += w.Harvester;
        if (f.Mechanic is { Kind: MechanicKind.RegeneratingShields } m) p += w.Shield * m.MaxShield;
        return p;
    }
}

/// <summary>Tunable coefficients for the point-budget balance heuristic. First-pass
/// defaults calibrated so the reference faction's four units land within the band
/// (efficiencies ~1.21–1.88, mean ~1.44, all within ±40%). NOT claimed to be true
/// balance — a sane default the author can override; tune by playtesting.</summary>
public sealed record BudgetWeights(
    double Hp = 1.0, double Dps = 8.0, double Range = 4.0, double Speed = 30.0,
    double Sight = 2.0, double Harvester = 40.0, double Shield = 1.5, double Supply = 25.0,
    double RefCooldown = 10.0, double Tolerance = 0.40)
{
    public static readonly BudgetWeights Default = new();
}
