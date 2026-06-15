# Faction Pack Validation & Authoring Implementation Plan (3d-2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an author-facing `PackValidator` (playability + tier + point-budget checks → structured findings) and the Faction Forge authoring prompt, finishing the faction-pack arc.

**Architecture:** A new `PackValidator` in `SimCore.Packs` takes a loaded `FactionDef` and returns `ValidationFinding`s (Error/Warning + stable machine code + target id + message). It is a SEPARATE pass from `FactionDef.Validate()` (which stays the structural seed in `SimCore`) and may use `double` freely — it never feeds the deterministic sim, so `SimCore` stays untouched and the golden hash is safe. A `FactionPackLoader.LoadAndValidate` convenience bundles load + findings. A text doc, `docs/faction-forge-prompt.md`, is the LLM authoring prompt.

**Tech Stack:** C# / .NET 8, xUnit. No new dependencies.

**Source spec:** `docs/superpowers/specs/2026-06-14-pack-validation-authoring-design.md`

---

## Conventions for every task

- Run from repo root `C:\Users\lssha\llm-rts`. If `dotnet` missing: bash `export PATH="$PATH:/c/Program Files/dotnet"`.
- Run tests: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
- **Baseline:** 265 SimCore tests pass at the start of this plan.
- After each commit, confirm `git log --oneline -1` shows your commit.
- End commit messages with `Co-Authored-By: RuFlo <ruv@ruv.net>`.

## Engine facts the validator relies on (verified against source)

- `FactionDef` (namespace `SimCore.Sim`) exposes `Id`, `Name`, `UnitList`, `BuildingList`, `UpgradeList`, `Mechanic`, and `GetUnit/GetBuilding/GetUpgrade(string)`.
- `UnitDef(string Id, int Tier, string ProducedBy, IReadOnlyList<string> Requires, UnitSpec Spec)`. `UnitSpec(int MaxHp, Fix Speed, int MineralCost, int SupplyCost, int BuildTimeTicks, WeaponSpec? Weapon, HarvesterSpec? Harvester, int SightRange)`. `WeaponSpec(int Damage, Fix Range, int CooldownTicks)`. `HarvesterSpec(int CarryCapacity, int GatherTicks)`.
- `BuildingDef(string Id, int Tier, IReadOnlyList<string> Requires, BuildingSpec Spec)`. `BuildingSpec(int MaxHp, int Width, int Height, int MineralCost, int BuildTimeTicks, int SupplyProvided, bool IsDepot, bool CanTrain, int SightRange)`.
- `UpgradeDef(string Id, int Tier, string ResearchedAt, IReadOnlyList<string> Requires, IReadOnlyList<string> TargetUnitDefIds, UpgradeStat Stat, Fix Delta, int MineralCost, int ResearchTicks)`.
- `MechanicDef(MechanicKind Kind, int MaxShield, int RegenPerTick, int RegenDelayTicks)`; `enum MechanicKind { None=0, RegeneratingShields=1 }`.
- Building `Requires` are buildings only (FactionDef.Validate enforces this). Unit/upgrade `Requires` may be buildings OR upgrades. An upgrade's `ResearchedAt` is a building.
- `Fix` (namespace `SimCore.Math`) has public `long Raw`; convert to double for the (non-deterministic, advisory) budget math via `f.Raw / 65536.0`.
- The validator is called on a successfully-loaded def, but guards null/blank ids and skips null requirement entries defensively (a test constructs defs in code).

## File Structure (created by this plan)

- `src/SimCore.Packs/ValidationFinding.cs` — `Severity` enum + `ValidationFinding` record.
- `src/SimCore.Packs/PackValidator.cs` — the validator (+ `BudgetWeights`).
- `src/SimCore.Packs/FactionPackLoader.cs` — MODIFY: add `PackReport` + `LoadAndValidate`.
- `docs/faction-forge-prompt.md` — the authoring prompt.
- `tests/SimCore.Tests/Packs/PackValidatorTests.cs` — validator tests.
- `tests/SimCore.Tests/Packs/FactionPackLoaderTests.cs` — MODIFY: `LoadAndValidate` tests.

---

## Task 1: Finding types + structural minimums + blank ids

**Files:**
- Create: `src/SimCore.Packs/ValidationFinding.cs`
- Create: `src/SimCore.Packs/PackValidator.cs`
- Test: `tests/SimCore.Tests/Packs/PackValidatorTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/SimCore.Tests/Packs/PackValidatorTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using SimCore.Math;
using SimCore.Packs;
using SimCore.Sim;

namespace SimCore.Tests.Packs;

public class PackValidatorTests
{
    private static readonly string[] None = System.Array.Empty<string>();

    private static UnitDef Unit(string id, string producedBy, IReadOnlyList<string>? requires = null,
        int tier = 1, UnitSpec? spec = null) =>
        new(id, tier, producedBy, requires ?? None, spec ?? new UnitSpec(50, Fix.One, 50, 1, 50));

    private static BuildingDef Bld(string id, IReadOnlyList<string>? requires = null, int tier = 1) =>
        new(id, tier, requires ?? None, new BuildingSpec(100, 2, 2, 100, 100));

    private static bool Has(IReadOnlyList<ValidationFinding> fs, string code, string? target = null) =>
        fs.Any(f => f.Code == code && (target == null || f.TargetId == target));

    [Fact]
    public void Reference_faction_has_no_errors()
    {
        var findings = PackValidator.Validate(ReferenceFaction.Def);
        Assert.DoesNotContain(findings, f => f.Severity == Severity.Error);
    }

    [Fact]
    public void No_buildings_is_an_error()
    {
        var f = new FactionDef("x", "X", new[] { Unit("u", "ghost") }, System.Array.Empty<BuildingDef>());
        Assert.True(Has(PackValidator.Validate(f), "NO_BUILDINGS"));
    }

    [Fact]
    public void No_units_is_an_error()
    {
        var f = new FactionDef("x", "X", System.Array.Empty<UnitDef>(), new[] { Bld("hq") });
        Assert.True(Has(PackValidator.Validate(f), "NO_UNITS"));
    }

    [Fact]
    public void Blank_unit_id_is_an_error()
    {
        var f = new FactionDef("x", "X", new[] { Unit("  ", "hq") }, new[] { Bld("hq") });
        Assert.True(Has(PackValidator.Validate(f), "ID_BLANK"));
    }
}
```

- [ ] **Step 2: Run, expect failure** — `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`. Compile FAILS (`Severity`/`ValidationFinding`/`PackValidator` missing).

- [ ] **Step 3: Create the finding types**

Create `src/SimCore.Packs/ValidationFinding.cs`:

```csharp
namespace SimCore.Packs;

/// <summary>Error = pack is broken/unplayable (an importer should refuse to start a
/// match). Warning = unusual but legal (surfaced to the author, never blocks).</summary>
public enum Severity { Error, Warning }

/// <summary>One author-facing validator result. <see cref="Code"/> is a STABLE
/// machine-readable identifier (the fix-it contract); <see cref="TargetId"/> is the
/// offending unit/building/upgrade id (null for faction-level findings).</summary>
public sealed record ValidationFinding(Severity Severity, string Code, string? TargetId, string Message);
```

- [ ] **Step 4: Create the validator with structural + id checks**

Create `src/SimCore.Packs/PackValidator.cs`:

```csharp
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
```

(`BudgetWeights` is referenced in the signature but only USED in Task 4. To keep this task compiling, also create the minimal `BudgetWeights` placeholder now — it gets its fields in Task 4. Add to the bottom of `PackValidator.cs`:)

```csharp
/// <summary>Tunable coefficients for the point-budget balance heuristic (Task 4).
/// First-pass defaults to be tuned by playtesting.</summary>
public sealed record BudgetWeights
{
    public static readonly BudgetWeights Default = new();
}
```

- [ ] **Step 5: Run, expect pass** (265 + 4 = 269).

- [ ] **Step 6: Commit**

```bash
git add src/SimCore.Packs/ValidationFinding.cs src/SimCore.Packs/PackValidator.cs tests/SimCore.Tests/Packs/PackValidatorTests.cs
git commit -m "feat(packs): PackValidator scaffold — structural minimums + blank-id findings

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 2: Reachability — unbuildable units/upgrades + no-seed-unit

**Files:**
- Modify: `src/SimCore.Packs/PackValidator.cs`
- Test: `tests/SimCore.Tests/Packs/PackValidatorTests.cs` (add)

- [ ] **Step 1: Write the failing tests**

Add to `PackValidatorTests.cs`:

```csharp
    [Fact]
    public void Unit_with_unreachable_producer_is_flagged()
    {
        // hq is reachable; locked requires missing-building so tank's producer chain is dead.
        var f = new FactionDef("x", "X",
            new[] { Unit("worker", "hq"), Unit("tank", "factory") },
            new[] { Bld("hq"), Bld("factory", new[] { "ghost" }) }); // factory needs a building that doesn't exist
        // NOTE: FactionDef.Validate would flag the dangling 'ghost' ref, but PackValidator
        // is about reachability: factory is unreachable, so tank is unbuildable.
        var findings = PackValidator.Validate(f);
        Assert.True(Has(findings, "PRODUCER_UNREACHABLE", "tank"));
        Assert.False(Has(findings, "PRODUCER_UNREACHABLE", "worker"));
    }

    [Fact]
    public void No_buildable_unit_is_an_error()
    {
        // The only unit is produced by an unreachable building => nothing can be built at start.
        var f = new FactionDef("x", "X",
            new[] { Unit("tank", "factory") },
            new[] { Bld("hq"), Bld("factory", new[] { "ghost" }) });
        Assert.True(Has(PackValidator.Validate(f), "NO_SEED_UNIT"));
    }

    [Fact]
    public void Unreachable_upgrade_is_flagged()
    {
        var up = new UpgradeDef("dmg", 1, "lab", None, new[] { "worker" },
            UpgradeStat.Damage, Fix.FromInt(1), 50, 50);
        var f = new FactionDef("x", "X",
            new[] { Unit("worker", "hq") },
            new[] { Bld("hq"), Bld("lab", new[] { "ghost" }) },
            new[] { up });
        Assert.True(Has(PackValidator.Validate(f), "UPGRADE_UNREACHABLE", "dmg"));
    }

    [Fact]
    public void Reachable_chain_is_not_flagged()
    {
        // hq -> factory(requires hq) -> tank(producedBy factory). All reachable.
        var f = new FactionDef("x", "X",
            new[] { Unit("worker", "hq"), Unit("tank", "factory", new[] { "factory" }) },
            new[] { Bld("hq"), Bld("factory", new[] { "hq" }) });
        var findings = PackValidator.Validate(f);
        Assert.False(Has(findings, "PRODUCER_UNREACHABLE"));
        Assert.False(Has(findings, "NO_SEED_UNIT"));
    }
```

- [ ] **Step 2: Run, expect failure** (the new tests fail — codes not emitted yet).

- [ ] **Step 3: Add reachability + the three findings**

In `PackValidator.cs`, add these private helpers (above or below `Validate`):

```csharp
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

    private static bool IsBuildable(UnitDef u, HashSet<string> reachB, HashSet<string> reachU)
    {
        if (!reachB.Contains(u.ProducedBy)) return false;
        foreach (var req in u.Requires)
            if (!reachB.Contains(req) && !reachU.Contains(req)) return false;
        return true;
    }
```

Then, inside `Validate`, AFTER the blank-id loops and BEFORE `return findings;`, add:

```csharp
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
            string why = !reachB.Contains(u.ProducedBy)
                ? $"its producer '{u.ProducedBy}' is unreachable"
                : "one of its prerequisites is unreachable";
            findings.Add(new(Severity.Error, "PRODUCER_UNREACHABLE", u.Id,
                $"unit '{u.Id}' can never be built ({why})"));
        }

        if (faction.UnitList.Count > 0 && buildable == 0)
            findings.Add(new(Severity.Error, "NO_SEED_UNIT", null,
                "no unit can be built from the starting tech (faction is unplayable)"));
```

- [ ] **Step 4: Run, expect pass** (269 + 4 = 273). Confirm `Reference_faction_has_no_errors` STILL passes (the reference faction's units are all buildable: fabber from depot, trooper/outrider from barracks, tank from barracks requiring depot — all reachable).

- [ ] **Step 5: Commit**

```bash
git add src/SimCore.Packs/PackValidator.cs tests/SimCore.Tests/Packs/PackValidatorTests.cs
git commit -m "feat(packs): validator reachability — unbuildable unit/upgrade + no-seed-unit errors

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 3: Tier monotonicity (warning)

**Files:**
- Modify: `src/SimCore.Packs/PackValidator.cs`
- Test: `tests/SimCore.Tests/Packs/PackValidatorTests.cs` (add)

- [ ] **Step 1: Write the failing tests**

Add to `PackValidatorTests.cs`:

```csharp
    [Fact]
    public void Tier1_unit_requiring_tier3_building_warns()
    {
        var f = new FactionDef("x", "X",
            new[] { Unit("grunt", "hq", new[] { "citadel" }, tier: 1) },
            new[] { Bld("hq"), Bld("citadel", new[] { "hq" }, tier: 3) });
        var findings = PackValidator.Validate(f);
        Assert.True(Has(findings, "TIER_NONMONOTONIC", "grunt"));
        Assert.Equal(Severity.Warning, findings.First(x => x.Code == "TIER_NONMONOTONIC").Severity);
    }

    [Fact]
    public void Reference_faction_has_no_tier_warnings()
    {
        var findings = PackValidator.Validate(ReferenceFaction.Def);
        Assert.False(Has(findings, "TIER_NONMONOTONIC"));
    }
```

- [ ] **Step 2: Run, expect failure** (tier test fails — code not emitted).

- [ ] **Step 3: Add the tier check**

In `PackValidator.cs`, add a helper:

```csharp
    private static int? TierOf(FactionDef f, string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var b = f.GetBuilding(id);
        if (b is not null) return b.Tier;
        var u = f.GetUpgrade(id);
        if (u is not null) return u.Tier;
        return null; // unknown id: referential checks elsewhere handle it
    }
```

Then inside `Validate`, before `return findings;`, add:

```csharp
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
```

(`CheckTier` skips unknown/blank prereqs via `TierOf` returning null. Reference faction: tank is tier 2 and requires depot (tier 1) and is produced by barracks (tier 1) — all ≤ 2, so no warning.)

- [ ] **Step 4: Run, expect pass** (273 + 2 = 275). Confirm `Reference_faction_has_no_errors` and `Reference_faction_has_no_tier_warnings` both pass.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore.Packs/PackValidator.cs tests/SimCore.Tests/Packs/PackValidatorTests.cs
git commit -m "feat(packs): validator tier-monotonicity warning

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 4: Point-budget balance (warnings) + weights

**Files:**
- Modify: `src/SimCore.Packs/PackValidator.cs` (flesh out `BudgetWeights`, add the budget pass)
- Test: `tests/SimCore.Tests/Packs/PackValidatorTests.cs` (add)

- [ ] **Step 1: Write the failing tests**

Add to `PackValidatorTests.cs`:

```csharp
    [Fact]
    public void Reference_faction_is_within_budget_band()
    {
        // The default weights are calibrated so the reference faction has no budget warnings.
        var findings = PackValidator.Validate(ReferenceFaction.Def);
        Assert.False(Has(findings, "BUDGET_OVERPOWERED"));
        Assert.False(Has(findings, "BUDGET_UNDERPOWERED"));
    }

    [Fact]
    public void Grossly_efficient_unit_is_flagged_overpowered()
    {
        // 3 normal units (power 100 / cost 100 => eff 1.0) + 1 god unit (power 300 / cost 100 => eff 3.0).
        // mean = 1.5, band [0.9, 2.1]; only the god unit exceeds it.
        UnitSpec spec(int hp, int cost) => new(hp, Fix.Zero, cost, 0, 50); // no weapon/speed/harvester
        var f = new FactionDef("x", "X",
            new[]
            {
                Unit("a", "hq", spec: spec(100, 100)),
                Unit("b", "hq", spec: spec(100, 100)),
                Unit("c", "hq", spec: spec(100, 100)),
                Unit("god", "hq", spec: spec(300, 100)),
            },
            new[] { Bld("hq") });
        var findings = PackValidator.Validate(f);
        Assert.True(Has(findings, "BUDGET_OVERPOWERED", "god"));
        Assert.False(Has(findings, "BUDGET_OVERPOWERED", "a"));
    }

    [Fact]
    public void Single_unit_faction_skips_budget_check()
    {
        var f = new FactionDef("x", "X", new[] { Unit("solo", "hq") }, new[] { Bld("hq") });
        var findings = PackValidator.Validate(f);
        Assert.False(Has(findings, "BUDGET_OVERPOWERED"));
        Assert.False(Has(findings, "BUDGET_UNDERPOWERED"));
    }
```

- [ ] **Step 2: Run, expect failure** (budget codes not emitted).

- [ ] **Step 3: Flesh out `BudgetWeights`**

Replace the placeholder `BudgetWeights` record at the bottom of `PackValidator.cs` with:

```csharp
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
```

- [ ] **Step 4: Add the budget pass**

In `PackValidator.cs`, add a helper:

```csharp
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
```

Then inside `Validate`, before `return findings;`, add:

```csharp
        // Point-budget balance: per-unit efficiency outliers vs the faction's own mean.
        var w = weights ?? BudgetWeights.Default;
        var units = faction.UnitList;
        if (units.Count >= 2)
        {
            var eff = new double[units.Count];
            double sum = 0;
            for (int i = 0; i < units.Count; i++)
            {
                double power = UnitPower(units[i], faction, w);
                double cost = units[i].Spec.MineralCost + w.Supply * units[i].Spec.SupplyCost;
                eff[i] = power / System.Math.Max(1.0, cost);
                sum += eff[i];
            }
            double mean = sum / units.Count;
            for (int i = 0; i < units.Count; i++)
            {
                if (eff[i] > mean * (1 + w.Tolerance))
                    findings.Add(new(Severity.Warning, "BUDGET_OVERPOWERED", units[i].Id,
                        $"unit '{units[i].Id}' efficiency {eff[i]:0.00} is well above the faction mean {mean:0.00}"));
                else if (eff[i] < mean * (1 - w.Tolerance))
                    findings.Add(new(Severity.Warning, "BUDGET_UNDERPOWERED", units[i].Id,
                        $"unit '{units[i].Id}' efficiency {eff[i]:0.00} is well below the faction mean {mean:0.00}"));
            }
        }
```

- [ ] **Step 5: Run, expect pass** (275 + 3 = 278). The `Reference_faction_is_within_budget_band` test is the calibration gate. **If it fails** (the reference faction produces a budget warning), the weights need a small nudge, not a redesign — the expected efficiencies are Fabber 1.33 / Trooper 1.88 / Outrider 1.35 / Tank 1.21, mean 1.44, band [0.87, 2.02]; recompute with the actual code and adjust a weight so all four fit. Do NOT loosen `Tolerance` past 0.50.

- [ ] **Step 6: Commit**

```bash
git add src/SimCore.Packs/PackValidator.cs tests/SimCore.Tests/Packs/PackValidatorTests.cs
git commit -m "feat(packs): point-budget balance warnings (per-unit efficiency outliers)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 5: `LoadAndValidate` convenience

**Files:**
- Modify: `src/SimCore.Packs/FactionPackLoader.cs`
- Test: `tests/SimCore.Tests/Packs/FactionPackLoaderTests.cs` (add)

- [ ] **Step 1: Write the failing tests**

Add to `FactionPackLoaderTests.cs`:

```csharp
    [Fact]
    public void LoadAndValidate_reference_pack_is_clean()
    {
        string json = FactionPackLoader.ToJson(ReferenceFaction.Def);
        var report = FactionPackLoader.LoadAndValidate(json);
        Assert.NotNull(report.Faction);
        Assert.Empty(report.LoadErrors);
        Assert.DoesNotContain(report.Findings, x => x.Severity == Severity.Error);
    }

    [Fact]
    public void LoadAndValidate_hard_failure_has_no_findings()
    {
        var report = FactionPackLoader.LoadAndValidate("{ not valid json ");
        Assert.Null(report.Faction);
        Assert.NotEmpty(report.LoadErrors);
        Assert.Empty(report.Findings);
    }
```

- [ ] **Step 2: Run, expect failure** (`LoadAndValidate`/`PackReport` missing).

- [ ] **Step 3: Add `PackReport` + `LoadAndValidate`**

In `FactionPackLoader.cs`, add (alongside `PackLoadResult`):

```csharp
/// <summary>Load + full author-facing validation in one call. LoadErrors are the
/// structural/parse errors from LoadFromJson; Findings are the PackValidator report
/// (empty when the load hard-failed, i.e. Faction is null).</summary>
public sealed record PackReport(
    FactionDef? Faction,
    IReadOnlyList<string> LoadErrors,
    IReadOnlyList<ValidationFinding> Findings);
```

And add this method to `FactionPackLoader`:

```csharp
    public static PackReport LoadAndValidate(string json, BudgetWeights? weights = null)
    {
        var result = LoadFromJson(json);
        var findings = result.Faction is null
            ? (IReadOnlyList<ValidationFinding>)System.Array.Empty<ValidationFinding>()
            : PackValidator.Validate(result.Faction, weights);
        return new PackReport(result.Faction, result.Errors, findings);
    }
```

- [ ] **Step 4: Run, expect pass** (278 + 2 = 280).

- [ ] **Step 5: Commit**

```bash
git add src/SimCore.Packs/FactionPackLoader.cs tests/SimCore.Tests/Packs/FactionPackLoaderTests.cs
git commit -m "feat(packs): LoadAndValidate convenience (load + validator findings)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 6: Faction Forge authoring prompt

**Files:**
- Create: `docs/faction-forge-prompt.md`

This is a documentation-only task (no code, no tests). Write the prompt a player pastes into an external LLM to author a faction pack.

- [ ] **Step 1: Read the worked example**

Read `packs/reference/faction.json` (the committed reference pack from 3d-1) so the prompt's example matches the real format exactly.

- [ ] **Step 2: Write the prompt doc**

Create `docs/faction-forge-prompt.md` with these sections (write real, complete prose — no placeholders):

1. **Title + how to use:** "Faction Forge — author an RTS faction as a JSON pack." One paragraph: paste this whole doc into an LLM chat, describe the faction you want, and it will output a `faction.json` you save and import.
2. **Your role (for the LLM):** "You are a faction designer. Output exactly one JSON object — a faction pack — and nothing else."
3. **The format, by annotated example:** include the actual reference `faction.json` contents in a fenced block, then a field-by-field reference table covering: top-level `id`/`name`; each `units[]` field (`id` unique, `tier` int ≥1, `producedBy` = a building id, `requires` = list of building/upgrade ids, `maxHp`, `speed` decimal in tiles/tick, `mineralCost`, `supplyCost`, `buildTimeTicks`, `sightRange`, optional `weapon`{damage,range,cooldownTicks}, optional `harvester`{carryCapacity,gatherTicks}); each `buildings[]` field (`id`, `tier`, `requires`, `maxHp`, `width`, `height`, `mineralCost`, `buildTimeTicks`, optional `supplyProvided`, `isDepot`, `canTrain`, `sightRange`); `upgrades[]` (`id`, `tier`, `researchedAt` building, `requires`, `targetUnitDefIds` (unit ids or `"*"` for all), `stat` ∈ {Damage, Range, CooldownTicks, Speed, Sight}, `delta` decimal, `mineralCost`, `researchTicks`); optional `mechanic` ({`kind`: "RegeneratingShields", `maxShield`, `regenPerTick`, `regenDelayTicks`}).
4. **Conventions:** numbers are fixed-point decimals (e.g. `0.25`, `4`); omitted optional fields take engine defaults (sightRange 7 units / 8 buildings, supplyProvided 0, isDepot/canTrain false); enums are written by NAME (e.g. `"Damage"`, not a number); ids are lowercase, unique, no spaces.
5. **The rules your pack must follow (the validator checks these):**
   - Every unit's `producedBy` must be a building that's reachable from the start (its `requires` chain bottoms out at a no-requirement building); at least one unit must be buildable at game start.
   - A prerequisite should be the same tier or lower than the thing requiring it (else a tier warning).
   - Ids unique and non-blank.
   - Keep units roughly balanced: a unit's power should track its cost. As a rough guide, power ≈ `maxHp + 8·(damage·10/cooldownTicks) + 4·range + 30·speed + 2·sight + 40·(harvester?1:0) + 1.5·shield`, and cost ≈ `mineralCost + 25·supplyCost`; keep each unit's power/cost within ~±40% of the others. (This is advisory — outliers warn, they don't block.)
   - If using `RegeneratingShields`, set sensible `maxShield`/`regenPerTick`/`regenDelayTicks`.
6. **Identity guidance:** asymmetry and gaps are GOOD — no air, all-melee, a one-unit-type rush faction are all fine; the validator only warns, never rejects, on stylistic choices. Lean into a theme.
7. **Output contract:** "Output ONLY the JSON object, no prose, no markdown fences, so it can be saved directly as `faction.json`." Then include a small complete worked example: a minimal 1-building / 2-unit faction JSON that would pass validation.

- [ ] **Step 3: Commit**

```bash
git add docs/faction-forge-prompt.md
git commit -m "docs: Faction Forge authoring prompt (LLM pack-generation guide)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 7: Full-suite + determinism gate; finalize

**Files:** none (verification) — optionally MODIFY this plan doc to append an execution note.

- [ ] **Step 1: Confirm SimCore untouched**

```bash
git diff --stat master -- src/SimCore/
```
Expected: empty. (All 3d-2 code is in `SimCore.Packs` + docs/tests.)

- [ ] **Step 2: Full solution, Release**

Run: `dotnet test --configuration Release --nologo -v q`
Expected: PASS — SimCore.Tests = 265 + 15 (≈) new = ~280, SpriteSlicer.Tests 6, 0 failures. Determinism tests pass; golden constant unchanged at `5141900307592480923UL`.

- [ ] **Step 3: Full solution, Debug**

Run: `dotnet test --configuration Debug --nologo -v q`
Expected: PASS (Debug == Release).

- [ ] **Step 4: Sanity-run the validator on the reference pack**

Confirm `PackValidatorTests.Reference_faction_has_no_errors`, `..._no_tier_warnings`, and `..._is_within_budget_band` all pass (the reference faction is fully clean — the arc's dogfood guarantee).

- [ ] **Step 5: Commit (if the plan doc was annotated; otherwise skip)**

```bash
git add docs/superpowers/plans/2026-06-14-pack-validation-authoring.md
git commit -m "docs: 3d-2 execution outcome note

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Self-Review (author checklist — completed)

- **Spec coverage:** finding types + structural minimums + blank ids (Task 1); reachability → unbuildable unit/upgrade + no-seed-unit (Task 2); tier monotonicity (Task 3); point-budget balance + weights (Task 4); LoadAndValidate (Task 5); Faction Forge prompt (Task 6); determinism gate + finalize (Task 7). Every spec "In scope" item maps to a task.
- **Placeholders:** none — every code/test step has complete code; the one forward reference (`BudgetWeights` in Task 1's signature) is satisfied by the Task-1 placeholder record, fleshed out in Task 4.
- **Type consistency:** `Severity`/`ValidationFinding`/`PackValidator.Validate`/`BudgetWeights`/`PackReport`/`LoadAndValidate` used identically across tasks. Field accesses (`u.Spec.Weapon.CooldownTicks`, `wp.Range.Raw`, `faction.Mechanic`, `m.MaxShield`) verified against the engine record signatures listed above. Budget calibration numbers hand-verified against `ReferenceSpecs`.

---

## Execution Outcome (3d-2, completed 2026-06-14)

All 7 tasks implemented via subagent-driven development (foreground implementers +
two-stage spec/quality review per task). Final state on branch
`feat/pack-validation`: **285 SimCore + 6 SpriteSlicer tests, 0 failures, Debug ==
Release**. `SimCore` is byte-for-byte untouched (empty `git diff master --
src/SimCore/`) — the validator lives entirely in `SimCore.Packs` and uses `double`
math freely without touching the deterministic core, so the golden trajectory hash
is unchanged at `5141900307592480923UL` and all determinism/replay tests pass.

Delivered: `PackValidator.Validate(FactionDef, BudgetWeights?)` →
`IReadOnlyList<ValidationFinding>` (Severity + stable Code + TargetId + Message);
structural-minimums + blank-id + reachability (PRODUCER/UPGRADE_UNREACHABLE,
NO_SEED_UNIT) Errors; TIER_NONMONOTONIC Warning; point-budget per-unit efficiency
outliers (BUDGET_OVER/UNDERPOWERED Warnings) with tunable `BudgetWeights` and
free-unit (cost 0) exclusion; `FactionPackLoader.LoadAndValidate` → `PackReport`;
and `docs/faction-forge-prompt.md`, the LLM authoring prompt (both its worked
examples were proven validator-clean by running them through the real loader).

Quality reviews caught and we fixed: the upgrade-fixpoint test-coverage gap (now
covered), the zero-cost free-unit false-flag (now excluded), and the missing
UNDERPOWERED test path. The reference faction is fully clean (no errors, no
tier/budget warnings) — the arc's dogfood guarantee.

**The faction-pack arc (3a tiers → 3b upgrades → 3c mechanics → 3d-1 serialization
→ 3d-2 validation & authoring) is complete.** Next roadmap item (outside this arc):
plan 5 — CPU opponent + match flow.
