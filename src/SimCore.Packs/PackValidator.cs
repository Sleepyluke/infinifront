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

        return findings;
    }
}

/// <summary>Tunable coefficients for the point-budget balance heuristic (Task 4).
/// First-pass defaults to be tuned by playtesting.</summary>
public sealed record BudgetWeights
{
    public static readonly BudgetWeights Default = new();
}
