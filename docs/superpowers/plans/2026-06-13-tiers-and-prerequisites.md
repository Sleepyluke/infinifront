# Tech Tiers & Prerequisites Implementation Plan (Plan 3a)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce `FactionDef` (the engine's faction source of truth), convert train/build to id-based commands with prerequisite + `produced_by` gating, and migrate the reference faction — with zero new hashed state.

**Architecture:** `FactionDef` is an immutable named catalog of `UnitDef`/`BuildingDef` (each = tier + prerequisites + the existing `UnitSpec`/`BuildingSpec`). `SimWorld` holds one optional `FactionDef` and resolves command def ids against it at command time; prerequisites are validated against already-hashed building completion state, so no new hashed runtime state is added. The first three tasks are pure additions; tasks 5–8 carry the id-based command refactor and its test-fixture churn.

**Tech Stack:** .NET 8, xUnit, existing SimCore. PATH note: if `dotnet` is not found, run `$env:Path += ';C:\Program Files\dotnet'`.

**Spec:** `docs/superpowers/specs/2026-06-13-faction-tiers-prerequisites-design.md`

**Faction-pack arc:** this is plan 3a of 4 (tiers → upgrades → mechanics → pack format).

**Golden-hash protocol:** `Trajectory_Hash_Matches_Golden_Constant` in `tests/SimCore.Tests/DeterminismTests.cs` folds all 500 per-tick hashes into `GoldenTrajectoryHash` (currently `4005804941942785108UL`). This plan targets an **unchanged** golden — it adds no hashed state and preserves sim behavior. Only Task 8 may legitimately touch the constant, and only if the id-based scenario rewrite provably shifts the trajectory; if a golden failure appears in Tasks 4–7 it signals an accidental behavior change to debug, not to re-pin. The two replay tests (`Same_Script_...`, `Replaying_...`) must never fail.

**Current state:** 169 tests passing on `master`. `Commands.cs` has `BuildCommand(PlayerId, WorkerUnitId, BuildingSpec Spec, CellX, CellY)` and `TrainCommand(PlayerId, BuildingId, UnitSpec Spec)` (spec-carrying). `SimWorld.Apply` handles them at `SimWorld.cs:166` (build) and `:176` (train). `ReferenceSpecs.cs` holds the reference faction's specs. Godot `CommandController.cs` issues `BuildCommand`; train is issued from the Godot HUD.

---

### Task 1: FactionDef / UnitDef / BuildingDef data shape

Pure addition — nothing else changes, nothing breaks.

**Files:**
- Create: `src/SimCore/Sim/FactionDef.cs`
- Test: `tests/SimCore.Tests/FactionDefTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/SimCore.Tests/FactionDefTests.cs
using System.Collections.Generic;
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class FactionDefTests
{
    private static UnitSpec Spec() => new(40, Fix.FromFraction(1, 2), 50, 1, 20);
    private static BuildingSpec BSpec() => new(100, 2, 2, 100, 10, CanTrain: true);

    [Fact]
    public void Lookup_By_Id_Returns_Defs_And_Null_For_Unknown()
    {
        var faction = new FactionDef("ref", "Reference",
            units: new[] { new UnitDef("trooper", 1, "barracks", new string[0], Spec()) },
            buildings: new[] { new BuildingDef("barracks", 1, new string[0], BSpec()) });

        Assert.Equal("ref", faction.Id);
        Assert.Equal("Reference", faction.Name);
        Assert.Equal("trooper", faction.GetUnit("trooper")!.Id);
        Assert.Equal(1, faction.GetUnit("trooper")!.Tier);
        Assert.Equal("barracks", faction.GetUnit("trooper")!.ProducedBy);
        Assert.Equal("barracks", faction.GetBuilding("barracks")!.Id);
        Assert.Null(faction.GetUnit("nope"));
        Assert.Null(faction.GetBuilding("nope"));
        Assert.Single(faction.UnitList);
        Assert.Single(faction.BuildingList);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FactionDefTests`
Expected: compilation FAILS — `FactionDef` does not exist.

- [ ] **Step 3: Implement**

```csharp
// src/SimCore/Sim/FactionDef.cs
using System.Collections.Generic;

namespace SimCore.Sim;

/// <summary>Engine source of truth for a faction: a named catalog of unit/building defs.
/// Immutable. Plan 3d deserializes faction-pack JSON into this; today ReferenceFaction is
/// the only instance. Dictionaries are lookup-only; ordered enumeration uses the *List
/// properties (deterministic). Never iterate the dictionaries in sim logic.</summary>
public sealed class FactionDef
{
    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<UnitDef> UnitList { get; }
    public IReadOnlyList<BuildingDef> BuildingList { get; }

    private readonly Dictionary<string, UnitDef> _units = new();
    private readonly Dictionary<string, BuildingDef> _buildings = new();

    public FactionDef(string id, string name, IEnumerable<UnitDef> units, IEnumerable<BuildingDef> buildings)
    {
        Id = id;
        Name = name;
        var ul = new List<UnitDef>();
        foreach (var u in units) { ul.Add(u); _units[u.Id] = u; }
        var bl = new List<BuildingDef>();
        foreach (var b in buildings) { bl.Add(b); _buildings[b.Id] = b; }
        UnitList = ul;
        BuildingList = bl;
    }

    public UnitDef? GetUnit(string id) => _units.TryGetValue(id, out var u) ? u : null;
    public BuildingDef? GetBuilding(string id) => _buildings.TryGetValue(id, out var b) ? b : null;
}

public sealed record UnitDef(
    string Id, int Tier, string ProducedBy, IReadOnlyList<string> Requires, UnitSpec Spec);

public sealed record BuildingDef(
    string Id, int Tier, IReadOnlyList<string> Requires, BuildingSpec Spec);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: 170 passing (169 + 1). Golden unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/FactionDef.cs tests/SimCore.Tests/FactionDefTests.cs
git commit -m "feat: FactionDef/UnitDef/BuildingDef catalog records"
```

---

### Task 2: FactionDef.Validate() — referential integrity + cycle detection

**Files:**
- Modify: `src/SimCore/Sim/FactionDef.cs` (add `Validate`)
- Test: `tests/SimCore.Tests/FactionDefValidateTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SimCore.Tests/FactionDefValidateTests.cs
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class FactionDefValidateTests
{
    private static UnitSpec U() => new(40, Fix.FromFraction(1, 2), 50, 1, 20);
    private static BuildingSpec B() => new(100, 2, 2, 100, 10, CanTrain: true);

    [Fact]
    public void Good_Faction_Validates_Empty()
    {
        var f = new FactionDef("f", "F",
            units: new[] { new UnitDef("trooper", 1, "rax", new string[0], U()) },
            buildings: new[] { new BuildingDef("rax", 1, new string[0], B()) });
        Assert.Empty(f.Validate());
    }

    [Fact]
    public void Dangling_ProducedBy_Is_Flagged()
    {
        var f = new FactionDef("f", "F",
            units: new[] { new UnitDef("trooper", 1, "ghost", new string[0], U()) },
            buildings: new[] { new BuildingDef("rax", 1, new string[0], B()) });
        Assert.Contains(f.Validate(), e => e.Contains("trooper") && e.Contains("ghost"));
    }

    [Fact]
    public void Producerless_Unit_Is_Flagged()
    {
        var f = new FactionDef("f", "F",
            units: new[] { new UnitDef("trooper", 1, "", new string[0], U()) },
            buildings: new[] { new BuildingDef("rax", 1, new string[0], B()) });
        Assert.Contains(f.Validate(), e => e.Contains("trooper") && e.Contains("producer"));
    }

    [Fact]
    public void Dangling_Requires_Is_Flagged()
    {
        var f = new FactionDef("f", "F",
            units: new[] { new UnitDef("tank", 2, "rax", new[] { "ghostlab" }, U()) },
            buildings: new[] { new BuildingDef("rax", 1, new string[0], B()) });
        Assert.Contains(f.Validate(), e => e.Contains("tank") && e.Contains("ghostlab"));
    }

    [Fact]
    public void Building_Prerequisite_Cycle_Is_Flagged()
    {
        var f = new FactionDef("f", "F",
            units: new UnitDef[0],
            buildings: new[]
            {
                new BuildingDef("a", 1, new[] { "b" }, B()),
                new BuildingDef("b", 1, new[] { "a" }, B()),
            });
        Assert.Contains(f.Validate(), e => e.Contains("cycle"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FactionDefValidateTests`
Expected: compilation FAILS — `Validate` does not exist.

- [ ] **Step 3: Implement** — add to `FactionDef`:

```csharp
    /// <summary>Referential integrity + cycle detection. Returns human-readable errors
    /// (empty = valid). Seed of the plan-3d pack validator; budget/structural rules are NOT here.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        foreach (var u in UnitList)
        {
            if (string.IsNullOrEmpty(u.ProducedBy))
                errors.Add($"unit '{u.Id}' has no producer (ProducedBy is empty)");
            else if (GetBuilding(u.ProducedBy) is null)
                errors.Add($"unit '{u.Id}' ProducedBy references unknown building '{u.ProducedBy}'");
            foreach (var req in u.Requires)
                if (GetBuilding(req) is null)
                    errors.Add($"unit '{u.Id}' requires unknown building '{req}'");
        }

        foreach (var b in BuildingList)
            foreach (var req in b.Requires)
                if (GetBuilding(req) is null)
                    errors.Add($"building '{b.Id}' requires unknown building '{req}'");

        // Cycle detection over the building-prerequisite graph (DFS, three-color).
        var state = new Dictionary<string, int>(); // 0=unvisited,1=in-stack,2=done
        bool HasCycle(string id)
        {
            if (state.TryGetValue(id, out var s))
            {
                if (s == 1) return true;
                if (s == 2) return false;
            }
            state[id] = 1;
            var b = GetBuilding(id);
            if (b is not null)
                foreach (var req in b.Requires)
                    if (GetBuilding(req) is not null && HasCycle(req))
                        return true;
            state[id] = 2;
            return false;
        }
        foreach (var b in BuildingList)
            if (state.GetValueOrDefault(b.Id) == 0 && HasCycle(b.Id))
            {
                errors.Add($"building prerequisite cycle detected involving '{b.Id}'");
                break; // one cycle report is enough
            }

        return errors;
    }
```

Note: `Validate` enumerates `UnitList`/`BuildingList` (ordered) — deterministic error ordering, though tests assert membership not order.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: 175 passing (170 + 5). Golden unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/FactionDef.cs tests/SimCore.Tests/FactionDefValidateTests.cs
git commit -m "feat: FactionDef.Validate referential integrity + cycle detection"
```

---

### Task 3: ReferenceFaction — migrate the reference faction to a FactionDef

`ReferenceSpecs.cs` keeps its spec constants (the stat payloads); a new `ReferenceFaction` wraps them in defs with the starter tech tree.

**Files:**
- Create: `src/SimCore/Sim/ReferenceFaction.cs`
- Test: `tests/SimCore.Tests/ReferenceFactionTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/SimCore.Tests/ReferenceFactionTests.cs
using SimCore.Sim;
using Xunit;

public class ReferenceFactionTests
{
    [Fact]
    public void Reference_Faction_Is_Valid()
    {
        Assert.Empty(ReferenceFaction.Def.Validate());
    }

    [Fact]
    public void Reference_Faction_Has_Expected_Tech_Tree()
    {
        var f = ReferenceFaction.Def;
        Assert.Equal("depot", f.GetUnit("fabber")!.ProducedBy);
        Assert.Equal("barracks", f.GetUnit("trooper")!.ProducedBy);
        Assert.Equal("barracks", f.GetUnit("outrider")!.ProducedBy);
        Assert.Equal("barracks", f.GetUnit("tank")!.ProducedBy);
        Assert.Equal(2, f.GetUnit("tank")!.Tier);
        Assert.Contains("depot", f.GetUnit("tank")!.Requires);   // tank gated behind depot
        Assert.Empty(f.GetBuilding("depot")!.Requires);
        Assert.Empty(f.GetBuilding("barracks")!.Requires);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ReferenceFactionTests`
Expected: compilation FAILS — `ReferenceFaction` does not exist.

- [ ] **Step 3: Implement**

```csharp
// src/SimCore/Sim/ReferenceFaction.cs
namespace SimCore.Sim;

/// <summary>The hand-built reference faction as a FactionDef, wrapping ReferenceSpecs' stat
/// payloads with a starter tech tree. Plan 3d will make this the first data-driven pack.</summary>
public static class ReferenceFaction
{
    private static readonly string[] None = System.Array.Empty<string>();

    public static readonly FactionDef Def = new(
        id: "reference",
        name: "Reference",
        units: new[]
        {
            new UnitDef("fabber",   1, "depot",    None,                ReferenceSpecs.Fabber),
            new UnitDef("trooper",  1, "barracks", None,                ReferenceSpecs.Trooper),
            new UnitDef("outrider", 1, "barracks", None,                ReferenceSpecs.Outrider),
            new UnitDef("tank",     2, "barracks", new[] { "depot" },   ReferenceSpecs.Tank),
        },
        buildings: new[]
        {
            new BuildingDef("depot",    1, None, ReferenceSpecs.Depot),
            new BuildingDef("barracks", 1, None, ReferenceSpecs.Barracks),
        });
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: 177 passing (175 + 2). Golden unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/ReferenceFaction.cs tests/SimCore.Tests/ReferenceFactionTests.cs
git commit -m "feat: ReferenceFaction — reference faction as a FactionDef with tech tree"
```

---

### Task 4: SimWorld faction plumbing + TestFactions helper

Add an optional `FactionDef` to `SimWorld` and a shared test faction. No command behavior changes yet — everything still passes.

**Files:**
- Modify: `src/SimCore/Sim/SimWorld.cs` (ctor param, `Faction` property)
- Create: `tests/SimCore.Tests/TestFactions.cs`
- Test: `tests/SimCore.Tests/SimWorldFactionTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/SimCore.Tests/SimWorldFactionTests.cs
using SimCore.Sim;
using Xunit;

public class SimWorldFactionTests
{
    [Fact]
    public void World_Exposes_Its_Faction()
    {
        var w = new SimWorld(new MapGrid(8, 8), seed: 1, faction: TestFactions.Standard);
        Assert.Same(TestFactions.Standard, w.Faction);
        Assert.Equal("depot", w.Faction!.GetBuilding("depot")!.Id);
    }

    [Fact]
    public void Faction_Defaults_Null_For_Legacy_Construction()
    {
        var w = new SimWorld(new MapGrid(8, 8), seed: 1);
        Assert.Null(w.Faction);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter SimWorldFactionTests`
Expected: compilation FAILS — `TestFactions` and `SimWorld.Faction` / the `faction:` param don't exist.

- [ ] **Step 3: Implement.**

Create `tests/SimCore.Tests/TestFactions.cs` — a shared catalog mirroring fixtures used across building/production/economy tests:

```csharp
// tests/SimCore.Tests/TestFactions.cs
using SimCore.Math;
using SimCore.Sim;

/// <summary>Shared FactionDef for tests that exercise id-based build/train commands.
/// Stat values match the per-test fixtures they replace (BuildingTests.Depot etc.).</summary>
public static class TestFactions
{
    private static readonly string[] None = System.Array.Empty<string>();

    // Building specs (match the historical static fixtures).
    public static readonly BuildingSpec DepotSpec =
        new(MaxHp: 100, Width: 2, Height: 2, MineralCost: 100, BuildTimeTicks: 10,
            SupplyProvided: 8, IsDepot: true);
    public static readonly BuildingSpec BarracksSpec =
        new(MaxHp: 150, Width: 2, Height: 2, MineralCost: 150, BuildTimeTicks: 5, CanTrain: true);
    public static readonly BuildingSpec HutSpec =
        new(MaxHp: 30, Width: 2, Height: 2, MineralCost: 50, BuildTimeTicks: 1);

    // Unit specs.
    public static readonly UnitSpec MarineSpec =
        new(MaxHp: 40, Speed: Fix.FromFraction(1, 2), MineralCost: 50, SupplyCost: 1,
            BuildTimeTicks: 8, Weapon: new WeaponSpec(6, Fix.FromInt(2), 5));
    public static readonly UnitSpec TankSpec =
        new(MaxHp: 150, Speed: Fix.FromFraction(1, 8), MineralCost: 150, SupplyCost: 3,
            BuildTimeTicks: 12, Weapon: new WeaponSpec(20, Fix.FromInt(6), 20));

    /// <summary>Standard catalog: depot (no reqs), barracks (no reqs), hut (no reqs),
    /// marine (produced_by barracks), tank (produced_by barracks, requires depot — tier 2).</summary>
    public static readonly FactionDef Standard = new(
        id: "test", name: "Test",
        units: new[]
        {
            new UnitDef("marine", 1, "barracks", None, MarineSpec),
            new UnitDef("tank",   2, "barracks", new[] { "depot" }, TankSpec),
        },
        buildings: new[]
        {
            new BuildingDef("depot",    1, None, DepotSpec),
            new BuildingDef("barracks", 1, None, BarracksSpec),
            new BuildingDef("hut",      1, None, HutSpec),
        });
}
```

In `src/SimCore/Sim/SimWorld.cs`, add the `Faction` property and ctor param. Replace the constructor (lines 22–28):

```csharp
    public FactionDef? Faction { get; }

    public SimWorld(MapGrid map, ulong seed, int playerCount = 2, FactionDef? faction = null)
    {
        Map = map;
        Rng = new DeterministicRandom(seed);
        Faction = faction;
        _players = new PlayerState[playerCount];
        for (int i = 0; i < playerCount; i++) _players[i] = new PlayerState();
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: 179 passing. Golden unchanged — `Faction` is not hashed, and the ctor param is additive (defaults preserve every existing `new SimWorld(map, seed)` and `new SimWorld(map, seed, playerCount: N)` call; the `faction:` named arg keeps `playerCount` callers working since `faction` is last).

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/SimWorld.cs tests/SimCore.Tests/TestFactions.cs tests/SimCore.Tests/SimWorldFactionTests.cs
git commit -m "feat: SimWorld holds optional FactionDef; shared TestFactions catalog"
```

---

### Task 5: Id-based BuildCommand + building prerequisite gating

**Files:**
- Modify: `src/SimCore/Sim/Commands.cs` (`BuildCommand` → id-based)
- Modify: `src/SimCore/Sim/SimWorld.cs` (`Apply` build case)
- Modify: existing build/economy test fixtures (mechanical migration)
- Test: `tests/SimCore.Tests/BuildPrerequisiteTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SimCore.Tests/BuildPrerequisiteTests.cs
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class BuildPrerequisiteTests
{
    // A faction where "factory" requires "depot".
    private static FactionDef Faction()
    {
        var none = System.Array.Empty<string>();
        var depot = new BuildingSpec(100, 2, 2, 100, 5, IsDepot: true);
        var factory = new BuildingSpec(120, 2, 2, 120, 5, CanTrain: true);
        return new FactionDef("f", "F",
            units: System.Array.Empty<UnitDef>(),
            buildings: new[]
            {
                new BuildingDef("depot", 1, none, depot),
                new BuildingDef("factory", 2, new[] { "depot" }, factory),
            });
    }

    private static (SimWorld w, int worker) WorldWithWorker()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: Faction());
        w.Players[0].Minerals = 500;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        return (w, worker);
    }

    [Fact]
    public void Build_By_Id_Places_Building()
    {
        var (w, worker) = WorldWithWorker();
        w.Step(new Command[] { new BuildCommand(0, worker, "depot", 6, 5) });
        Assert.Single(w.Buildings);
        Assert.Equal(400, w.Players[0].Minerals);
    }

    [Fact]
    public void Build_Rejected_When_Prerequisite_Missing()
    {
        var (w, worker) = WorldWithWorker();
        w.Step(new Command[] { new BuildCommand(0, worker, "factory", 6, 5) }); // needs depot
        Assert.Empty(w.Buildings);
        Assert.Equal(500, w.Players[0].Minerals);
    }

    [Fact]
    public void Build_Allowed_Once_Prerequisite_Complete()
    {
        var (w, worker) = WorldWithWorker();
        w.Step(new Command[] { new BuildCommand(0, worker, "depot", 6, 5) });
        for (int i = 0; i < 5; i++) w.Step(System.Array.Empty<Command>()); // depot completes (BuildTime 5)
        Assert.True(w.Buildings[0].IsComplete);
        w.Step(new Command[] { new BuildCommand(0, worker, "factory", 10, 5) });
        Assert.Equal(2, w.Buildings.Count);
    }

    [Fact]
    public void Build_Rejected_When_Prerequisite_Incomplete()
    {
        var (w, worker) = WorldWithWorker();
        w.Step(new Command[] { new BuildCommand(0, worker, "depot", 6, 5) }); // not yet complete
        w.Step(new Command[] { new BuildCommand(0, worker, "factory", 10, 5) });
        Assert.Single(w.Buildings); // factory rejected — depot incomplete
    }

    [Fact]
    public void Build_Rejected_For_Unknown_Def_Id()
    {
        var (w, worker) = WorldWithWorker();
        w.Step(new Command[] { new BuildCommand(0, worker, "nonesuch", 6, 5) });
        Assert.Empty(w.Buildings);
        Assert.Equal(500, w.Players[0].Minerals);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter BuildPrerequisiteTests`
Expected: compilation FAILS — `BuildCommand` still takes a `BuildingSpec`, not a string id.

- [ ] **Step 3: Implement.**

In `src/SimCore/Sim/Commands.cs`, change `BuildCommand` (line 14):

```csharp
public sealed record BuildCommand(int PlayerId, int WorkerUnitId, string BuildingDefId, int CellX, int CellY) : Command(PlayerId);
```

In `src/SimCore/Sim/SimWorld.cs`, replace the `BuildCommand` case (lines 166–175):

```csharp
            case BuildCommand bc:
                var bdef = Faction?.GetBuilding(bc.BuildingDefId);
                if (bdef is null) break; // unknown def id (or no faction) — reject
                var builder = GetUnit(bc.WorkerUnitId);
                if (builder is null || builder.OwnerId != bc.PlayerId) break;
                if (!PrerequisitesMet(bc.PlayerId, bdef.Requires)) break;
                if (_players[bc.PlayerId].Minerals < bdef.Spec.MineralCost) break;
                var siteCenter = FootprintCenter(bc.CellX, bc.CellY, bdef.Spec.Width, bdef.Spec.Height);
                if ((builder.Position - siteCenter).LengthSquared() > Fix.FromInt(16)) break; // within 4 of site center
                if (!FootprintPlaceable(bc.CellX, bc.CellY, bdef.Spec.Width, bdef.Spec.Height)) break;
                _players[bc.PlayerId].Minerals -= bdef.Spec.MineralCost;
                PlaceBuilding(bc.PlayerId, bdef.Spec, bc.CellX, bc.CellY);
                break;
```

Add the prerequisite helper (place it near `Apply`, e.g. after the `Apply` method):

```csharp
    /// <summary>True if the player owns ≥1 complete building of every required def id.
    /// Building defs are identified by matching the placed building's spec against the faction
    /// catalog by reference (PlaceBuilding stores the def's Spec instance).</summary>
    private bool PrerequisitesMet(int playerId, IReadOnlyList<string> requires)
    {
        if (requires.Count == 0) return true;
        if (Faction is null) return false;
        foreach (var reqId in requires)
        {
            var reqDef = Faction.GetBuilding(reqId);
            if (reqDef is null) return false;
            bool owned = false;
            foreach (var b in _buildings)
                if (b.OwnerId == playerId && b.IsComplete && ReferenceEquals(b.Spec, reqDef.Spec))
                {
                    owned = true;
                    break;
                }
            if (!owned) return false;
        }
        return true;
    }
```

Note: `PlaceBuilding(playerId, spec, x, y)` already stores `spec` on the `Building` (see `SimWorld.Buildings.cs`), so reference-equality against `reqDef.Spec` correctly identifies "owns a building of this def." This requires the faction's def specs to be the same instances used at placement — which they are, since `Apply` passes `bdef.Spec` to `PlaceBuilding`. (Plan 3d, when packs are loaded once into a single `FactionDef`, preserves this; if a future change clones specs at placement, switch this to store the def id on `Building`.)

- [ ] **Step 4: Migrate existing build/economy fixtures and run the full suite**

Find every test constructing a `BuildCommand` with a spec and convert it to id-based against `TestFactions.Standard`:

Run: `dotnet test` first — it will FAIL to compile in the building/economy test files. For each failing file (expected: `BuildingTests.cs`, `ConstructionTests.cs`, `ProductionTests.cs`, `BuildingCombatTests.cs`, `HarvestTests.cs`, and any other that builds), apply this mechanical migration:
- Construct the world with `faction: TestFactions.Standard`: `new SimWorld(map, seed, faction: TestFactions.Standard)` (keep any existing `playerCount:` arg).
- Replace `new BuildCommand(p, worker, SomeSpec, x, y)` → `new BuildCommand(p, worker, "<id>", x, y)` where `<id>` is the matching def in `TestFactions.Standard` (`BuildingTests.Depot`→`"depot"`; the `Barracks` fixture in `ProductionTests`→`"barracks"`; the `Hut` fixture in `BuildingCombatTests`→`"hut"`).
- Where a test referenced a now-removed local spec constant only to pass into a command, delete the local constant and use the id. Where a test still reads spec fields for assertions (e.g. `BuildingTests.Depot.BuildTimeTicks`), keep a reference to `TestFactions.DepotSpec` (or the matching spec) for those reads.

The determinism scenario also constructs `BuildCommand` with a spec, so it won't compile either. Convert its build side now (its `TrainCommand` keeps the old `UnitSpec` form — that signature doesn't change until Task 6, so it still compiles):
- In `DeterminismTests.Scenario()`, add the scenario-local faction from Task 8 Step 1 (the `scenarioFaction` with `depot`, `rax`, and `marine` defs) above the `new SimWorld(...)` line, and construct the world with `faction: scenarioFaction`.
- Convert `new BuildCommand(0, worker, depotSpec, 7, 11)` → `new BuildCommand(0, worker, "depot", 7, 11)` and `new BuildCommand(0, worker, raxSpec, 11, 9)` → `new BuildCommand(0, worker, "rax", 11, 9)`.
- Leave the two `new TrainCommand(0, RaxId, marineSpec)` calls as-is for now (converted in Task 6).

Re-run: `dotnet test`
Expected: all passing (≈184: 179 + 5 new), including the three determinism tests with the **golden unchanged** (the scenario's depot/rax specs and build timings are identical; ids assigned in the same order; no new hashed state). If the golden fails here, the prerequisite/footprint refactor changed build behavior — debug it (e.g. the rax/depot have no `Requires`, so both builds must still succeed exactly as before); do NOT re-pin.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: id-based BuildCommand with building prerequisite gating; migrate fixtures"
```

---

### Task 6: Id-based TrainCommand + produced_by + prerequisite gating

**Files:**
- Modify: `src/SimCore/Sim/Commands.cs` (`TrainCommand` → id-based)
- Modify: `src/SimCore/Sim/SimWorld.cs` (`Apply` train case)
- Modify: `src/SimCore/Sim/SimWorld.Buildings.cs` (production spawns from def spec — verify no change needed)
- Modify: production test fixtures
- Test: `tests/SimCore.Tests/TrainPrerequisiteTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SimCore.Tests/TrainPrerequisiteTests.cs
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class TrainPrerequisiteTests
{
    private static (SimWorld w, int barracks) ReadyBarracks()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: TestFactions.Standard);
        w.Players[0].Minerals = 1000;
        w.Players[0].SupplyCap = 20;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(0, worker, "barracks", 7, 5) });
        for (int i = 0; i < TestFactions.BarracksSpec.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        return (w, w.Buildings[0].Id);
    }

    [Fact]
    public void Train_By_Id_Enqueues_And_Spawns()
    {
        var (w, b) = ReadyBarracks();
        var before = w.Units.Count;
        w.Step(new Command[] { new TrainCommand(0, b, "marine") });
        for (int i = 0; i < TestFactions.MarineSpec.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(before + 1, w.Units.Count);
        Assert.Equal(6, w.Units[^1].Weapon!.Damage); // marine stats from the def spec
    }

    [Fact]
    public void Train_Rejected_When_ProducedBy_Mismatch()
    {
        // marine is produced_by "barracks"; a depot cannot train it.
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: TestFactions.Standard);
        w.Players[0].Minerals = 1000; w.Players[0].SupplyCap = 20;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(0, worker, "depot", 7, 5) });
        for (int i = 0; i < TestFactions.DepotSpec.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        var depotId = w.Buildings[0].Id;
        w.Step(new Command[] { new TrainCommand(0, depotId, "marine") }); // depot can't train (not CanTrain, wrong producer)
        Assert.Empty(w.GetBuilding(depotId)!.Queue);
    }

    [Fact]
    public void Train_Rejected_When_Tier2_Prerequisite_Missing()
    {
        // tank requires "depot"; barracks alone can't train it.
        var (w, b) = ReadyBarracks(); // only a barracks exists
        w.Step(new Command[] { new TrainCommand(0, b, "tank") });
        Assert.Empty(w.GetBuilding(b)!.Queue);
    }

    [Fact]
    public void Train_Allowed_When_Tier2_Prerequisite_Present()
    {
        var (w, b) = ReadyBarracks();
        var worker = w.SpawnUnit(0, w.Map.CellCenter(15, 15), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(0, worker, "depot", 14, 14) });
        for (int i = 0; i < TestFactions.DepotSpec.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        w.Step(new Command[] { new TrainCommand(0, b, "tank") });
        Assert.Single(w.GetBuilding(b)!.Queue); // depot present → tank accepted
    }

    [Fact]
    public void Train_Rejected_For_Unknown_Unit_Id()
    {
        var (w, b) = ReadyBarracks();
        w.Step(new Command[] { new TrainCommand(0, b, "griffon") });
        Assert.Empty(w.GetBuilding(b)!.Queue);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter TrainPrerequisiteTests`
Expected: compilation FAILS — `TrainCommand` still takes a `UnitSpec`.

- [ ] **Step 3: Implement.**

In `src/SimCore/Sim/Commands.cs`, change `TrainCommand` (line 16):

```csharp
public sealed record TrainCommand(int PlayerId, int BuildingId, string UnitDefId) : Command(PlayerId);
```

In `src/SimCore/Sim/SimWorld.cs`, replace the `TrainCommand` case (lines 176–186):

```csharp
            case TrainCommand tc:
                var udef = Faction?.GetUnit(tc.UnitDefId);
                if (udef is null) break; // unknown unit def — reject
                var trainer = GetBuilding(tc.BuildingId);
                if (trainer is null || trainer.OwnerId != tc.PlayerId || !trainer.IsComplete || !trainer.Spec.CanTrain) break;
                // produced_by: this building must be the unit's producer.
                var trainerDef = Faction!.GetBuilding(udef.ProducedBy);
                if (trainerDef is null || !ReferenceEquals(trainer.Spec, trainerDef.Spec)) break;
                if (!PrerequisitesMet(tc.PlayerId, udef.Requires)) break;
                if (trainer.Queue.Count >= Building.MaxQueueLength) break;
                var ps = _players[tc.PlayerId];
                if (ps.Minerals < udef.Spec.MineralCost) break;
                if (ps.SupplyUsed + udef.Spec.SupplyCost > ps.SupplyCap) break;
                ps.Minerals -= udef.Spec.MineralCost;
                ps.SupplyUsed += udef.Spec.SupplyCost; // reserve supply at enqueue
                trainer.Queue.Add(new TrainingItem { Spec = udef.Spec, RemainingTicks = udef.Spec.BuildTimeTicks });
                break;
```

(`UpdateProduction` in `SimWorld.Buildings.cs` already spawns from `TrainingItem.Spec` — no change needed there. The `produced_by` check via reference-equality identifies the building's def the same way `PrerequisitesMet` does.)

- [ ] **Step 4: Migrate production fixtures and run the full suite**

Convert every `new TrainCommand(p, building, SomeUnitSpec)` in the test suite to `new TrainCommand(p, building, "<id>")` against `TestFactions.Standard` (`ProductionTests`' `Marine`→`"marine"`). Worlds in those tests must be constructed with `faction: TestFactions.Standard`. Where a test reads `Marine.BuildTimeTicks` etc. for loop bounds, use `TestFactions.MarineSpec.BuildTimeTicks`.

Run: `dotnet test`
Expected: all passing (≈189: 184 + 5 new). Golden unchanged.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: id-based TrainCommand with produced_by + prerequisite gating; migrate fixtures"
```

---

### Task 7: Godot wiring — id-based commands + catalog-driven build menu

The Godot layer must issue id-based commands and read the faction catalog. This is C# that compiles against SimCore; verify with a build (xUnit can't drive Godot UI).

**Files:**
- Modify: `godot/scripts/CommandController.cs` (build ghost → def id)
- Modify: the Godot HUD/build-menu source that issues `BuildCommand`/`TrainCommand` (locate via grep)
- Modify: `godot/scripts/SimRunner.cs` or wherever the world is constructed (pass `ReferenceFaction.Def`)

- [ ] **Step 1: Locate all Godot command sites and world construction**

Run:
```bash
cd /c/Users/lssha/llm-rts && grep -rn "new BuildCommand\|new TrainCommand\|new SimWorld\|ReferenceSpecs\." godot/
```
Expected: the build-ghost site in `CommandController.cs`, the train site in the HUD, and the `new SimWorld(...)` construction. Record each.

- [ ] **Step 2: Pass the reference faction at world construction**

At the `new SimWorld(...)` call (in `SimRunner` or the bootstrap), add `faction: ReferenceFaction.Def`. Wherever the initial map is seeded with starting units/buildings, leave direct `SpawnUnit`/`PlaceBuilding`/`AddResourceNode` calls as-is (setup-time APIs, not commands).

- [ ] **Step 3: Convert the build ghost to carry a def id**

In `CommandController.cs`, change the ghost to hold a `BuildingDef` (so `_Draw` can still read `Width`/`Height` from `def.Spec`) and issue the id:

```csharp
    private BuildingDef? _ghostDef;     // non-null → placement mode

    public void ArmBuildGhost(BuildingDef def) { _attackMoveArmed = false; _patrolArmed = false; _ghostDef = def; QueueRedraw(); }
```

Replace `_ghostSpec` reads with `_ghostDef`, using `_ghostDef.Spec.Width`/`.Height` in `_Draw`/`FootprintFree`, and in `TryPlaceGhost`:

```csharp
        _runner.Enqueue(new BuildCommand(_sel.ControlledPlayer, worker.Id, _ghostDef!.Id, cx, cy));
```

Update every `_ghostSpec` reference in the file accordingly (the `null` checks, `_Process`, `_Draw`, escape/right-click clears).

- [ ] **Step 4: Point the build/train menu at the catalog**

In the HUD/menu source, replace hardcoded `ReferenceSpecs.Depot`/`ReferenceSpecs.Barracks` build buttons with iteration over `runner.World.Faction!.BuildingList` (call `ArmBuildGhost(def)`), and replace train buttons with `runner.World.Faction!.GetBuilding(selectedBuildingDefId)`-driven unit lists, issuing `new TrainCommand(player, buildingId, unitDef.Id)`. Identify which catalog entries a selected building can train by filtering `Faction.UnitList.Where(u => u.ProducedBy == <selected building's def id>)`. To know a placed building's def id, match its `Spec` against `Faction.BuildingList` by reference (same approach the sim uses) or add a small helper. Keep the menu minimal — exact UI layout is unchanged, only the data source.

- [ ] **Step 5: Build the Godot project**

Run:
```bash
cd /c/Users/lssha/llm-rts && dotnet build godot/LlmRts.Godot.csproj -c Debug
```
(If the csproj name differs, use the one found under `godot/`.)
Expected: build succeeds, 0 errors. If the HUD references a removed `_ghostSpec`/`ReferenceSpecs` build button, fix until it compiles.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: Godot issues id-based build/train commands from the faction catalog"
```

---

### Task 8: Determinism gate — verify the id-based scenario

The scenario was converted across Tasks 5 (build side + scenario faction) and 6 (train side). This task is the **determinism checkpoint**: confirm the conversion preserved the trajectory and the economy still runs. Reference — the `scenarioFaction` added in Task 5 Step 4 is:

```csharp
        var scenarioFaction = new FactionDef("scenario", "Scenario",
            units: new[] { new UnitDef("marine", 1, "rax", System.Array.Empty<string>(), marineSpec) },
            buildings: new[]
            {
                new BuildingDef("depot", 1, System.Array.Empty<string>(), depotSpec),
                new BuildingDef("rax", 1, System.Array.Empty<string>(), raxSpec),
            });
```

and the world is constructed `new SimWorld(map, seed: 1234, faction: scenarioFaction)`. Confirm the two t=250 `TrainCommand`s read `"marine"` (converted in Task 6) and the two `BuildCommand`s read `"depot"`/`"rax"`.

- [ ] **Step 1: Run the replay + golden tests**

Run: `dotnet test --filter DeterminismTests`
Expected: all three PASS, `GoldenTrajectoryHash` **unchanged** at `4005804941942785108UL`. The specs, build/train timings, and id-assignment order are identical, and no new state is hashed, so the trajectory must match. If the golden FAILS, do NOT re-pin reflexively — a failure means the refactor altered behavior (most likely a prerequisite/`produced_by` check wrongly rejecting a command that previously succeeded: the rax is `CanTrain` with no requires, marine is `produced_by "rax"` with no requires, so both t=250 trains must still enqueue). Debug to root cause; re-pin only if you confirm an intended, benign change (none is expected).

- [ ] **Step 2: Verify economy still executes (temporary instrumentation)**

Temporarily, in a scratch run, confirm the t=250 marines still spawn (gating must not silently reject them): `w.Units.Count` should rise by 2 by t≈280. Remove the instrumentation; `git status` shows a clean tree.

- [ ] **Step 3: Full suite, both configurations**

Run: `dotnet test` and `dotnet test --configuration Release`
Expected: all passing in both (~189), identical golden constant.

- [ ] **Step 4: No commit needed**

The scenario edits were committed in Tasks 5 and 6. This task is a verification gate; if `git status` is clean, there is nothing to commit. If you made a fix during Step 1 debugging, commit it:

```bash
git add tests/SimCore.Tests/DeterminismTests.cs
git commit -m "test: determinism scenario verification (id-based commands)"
```

---

## Done Criteria

- `dotnet test --configuration Release` passes (~189 tests).
- `FactionDef` exists with `Validate()`; `ReferenceFaction.Def.Validate()` is empty.
- `BuildCommand`/`TrainCommand` are id-based; prerequisites and `produced_by` are enforced; unknown ids are rejected.
- Godot builds and issues id-based commands from `ReferenceFaction.Def`.
- Golden trajectory hash **unchanged** (no new hashed state; behavior preserved).
- `grep -ri godot src/SimCore` → no hits; no float/double in `src/SimCore` outside `Fix.ToString()`.

**Next plan (3b):** Upgrades / research — research orders at buildings, upgrade defs applying stat deltas, per-player applied-upgrade state (hashed, scenario re-pinned), extending `FactionDef` with an upgrades catalog.

## Plan-3b Inputs (carried forward from 3a final review — STATUS: 3a COMPLETE, merged 2026-06-13, 183 SimCore tests, golden 4005804941942785108UL)

1. **Extend FactionDef additively** — add `IEnumerable<UpgradeDef> upgrades` ctor param + `UpgradeList`/`GetUpgrade(id)`, mirroring units/buildings; keep "dicts lookup-only, ordered enumeration via *List".
2. **Grow `Validate()`** — upgrade `ResearchedAt` building exists; upgrade `Requires` (buildings AND upgrades) resolve; extend cycle detection to the upgrade/combined prereq graph (current DFS is building-only).
3. **`ResearchCommand(PlayerId, BuildingId, UpgradeDefId)`** — mirror TrainCommand: validate producer via `building.DefId == upgradeDef.ResearchedAt`, `PrerequisitesMet`, charge minerals/time. Research is one-shot (no supply, not queue-of-N) — decide whether it reuses Building.Queue or gets a single-slot research-progress field.
4. **Per-player applied-upgrade state must be hashed deterministically** — add to `PlayerState` as an ORDERED structure (sorted list of completed upgrade ids, or index bitmask) — NEVER a plain HashSet iterated in StateHasher. Fold into StateHasher and re-pin the golden in the same commit.
5. **`PrerequisitesMet` must learn upgrade prereqs** — refactor to a single `Has(playerId, reqId)` resolving against both owned-complete-buildings and applied-upgrades.
6. **CRUX DECISION for the 3b spec — upgrade effect application:** tiers/prereqs were pure gating (no stat mutation); upgrades mutate stats, colliding with the immutable-spec-copied-at-spawn model. Decide in the spec: (a) **apply-at-spawn** (read player upgrades when constructing the entity — simpler, determinism-friendly, only affects future units) vs (b) **apply-retroactively** (recompute existing entities' stats on research completion — bigger hashing surface, re-derives hashed fields). Recommend (a) unless retroactive upgrades are a hard requirement.
7. **Minor (not blocking):** `FactionDef.Validate()` is library-only (called by the 3d pack loader, not at runtime) — an invalid hand-authored faction isn't caught live today; fine while ReferenceFaction is the only faction. `PrerequisitesMet` is O(requires×buildings)/command — fine at current scale.
