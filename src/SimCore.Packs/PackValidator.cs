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

        return findings;
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
}

/// <summary>Tunable coefficients for the point-budget balance heuristic (Task 4).
/// First-pass defaults to be tuned by playtesting.</summary>
public sealed record BudgetWeights
{
    public static readonly BudgetWeights Default = new();
}
