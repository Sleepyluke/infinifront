# Upgrades & Research Implementation Plan (Plan 3b)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add researchable upgrades that retroactively boost unit stats, implemented via compute-on-read effective stats so the only new hashed state is a small per-player applied-upgrade set.

**Architecture:** `FactionDef` gains an `UpgradeDef` catalog. A `ResearchCommand` runs at a building (single research slot); on completion the upgrade id is added to the owner's sorted `AppliedUpgrades`. Combat/movement/vision read **effective stats** (`base + Σ applicable upgrade deltas`) via `SimWorld` accessors — no `Unit`/`Weapon` field is ever mutated by an upgrade. Units gain a `DefId` (like `Building.DefId`) so upgrades can target unit types.

**Tech Stack:** .NET 8, xUnit, existing SimCore. PATH note: if `dotnet` is not found, run `$env:Path += ';C:\Program Files\dotnet'`.

**Spec:** `docs/superpowers/specs/2026-06-13-upgrades-research-design.md`. **Arc:** plan 3b of 4 (3a tiers ✅ → upgrades → mechanics → pack format).

**Refinement vs spec:** `UpgradeDef.Delta` is a **`Fix`** (the spec wrote `int`). Range and Speed are fractional `Fix` stats, so an int delta can't express a meaningful speed boost (+1 cell/tick is enormous). `Fix` deltas unify on the engine's native numeric type; int-typed stats (Damage/CooldownTicks/Sight) read `delta.ToInt()`.

**Golden-hash protocol:** `Trajectory_Hash_Matches_Golden_Constant` (`tests/SimCore.Tests/DeterminismTests.cs`, currently `4005804941942785108UL`) folds all 500 per-tick hashes. Tasks 1–6 are **golden-unchanged** (additive catalog, unhashed `DefId`, `AppliedUpgrades`/research-slot not yet hashed and empty/zero in the scenario, and effective stats == base when no upgrades are applied). The single re-pin happens in **Task 7** (StateHasher v4 folds the new state *and* the scenario researches an upgrade mid-run — one combined behavior+hash change). The two replay tests must never fail.

**Current state:** 183 SimCore tests on `master`. `FactionDef` ctor is `(id, name, units, buildings)`. `Building.DefId` exists. `PlayerState` has Minerals/SupplyUsed/SupplyCap. Combat reads `u.Weapon.Range/Damage/CooldownTicks` in `SimWorld.Combat.cs`; `MoveUnits` reads `u.SpeedPerTick` (SimWorld.cs ~356/362); vision reads `u.SightRange` (SimWorld.Vision.cs:35). `PrerequisitesMet(playerId, requires)` compares `b.DefId == reqId`.

---

### Task 1: UpgradeDef + FactionDef upgrades catalog (additive)

**Files:**
- Modify: `src/SimCore/Sim/FactionDef.cs`
- Test: `tests/SimCore.Tests/UpgradeCatalogTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/SimCore.Tests/UpgradeCatalogTests.cs
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class UpgradeCatalogTests
{
    private static UnitSpec U() => new(40, Fix.FromFraction(1, 2), 50, 1, 20);
    private static BuildingSpec B() => new(100, 2, 2, 100, 10, CanTrain: true);

    [Fact]
    public void Upgrades_Are_Catalogued_And_Looked_Up()
    {
        var faction = new FactionDef("f", "F",
            units: new[] { new UnitDef("trooper", 1, "rax", new string[0], U()) },
            buildings: new[] { new BuildingDef("rax", 1, new string[0], B()) },
            upgrades: new[]
            {
                new UpgradeDef("dmg1", 1, "rax", new string[0], new[] { "trooper" },
                    UpgradeStat.Damage, Fix.FromInt(2), 50, 20),
            });

        var up = faction.GetUpgrade("dmg1")!;
        Assert.Equal("dmg1", up.Id);
        Assert.Equal("rax", up.ResearchedAt);
        Assert.Equal(UpgradeStat.Damage, up.Stat);
        Assert.Equal(Fix.FromInt(2), up.Delta);
        Assert.Single(faction.UpgradeList);
        Assert.Null(faction.GetUpgrade("nope"));
    }

    [Fact]
    public void Faction_Without_Upgrades_Constructor_Still_Works()
    {
        var faction = new FactionDef("f", "F",
            units: new[] { new UnitDef("trooper", 1, "rax", new string[0], U()) },
            buildings: new[] { new BuildingDef("rax", 1, new string[0], B()) });
        Assert.Empty(faction.UpgradeList);
        Assert.Null(faction.GetUpgrade("x"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter UpgradeCatalogTests`
Expected: compilation FAILS (`UpgradeDef`, the upgrades ctor param don't exist).

- [ ] **Step 3: Implement.** In `src/SimCore/Sim/FactionDef.cs`:

Add the enum + record at the bottom of the file (next to UnitDef/BuildingDef):

```csharp
public enum UpgradeStat { Damage, Range, CooldownTicks, Speed, Sight }

public sealed record UpgradeDef(
    string Id, int Tier, string ResearchedAt, IReadOnlyList<string> Requires,
    IReadOnlyList<string> TargetUnitDefIds, UpgradeStat Stat, SimCore.Math.Fix Delta,
    int MineralCost, int ResearchTicks);
```

In the `FactionDef` class add the upgrades storage + accessors, and **add an overloaded constructor** that takes upgrades while keeping the existing 4-arg constructor working. Replace the existing constructor with a chained pair:

```csharp
    public IReadOnlyList<UpgradeDef> UpgradeList { get; }
    private readonly Dictionary<string, UpgradeDef> _upgrades = new();

    public FactionDef(string id, string name, IEnumerable<UnitDef> units, IEnumerable<BuildingDef> buildings)
        : this(id, name, units, buildings, System.Array.Empty<UpgradeDef>()) { }

    public FactionDef(string id, string name, IEnumerable<UnitDef> units,
        IEnumerable<BuildingDef> buildings, IEnumerable<UpgradeDef> upgrades)
    {
        Id = id;
        Name = name;
        var ul = new List<UnitDef>();
        foreach (var u in units) { ul.Add(u); _units[u.Id] = u; }
        var bl = new List<BuildingDef>();
        foreach (var b in buildings) { bl.Add(b); _buildings[b.Id] = b; }
        var gl = new List<UpgradeDef>();
        foreach (var g in upgrades) { gl.Add(g); _upgrades[g.Id] = g; }
        UnitList = ul;
        BuildingList = bl;
        UpgradeList = gl;
    }

    public UpgradeDef? GetUpgrade(string id) => _upgrades.TryGetValue(id, out var g) ? g : null;
```

(Keep the existing `_units`/`_buildings` field declarations and `GetUnit`/`GetBuilding`/`UnitList`/`BuildingList` as they are — only the constructor is replaced and the upgrade members added.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: +2 tests, all passing. Golden unchanged (additive; the 4-arg ctor still used by ReferenceFaction/TestFactions).

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/FactionDef.cs tests/SimCore.Tests/UpgradeCatalogTests.cs
git commit -m "feat: UpgradeDef + FactionDef upgrades catalog (additive ctor)"
```

---

### Task 2: FactionDef.Validate() — upgrade integrity

**Files:**
- Modify: `src/SimCore/Sim/FactionDef.cs` (`Validate`)
- Test: `tests/SimCore.Tests/UpgradeValidateTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SimCore.Tests/UpgradeValidateTests.cs
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class UpgradeValidateTests
{
    private static UnitSpec U() => new(40, Fix.FromFraction(1, 2), 50, 1, 20);
    private static BuildingSpec B() => new(100, 2, 2, 100, 10, CanTrain: true);
    private static UpgradeDef Up(string id, string at, string[] req, string[] targets) =>
        new(id, 1, at, req, targets, UpgradeStat.Damage, Fix.FromInt(1), 50, 20);

    private static FactionDef Faction(UpgradeDef[] ups) => new("f", "F",
        units: new[] { new UnitDef("trooper", 1, "rax", new string[0], U()) },
        buildings: new[] { new BuildingDef("rax", 1, new string[0], B()) },
        upgrades: ups);

    [Fact]
    public void Good_Upgrade_Validates_Empty()
        => Assert.Empty(Faction(new[] { Up("dmg1", "rax", new string[0], new[] { "trooper" }) }).Validate());

    [Fact]
    public void Star_Target_Is_Valid()
        => Assert.Empty(Faction(new[] { Up("dmg1", "rax", new string[0], new[] { "*" }) }).Validate());

    [Fact]
    public void Dangling_ResearchedAt_Is_Flagged()
        => Assert.Contains(Faction(new[] { Up("dmg1", "ghost", new string[0], new[] { "*" }) }).Validate(),
            e => e.Contains("dmg1") && e.Contains("ghost"));

    [Fact]
    public void Dangling_Target_Unit_Is_Flagged()
        => Assert.Contains(Faction(new[] { Up("dmg1", "rax", new string[0], new[] { "ghostunit" }) }).Validate(),
            e => e.Contains("dmg1") && e.Contains("ghostunit"));

    [Fact]
    public void Dangling_Requires_Resolves_Building_Or_Upgrade()
    {
        // requires another upgrade that exists → valid
        var f = Faction(new[]
        {
            Up("dmg1", "rax", new string[0], new[] { "*" }),
            Up("dmg2", "rax", new[] { "dmg1" }, new[] { "*" }),
        });
        Assert.Empty(f.Validate());
        // requires something that is neither building nor upgrade → flagged
        var bad = Faction(new[] { Up("dmg2", "rax", new[] { "ghost" }, new[] { "*" }) });
        Assert.Contains(bad.Validate(), e => e.Contains("dmg2") && e.Contains("ghost"));
    }

    [Fact]
    public void Upgrade_Prerequisite_Cycle_Is_Flagged()
    {
        var f = Faction(new[]
        {
            Up("a", "rax", new[] { "b" }, new[] { "*" }),
            Up("b", "rax", new[] { "a" }, new[] { "*" }),
        });
        Assert.Contains(f.Validate(), e => e.Contains("cycle"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter UpgradeValidateTests`
Expected: several FAIL (Validate doesn't check upgrades yet).

- [ ] **Step 3: Implement.** In `FactionDef.Validate()`, before the final `return errors;`, add upgrade checks and extend cycle detection to the combined building+upgrade prerequisite graph.

Add after the existing building-requires loop:

```csharp
        foreach (var up in UpgradeList)
        {
            if (GetBuilding(up.ResearchedAt) is null)
                errors.Add($"upgrade '{up.Id}' ResearchedAt references unknown building '{up.ResearchedAt}'");
            foreach (var req in up.Requires)
                if (GetBuilding(req) is null && GetUpgrade(req) is null)
                    errors.Add($"upgrade '{up.Id}' requires unknown building/upgrade '{req}'");
            foreach (var t in up.TargetUnitDefIds)
                if (t != "*" && GetUnit(t) is null)
                    errors.Add($"upgrade '{up.Id}' targets unknown unit '{t}'");
        }
```

Replace the building-only cycle detection block with a combined graph that follows building→building and upgrade→(building|upgrade) edges. Change the existing `HasCycle` local function and its driving loops to:

```csharp
        // Combined prerequisite cycle detection over buildings + upgrades.
        var state = new Dictionary<string, int>(); // 0=unvisited,1=in-stack,2=done
        IReadOnlyList<string> RequiresOf(string id)
        {
            var b = GetBuilding(id);
            if (b is not null) return b.Requires;
            var up = GetUpgrade(id);
            if (up is not null) return up.Requires;
            return System.Array.Empty<string>();
        }
        bool Resolves(string id) => GetBuilding(id) is not null || GetUpgrade(id) is not null;
        bool HasCycle(string id)
        {
            if (state.TryGetValue(id, out var s)) { if (s == 1) return true; if (s == 2) return false; }
            state[id] = 1;
            foreach (var req in RequiresOf(id))
                if (Resolves(req) && HasCycle(req)) return true;
            state[id] = 2;
            return false;
        }
        bool cycleFound = false;
        foreach (var b in BuildingList)
            if (state.GetValueOrDefault(b.Id) == 0 && HasCycle(b.Id)) { cycleFound = true; break; }
        if (!cycleFound)
            foreach (var up in UpgradeList)
                if (state.GetValueOrDefault(up.Id) == 0 && HasCycle(up.Id)) { cycleFound = true; break; }
        if (cycleFound)
            errors.Add("prerequisite cycle detected");
```

(Remove the old building-only cycle block; this replaces it. Note: building ids and upgrade ids share the `state`/graph namespace — they are distinct id spaces in practice; if a building and upgrade ever shared an id the graph would merge them, which `Validate` authors should avoid. Acceptable for v1.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: +6 tests passing; existing `FactionDefValidateTests` still pass; golden unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/FactionDef.cs tests/SimCore.Tests/UpgradeValidateTests.cs
git commit -m "feat: FactionDef.Validate covers upgrades + combined prereq cycles"
```

---

### Task 3: Unit.DefId + threading through spawn/production

**Files:**
- Modify: `src/SimCore/Sim/Unit.cs` (add `DefId`)
- Modify: `src/SimCore/Sim/SimWorld.cs` (`Spawn` + overloads)
- Modify: `src/SimCore/Sim/Building.cs` (`TrainingItem.UnitDefId`)
- Modify: `src/SimCore/Sim/SimWorld.cs` (TrainCommand sets UnitDefId)
- Modify: `src/SimCore/Sim/SimWorld.Buildings.cs` (`UpdateProduction` passes def id)
- Test: `tests/SimCore.Tests/UnitDefIdTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SimCore.Tests/UnitDefIdTests.cs
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class UnitDefIdTests
{
    [Fact]
    public void Legacy_Spawn_Has_Empty_DefId()
    {
        var w = new SimWorld(new MapGrid(8, 8), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(1, 1), Fix.FromInt(1), 10);
        Assert.Equal("", w.GetUnit(id)!.DefId);
    }

    [Fact]
    public void Spec_Spawn_With_DefId_Sets_It()
    {
        var w = new SimWorld(new MapGrid(8, 8), seed: 1);
        var spec = new UnitSpec(40, Fix.FromFraction(1, 2), 50, 1, 5);
        var id = w.SpawnUnit(0, w.Map.CellCenter(1, 1), spec, "marine");
        Assert.Equal("marine", w.GetUnit(id)!.DefId);
    }

    [Fact]
    public void Trained_Unit_Carries_Def_Id()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: TestFactions.Standard);
        w.Players[0].Minerals = 1000; w.Players[0].SupplyCap = 20;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(0, worker, "barracks", 7, 5) });
        for (int i = 0; i < TestFactions.BarracksSpec.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        var b = w.Buildings[0].Id;
        w.Step(new Command[] { new TrainCommand(0, b, "marine") });
        for (int i = 0; i < TestFactions.MarineSpec.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal("marine", w.Units[^1].DefId);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter UnitDefIdTests`
Expected: compilation FAILS (`Unit.DefId`, the `SpawnUnit(...,spec,defId)` overload don't exist).

- [ ] **Step 3: Implement.**

In `src/SimCore/Sim/Unit.cs`, add (near `SupplyCost`):

```csharp
    public string DefId { get; init; } = ""; // unit def id; derived static label, NOT hashed (like Building.DefId)
```

In `src/SimCore/Sim/SimWorld.cs`, thread `defId` through `Spawn` and the spec overload. Replace the spec-based `SpawnUnit` and the `Spawn` helper:

```csharp
    public int SpawnUnit(int ownerId, FixVec pos, UnitSpec spec, string defId = "") =>
        Spawn(ownerId, pos, spec.Speed, spec.MaxHp, spec.SupplyCost, spec.Weapon?.Instantiate(), spec.Harvester, spec.SightRange, defId);

    private int Spawn(int ownerId, FixVec pos, Fix speedPerTick, int hp, int supplyCost, Weapon? weapon, HarvesterSpec? harvester, int sightRange, string defId = "")
    {
        EnsureOccupancy();
        var (cx, cy) = Map.WorldToCell(pos);
        if (OccupantAt(cx, cy) != 0) return 0;

        var u = new Unit
        {
            Id = _nextId++, OwnerId = ownerId, Position = pos, SpeedPerTick = speedPerTick,
            Hp = hp, SupplyCost = supplyCost, Weapon = weapon, Harvester = harvester,
            SightRange = sightRange, DefId = defId
        };
        _units.Add(u);
        _byId[u.Id] = u;
        _players[ownerId].SupplyUsed += supplyCost;
        ClaimCell(cx, cy, u.Id);
        return u.Id;
    }
```

(The legacy `SpawnUnit(ownerId, pos, speedPerTick, hp, weapon)` overload is unchanged — it calls `Spawn(...)` which now defaults `defId: ""`.)

In `src/SimCore/Sim/Building.cs`, add `UnitDefId` to `TrainingItem`:

```csharp
public sealed class TrainingItem
{
    public UnitSpec Spec { get; init; } = null!;
    public string UnitDefId { get; init; } = "";
    public int RemainingTicks { get; set; }
}
```

In `src/SimCore/Sim/SimWorld.cs` TrainCommand case, set the def id on the queued item — change the enqueue line to:

```csharp
                trainer.Queue.Add(new TrainingItem { Spec = udef.Spec, UnitDefId = tc.UnitDefId, RemainingTicks = udef.Spec.BuildTimeTicks });
```

In `src/SimCore/Sim/SimWorld.Buildings.cs` `UpdateProduction`, pass the def id when spawning the finished unit — change the `SpawnUnit(...)` call to include `item.UnitDefId`:

```csharp
            SpawnUnit(b.OwnerId, Map.CellCenter(cell.Value.x, cell.Value.y), item.Spec, item.UnitDefId);
```

(Read the actual `UpdateProduction` spawn line first; preserve its surrounding logic — only add the `item.UnitDefId` argument.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: +3 tests passing. Golden unchanged — `DefId`/`UnitDefId` are not hashed (derived labels), and no behavior depends on them yet.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/Unit.cs src/SimCore/Sim/SimWorld.cs src/SimCore/Sim/Building.cs src/SimCore/Sim/SimWorld.Buildings.cs tests/SimCore.Tests/UnitDefIdTests.cs
git commit -m "feat: Unit.DefId threaded through spawn + production (unhashed label)"
```

---

### Task 4: PlayerState.AppliedUpgrades + unified Has() prerequisite

**Files:**
- Modify: `src/SimCore/Sim/PlayerState.cs`
- Modify: `src/SimCore/Sim/SimWorld.cs` (`PrerequisitesMet` → uses `Has`)
- Test: `tests/SimCore.Tests/AppliedUpgradeTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SimCore.Tests/AppliedUpgradeTests.cs
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class AppliedUpgradeTests
{
    [Fact]
    public void Add_Upgrade_Is_Sorted_And_Queryable()
    {
        var ps = new PlayerState();
        ps.AddUpgrade("zeta");
        ps.AddUpgrade("alpha");
        ps.AddUpgrade("alpha"); // idempotent
        Assert.True(ps.HasUpgrade("alpha"));
        Assert.True(ps.HasUpgrade("zeta"));
        Assert.False(ps.HasUpgrade("mu"));
        Assert.Equal(new[] { "alpha", "zeta" }, ps.AppliedUpgrades); // sorted, deduped
    }

    [Fact]
    public void Prerequisite_Can_Be_An_Applied_Upgrade()
    {
        // A faction where building "armory" requires upgrade "permit".
        var none = System.Array.Empty<string>();
        var armory = new BuildingSpec(120, 2, 2, 120, 3, CanTrain: true);
        var depot = new BuildingSpec(100, 2, 2, 100, 3, IsDepot: true);
        var faction = new FactionDef("f", "F",
            units: System.Array.Empty<UnitDef>(),
            buildings: new[]
            {
                new BuildingDef("depot", 1, none, depot),
                new BuildingDef("armory", 2, new[] { "permit" }, armory),
            },
            upgrades: new[]
            {
                new UpgradeDef("permit", 1, "depot", none, new[] { "*" }, UpgradeStat.Damage, Fix.Zero, 0, 1),
            });

        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: faction);
        w.Players[0].Minerals = 1000;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        // armory blocked until "permit" is applied
        w.Step(new Command[] { new BuildCommand(0, worker, "armory", 7, 5) });
        Assert.Empty(w.Buildings);
        // grant the upgrade directly (research lands in Task 5) and retry
        w.Players[0].AddUpgrade("permit");
        w.Step(new Command[] { new BuildCommand(0, worker, "armory", 7, 5) });
        Assert.Single(w.Buildings);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter AppliedUpgradeTests`
Expected: compilation FAILS (`AddUpgrade`/`HasUpgrade`/`AppliedUpgrades` don't exist; prerequisite-by-upgrade not supported).

- [ ] **Step 3: Implement.**

In `src/SimCore/Sim/PlayerState.cs`:

```csharp
namespace SimCore.Sim;

/// <summary>Per-player economy + tech state. AppliedUpgrades is kept SORTED so the hash
/// is independent of research-completion order. (Hashed in StateHasher v4 — Task 7.)</summary>
public sealed class PlayerState
{
    public int Minerals { get; set; }
    public int SupplyUsed { get; set; }
    public int SupplyCap { get; set; }

    private readonly System.Collections.Generic.List<string> _appliedUpgrades = new();
    public System.Collections.Generic.IReadOnlyList<string> AppliedUpgrades => _appliedUpgrades;

    public bool HasUpgrade(string id) => _appliedUpgrades.Contains(id);

    /// <summary>Idempotent; inserts in sorted position for deterministic ordering.</summary>
    public void AddUpgrade(string id)
    {
        int i = _appliedUpgrades.BinarySearch(id, System.StringComparer.Ordinal);
        if (i < 0) _appliedUpgrades.Insert(~i, id);
    }
}
```

In `src/SimCore/Sim/SimWorld.cs`, generalize prerequisite resolution. Replace `PrerequisitesMet` with a version that delegates to `Has`:

```csharp
    private bool PrerequisitesMet(int playerId, IReadOnlyList<string> requires)
    {
        if (requires.Count == 0) return true;
        foreach (var reqId in requires)
            if (!Has(playerId, reqId)) return false;
        return true;
    }

    /// <summary>A prerequisite id is satisfied by owning a completed building of that DefId
    /// OR by having applied an upgrade of that id.</summary>
    private bool Has(int playerId, string reqId)
    {
        foreach (var b in _buildings)
            if (b.OwnerId == playerId && b.IsComplete && b.DefId == reqId) return true;
        return _players[playerId].HasUpgrade(reqId);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: +2 tests passing; existing build/train prerequisite tests still pass. Golden unchanged — `AppliedUpgrades` is empty everywhere and not yet hashed.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/PlayerState.cs src/SimCore/Sim/SimWorld.cs tests/SimCore.Tests/AppliedUpgradeTests.cs
git commit -m "feat: per-player AppliedUpgrades (sorted) + unified Has() prerequisite"
```

---

### Task 5: ResearchCommand + research slot + UpdateResearch

**Files:**
- Modify: `src/SimCore/Sim/Building.cs` (research slot fields)
- Modify: `src/SimCore/Sim/Commands.cs` (`ResearchCommand`)
- Modify: `src/SimCore/Sim/SimWorld.cs` (`Apply` ResearchCommand case)
- Modify: `src/SimCore/Sim/SimWorld.Buildings.cs` (`UpdateResearch`)
- Modify: `src/SimCore/Sim/SimWorld.cs` (`Step` calls `UpdateResearch`)
- Test: `tests/SimCore.Tests/ResearchTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SimCore.Tests/ResearchTests.cs
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class ResearchTests
{
    private static readonly string[] None = System.Array.Empty<string>();

    // Faction: barracks researches "dmg1" (target marine); depot exists for a prereq test.
    private static FactionDef Faction() => new("f", "F",
        units: new[] { new UnitDef("marine", 1, "barracks", None,
            new UnitSpec(40, Fix.FromFraction(1, 2), 50, 1, 5, Weapon: new WeaponSpec(6, Fix.FromInt(2), 5))) },
        buildings: new[]
        {
            new BuildingDef("depot", 1, None, new BuildingSpec(100, 2, 2, 100, 3, IsDepot: true)),
            new BuildingDef("barracks", 1, None, new BuildingSpec(150, 2, 2, 150, 3, CanTrain: true)),
        },
        upgrades: new[]
        {
            new UpgradeDef("dmg1", 1, "barracks", None, new[] { "marine" }, UpgradeStat.Damage, Fix.FromInt(2), 50, 10),
            new UpgradeDef("dmg2", 2, "barracks", new[] { "dmg1" }, new[] { "marine" }, UpgradeStat.Damage, Fix.FromInt(2), 50, 10),
        });

    private static (SimWorld w, int rax) ReadyRax()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: Faction());
        w.Players[0].Minerals = 1000;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(0, worker, "barracks", 7, 5) });
        for (int i = 0; i < 3; i++) w.Step(System.Array.Empty<Command>());
        return (w, w.Buildings[0].Id);
    }

    [Fact]
    public void Research_Completes_And_Applies_Upgrade()
    {
        var (w, rax) = ReadyRax();
        var before = w.Players[0].Minerals;
        w.Step(new Command[] { new ResearchCommand(0, rax, "dmg1") });
        Assert.Equal(before - 50, w.Players[0].Minerals);   // charged at start
        Assert.Equal("dmg1", w.GetBuilding(rax)!.ResearchingId);
        Assert.False(w.Players[0].HasUpgrade("dmg1"));
        for (int i = 0; i < 10; i++) w.Step(System.Array.Empty<Command>());
        Assert.True(w.Players[0].HasUpgrade("dmg1"));        // applied after ResearchTicks
        Assert.Equal("", w.GetBuilding(rax)!.ResearchingId); // slot cleared
    }

    [Fact]
    public void Research_Rejected_When_ResearchedAt_Mismatch()
    {
        // build a depot too; try to research dmg1 (ResearchedAt barracks) at the depot
        var (w, rax) = ReadyRax();
        var worker = w.SpawnUnit(0, w.Map.CellCenter(15, 15), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(0, worker, "depot", 14, 14) });
        for (int i = 0; i < 3; i++) w.Step(System.Array.Empty<Command>());
        var depot = w.Buildings[^1].Id;
        w.Step(new Command[] { new ResearchCommand(0, depot, "dmg1") });
        Assert.Equal("", w.GetBuilding(depot)!.ResearchingId);
    }

    [Fact]
    public void Research_Rejected_When_Upgrade_Prereq_Missing()
    {
        var (w, rax) = ReadyRax();
        w.Step(new Command[] { new ResearchCommand(0, rax, "dmg2") }); // needs dmg1 first
        Assert.Equal("", w.GetBuilding(rax)!.ResearchingId);
    }

    [Fact]
    public void Research_Allowed_When_Upgrade_Prereq_Present()
    {
        var (w, rax) = ReadyRax();
        w.Step(new Command[] { new ResearchCommand(0, rax, "dmg1") });
        for (int i = 0; i < 10; i++) w.Step(System.Array.Empty<Command>());
        w.Step(new Command[] { new ResearchCommand(0, rax, "dmg2") });
        Assert.Equal("dmg2", w.GetBuilding(rax)!.ResearchingId);
    }

    [Fact]
    public void Cannot_Research_Same_Upgrade_Twice_Or_While_Busy()
    {
        var (w, rax) = ReadyRax();
        w.Step(new Command[] { new ResearchCommand(0, rax, "dmg1") });
        var mins = w.Players[0].Minerals;
        // already researching → second command ignored (no extra charge)
        w.Step(new Command[] { new ResearchCommand(0, rax, "dmg1") });
        Assert.Equal(mins, w.Players[0].Minerals);
        for (int i = 0; i < 10; i++) w.Step(System.Array.Empty<Command>());
        // already applied → cannot research again
        w.Step(new Command[] { new ResearchCommand(0, rax, "dmg1") });
        Assert.Equal("", w.GetBuilding(rax)!.ResearchingId);
    }

    [Fact]
    public void Research_Rejected_For_Unknown_Upgrade()
    {
        var (w, rax) = ReadyRax();
        w.Step(new Command[] { new ResearchCommand(0, rax, "nope") });
        Assert.Equal("", w.GetBuilding(rax)!.ResearchingId);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter ResearchTests`
Expected: compilation FAILS (`ResearchCommand`, `Building.ResearchingId` don't exist).

- [ ] **Step 3: Implement.**

In `src/SimCore/Sim/Building.cs`, add to the `Building` class:

```csharp
    public string ResearchingId { get; set; } = ""; // "" = idle
    public int ResearchTicksRemaining { get; set; }
```

In `src/SimCore/Sim/Commands.cs`, add:

```csharp
public sealed record ResearchCommand(int PlayerId, int BuildingId, string UpgradeDefId) : Command(PlayerId);
```

In `src/SimCore/Sim/SimWorld.cs` `Apply`, add the case:

```csharp
            case ResearchCommand rc:
                var rup = Faction?.GetUpgrade(rc.UpgradeDefId);
                if (rup is null) break; // unknown upgrade
                var rb = GetBuilding(rc.BuildingId);
                if (rb is null || rb.OwnerId != rc.PlayerId || !rb.IsComplete) break;
                if (rb.DefId != rup.ResearchedAt) break;          // wrong research building
                if (rb.ResearchingId.Length != 0) break;          // building busy
                if (_players[rc.PlayerId].HasUpgrade(rup.Id)) break; // already researched
                if (!PrerequisitesMet(rc.PlayerId, rup.Requires)) break;
                if (_players[rc.PlayerId].Minerals < rup.MineralCost) break;
                _players[rc.PlayerId].Minerals -= rup.MineralCost;
                rb.ResearchingId = rup.Id;
                rb.ResearchTicksRemaining = rup.ResearchTicks;
                break;
```

In `src/SimCore/Sim/SimWorld.Buildings.cs`, add:

```csharp
    private void UpdateResearch()
    {
        foreach (var b in _buildings)
        {
            if (b.ResearchingId.Length == 0) continue;
            b.ResearchTicksRemaining--;
            if (b.ResearchTicksRemaining > 0) continue;
            _players[b.OwnerId].AddUpgrade(b.ResearchingId);
            b.ResearchingId = "";
        }
    }
```

In `src/SimCore/Sim/SimWorld.cs` `Step`, add `UpdateResearch();` immediately after `UpdateProduction();`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: +6 tests passing. Golden unchanged — the existing determinism scenario issues no `ResearchCommand`, so `ResearchingId` stays "" and `AppliedUpgrades` stays empty (and neither is hashed yet); `UpdateResearch` is a no-op there.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/Building.cs src/SimCore/Sim/Commands.cs src/SimCore/Sim/SimWorld.cs src/SimCore/Sim/SimWorld.Buildings.cs tests/SimCore.Tests/ResearchTests.cs
git commit -m "feat: ResearchCommand + per-building research slot + UpdateResearch"
```

---

### Task 6: Effective-stat accessors + read-site refactor

**Files:**
- Create: `src/SimCore/Sim/SimWorld.Upgrades.cs`
- Modify: `src/SimCore/Sim/SimWorld.Combat.cs` (use effective damage/range/cooldown)
- Modify: `src/SimCore/Sim/SimWorld.cs` (`MoveUnits` uses effective speed)
- Modify: `src/SimCore/Sim/SimWorld.Vision.cs` (uses effective sight)
- Test: `tests/SimCore.Tests/EffectiveStatTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SimCore.Tests/EffectiveStatTests.cs
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class EffectiveStatTests
{
    private static readonly string[] None = System.Array.Empty<string>();

    private static FactionDef Faction(params UpgradeDef[] ups) => new("f", "F",
        units: new[] { new UnitDef("marine", 1, "barracks", None,
            new UnitSpec(40, Fix.FromFraction(1, 2), 50, 1, 5, Weapon: new WeaponSpec(6, Fix.FromInt(2), 5), SightRange: 7)) },
        buildings: new[] { new BuildingDef("barracks", 1, None, new BuildingSpec(150, 2, 2, 150, 3, CanTrain: true)) },
        upgrades: ups);

    private static int SpawnMarine(SimWorld w, int x, int y)
    {
        var spec = new UnitSpec(40, Fix.FromFraction(1, 2), 50, 1, 5, Weapon: new WeaponSpec(6, Fix.FromInt(2), 5), SightRange: 7);
        return w.SpawnUnit(0, w.Map.CellCenter(x, y), spec, "marine");
    }

    [Fact]
    public void No_Upgrades_Effective_Equals_Base()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: Faction());
        var u = w.GetUnit(SpawnMarine(w, 2, 2))!;
        Assert.Equal(6, w.EffectiveDamage(u));
        Assert.Equal(Fix.FromInt(2), w.EffectiveRange(u));
        Assert.Equal(5, w.EffectiveCooldownTicks(u));
        Assert.Equal(Fix.FromFraction(1, 2), w.EffectiveSpeed(u));
        Assert.Equal(7, w.EffectiveSight(u));
    }

    [Fact]
    public void Applied_Damage_Upgrade_Raises_Effective_Damage_For_Targeted_Unit()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1,
            faction: Faction(new UpgradeDef("dmg1", 1, "barracks", None, new[] { "marine" }, UpgradeStat.Damage, Fix.FromInt(3), 50, 1)));
        var u = w.GetUnit(SpawnMarine(w, 2, 2))!;
        Assert.Equal(6, w.EffectiveDamage(u));      // before
        w.Players[0].AddUpgrade("dmg1");
        Assert.Equal(9, w.EffectiveDamage(u));      // retroactive: same existing unit, now +3
    }

    [Fact]
    public void Star_Target_Affects_DefIdless_Units_And_Stacks()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1,
            faction: Faction(
                new UpgradeDef("dmg1", 1, "barracks", None, new[] { "*" }, UpgradeStat.Damage, Fix.FromInt(2), 50, 1),
                new UpgradeDef("dmg2", 1, "barracks", None, new[] { "*" }, UpgradeStat.Damage, Fix.FromInt(2), 50, 1)));
        // legacy spawn → DefId "" → only "*" upgrades apply
        var weapon = new Weapon { Damage = 6, Range = Fix.FromInt(2), CooldownTicks = 5 };
        var u = w.GetUnit(w.SpawnUnit(0, w.Map.CellCenter(2, 2), Fix.FromFraction(1, 2), 40, weapon))!;
        w.Players[0].AddUpgrade("dmg1");
        w.Players[0].AddUpgrade("dmg2");
        Assert.Equal(10, w.EffectiveDamage(u)); // 6 + 2 + 2
    }

    [Fact]
    public void Targeting_Excludes_Nonmatching_Unit_Def()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1,
            faction: Faction(new UpgradeDef("dmg1", 1, "barracks", None, new[] { "tank" }, UpgradeStat.Damage, Fix.FromInt(3), 50, 1)));
        var u = w.GetUnit(SpawnMarine(w, 2, 2))!;  // DefId "marine"
        w.Players[0].AddUpgrade("dmg1");           // targets "tank" only
        Assert.Equal(6, w.EffectiveDamage(u));     // unaffected
    }

    [Fact]
    public void Negative_Delta_Clamps_At_Floor()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1,
            faction: Faction(new UpgradeDef("slow", 1, "barracks", None, new[] { "*" }, UpgradeStat.CooldownTicks, Fix.FromInt(-100), 0, 1)));
        var u = w.GetUnit(SpawnMarine(w, 2, 2))!;
        w.Players[0].AddUpgrade("slow");
        Assert.Equal(1, w.EffectiveCooldownTicks(u)); // floored at 1
    }

    [Fact]
    public void Damage_Upgrade_Makes_Combat_Kill_Faster()
    {
        // Attacker with +damage kills a dummy in fewer ticks than base.
        FactionDef F(bool withUp) => new("f", "F",
            units: System.Array.Empty<UnitDef>(),
            buildings: System.Array.Empty<BuildingDef>(),
            upgrades: withUp
                ? new[] { new UpgradeDef("dmg", 1, "x", None, new[] { "*" }, UpgradeStat.Damage, Fix.FromInt(50), 0, 1) }
                : System.Array.Empty<UpgradeDef>());

        int TicksToKill(bool withUp)
        {
            var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: F(withUp));
            var weapon = new Weapon { Damage = 5, Range = Fix.FromInt(3), CooldownTicks = 2 };
            var atk = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, weapon);
            var vic = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 100);
            if (withUp) w.Players[0].AddUpgrade("dmg");
            w.Step(new Command[] { new AttackCommand(0, new[] { atk }, vic) });
            int t = 1;
            while (w.GetUnit(vic) is not null && t < 500) { w.Step(System.Array.Empty<Command>()); t++; }
            return t;
        }

        Assert.True(TicksToKill(true) < TicksToKill(false), "damage upgrade should kill faster");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter EffectiveStatTests`
Expected: compilation FAILS (the `Effective*` accessors don't exist).

- [ ] **Step 3: Implement.**

Create `src/SimCore/Sim/SimWorld.Upgrades.cs`:

```csharp
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
```

In `src/SimCore/Sim/SimWorld.Combat.cs`, replace base-stat reads with effective accessors (read the file first; replace each occurrence):
- Every `u.Weapon.Range` → `EffectiveRange(u)` (the two acquire calls `u.Weapon.Range + AcquireBonus`, the two leashes `u.Weapon.Range + LeashBonus`, and the in-range check `u.Weapon.Range * u.Weapon.Range`).
- `u.Weapon.Damage` (the two damage-dealing lines) → `EffectiveDamage(u)`.
- `u.Weapon.CooldownTicks` (the cooldown reset) → `EffectiveCooldownTicks(u)`.
- Leave `u.Weapon.CooldownRemaining` reads/writes unchanged (live counter, not upgradable).

In `src/SimCore/Sim/SimWorld.cs` `MoveUnits`, replace the two `u.SpeedPerTick` reads in the dist/step block. Read the unit's effective speed once at the top of the per-unit body, e.g. after `if (_swappedThisTick.Contains(u.Id)) continue;` add `var spd = EffectiveSpeed(u);`, then use `spd` in place of `u.SpeedPerTick` in `if (dist <= spd)` and `step.Normalized() * spd`.

In `src/SimCore/Sim/SimWorld.Vision.cs` `UpdateVision`, change the unit stamp to use effective sight:
```csharp
            Stamp(u.OwnerId, cx, cy, EffectiveSight(u));
```
(Buildings keep `b.Spec.SightRange` — upgrades target units only.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: +6 tests passing; all existing tests still pass. **Golden unchanged** — every existing test/scenario has empty `AppliedUpgrades`, so every `Effective*` returns the base value and behavior is byte-identical. If the golden changes here, the refactor is not equivalent (e.g. a missed `Range` site, or an off-by-one in a floor) — debug; do NOT re-pin.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/SimWorld.Upgrades.cs src/SimCore/Sim/SimWorld.Combat.cs src/SimCore/Sim/SimWorld.cs src/SimCore/Sim/SimWorld.Vision.cs tests/SimCore.Tests/EffectiveStatTests.cs
git commit -m "feat: compute-on-read effective stats; combat/move/vision read them"
```

---

### Task 7: StateHasher v4 + determinism scenario v6 (one re-pin)

**Files:**
- Modify: `src/SimCore/Sim/StateHasher.cs`
- Modify: `tests/SimCore.Tests/DeterminismTests.cs`

- [ ] **Step 1: Write the failing hashing tests** — append to an existing or new file `tests/SimCore.Tests/UpgradeHashTests.cs`:

```csharp
// tests/SimCore.Tests/UpgradeHashTests.cs
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class UpgradeHashTests
{
    private static SimWorld World()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 5, faction: TestFactions.Standard);
        w.SpawnUnit(0, FixVec.FromInts(1, 1), Fix.FromFraction(1, 2), 50);
        w.SpawnUnit(1, FixVec.FromInts(5, 5), Fix.FromFraction(1, 2), 80);
        return w;
    }

    [Fact]
    public void Applied_Upgrade_Changes_Hash()
    {
        var a = World();
        var b = World();
        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
        b.Players[0].AddUpgrade("dmg1");
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Research_Slot_Changes_Hash()
    {
        var a = World();
        var b = World();
        a.SpawnUnit(0, FixVec.FromInts(2, 2), Fix.FromInt(1), 10); // keep counts equal via same op on both
        b.SpawnUnit(0, FixVec.FromInts(2, 2), Fix.FromInt(1), 10);
        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
        // Place a building on b and set a research slot; placing on both keeps parity, then mutate one.
        b.PlaceBuilding(0, TestFactions.BarracksSpec, 8, 8, "barracks");
        a.PlaceBuilding(0, TestFactions.BarracksSpec, 8, 8, "barracks");
        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
        b.Buildings[^1].ResearchingId = "dmg1";
        b.Buildings[^1].ResearchTicksRemaining = 7;
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter UpgradeHashTests`
Expected: both FAIL (applied upgrades + research slot not yet hashed).

- [ ] **Step 3: Extend StateHasher.** In `src/SimCore/Sim/StateHasher.cs`:

In the per-building loop (after the rally fields), add the research slot:
```csharp
            h = Mix(h, (ulong)b.ResearchingId.Length);
            foreach (var ch in b.ResearchingId) h = Mix(h, ch);
            h = Mix(h, (ulong)b.ResearchTicksRemaining);
```
In the per-player loop (after SupplyCap), add applied upgrades:
```csharp
            h = Mix(h, (ulong)p.AppliedUpgrades.Count);
            foreach (var up in p.AppliedUpgrades) // already sorted → deterministic
            {
                h = Mix(h, (ulong)up.Length);
                foreach (var ch in up) h = Mix(h, ch);
            }
```
Update the convention doc-comment: note `Unit.DefId` and `TrainingItem.UnitDefId` are excluded as derived static labels (1:1 with already-hashed spec), and that applied upgrades + research slot are now folded.

- [ ] **Step 4: Add research to the determinism scenario.** In `tests/SimCore.Tests/DeterminismTests.cs` `Scenario()`:

Add an upgrade to `scenarioFaction` (extend the `upgrades:` argument — the rax already exists as a building def):
```csharp
        var scenarioFaction = new FactionDef("scenario", "Scenario",
            units: new[] { new UnitDef("marine", 1, "rax", System.Array.Empty<string>(), marineSpec) },
            buildings: new[]
            {
                new BuildingDef("depot", 1, System.Array.Empty<string>(), depotSpec),
                new BuildingDef("rax", 1, System.Array.Empty<string>(), raxSpec),
            },
            upgrades: new[]
            {
                new UpgradeDef("dmg1", 1, "rax", System.Array.Empty<string>(), new[] { "*" },
                    UpgradeStat.Damage, Fix.FromInt(3), 50, 20),
            });
```
Bump player 0 minerals so research is affordable alongside the existing economy: change `w.Players[0].Minerals = 400;` to `w.Players[0].Minerals = 500;`. Add a research command to the script after the rax is complete and after the t=250 trains (rax built t=80, BuildTime 40 → complete ~t=120):
```csharp
            [300] = new() { new ResearchCommand(0, RaxId, "dmg1") },
```
This boosts every player-0 unit's damage by +3 from ~t=320, changing combat outcomes for the remainder — a real retroactive effect under the hashed trajectory.

- [ ] **Step 5: Re-pin and verify (both configs).**

Run: `dotnet test --filter DeterminismTests`
Expected: the two replay tests PASS (if either fails, real nondeterminism — debug, BLOCKED if unresolved). `Trajectory_Hash_Matches_Golden_Constant` FAILS (hash function grew AND the scenario now researches an upgrade — both intended). Copy the actual value into `GoldenTrajectoryHash`. Re-run `dotnet test` and `dotnet test --configuration Release` — all pass, identical constant in both.

Verify (temporary instrumentation, then remove): the research completes (player 0 `HasUpgrade("dmg1")` true by ~t=320) and the upgrade actually applies (a player-0 unit's `EffectiveDamage` is base+3 after t=320). Confirm `git status` clean of scratch files.

- [ ] **Step 6: Commit**

```bash
git add src/SimCore/Sim/StateHasher.cs tests/SimCore.Tests/UpgradeHashTests.cs tests/SimCore.Tests/DeterminismTests.cs
git commit -m "feat: StateHasher v4 (applied upgrades + research slot); scenario v6 researches mid-run (re-pin golden)"
```

---

### Task 8: Godot research buttons

**Files:**
- Modify: `godot/scripts/Hud.cs` (research button row)

- [ ] **Step 1: Survey + read.** Run:
```bash
cd /c/Users/lssha/llm-rts && grep -n "TrainCommand\|UnitList\|ResearchedAt\|DefId\|Faction" godot/scripts/Hud.cs
```
Read `Hud.cs` to find the train-button block (added in 3a) — the research row mirrors it.

- [ ] **Step 2: Add a research button row.** When a building is selected, alongside its train buttons, add a button per researchable upgrade for that building:
```csharp
// inside the selected-building UI block, after train buttons:
if (w.Faction is { } rf)
    foreach (var udef in rf.UpgradeList.Where(u => u.ResearchedAt == b.DefId
                                                && !w.Players[p].HasUpgrade(u.Id)))
    {
        var capturedUp = udef;
        AddButton($"Research {capturedUp.Id} ({capturedUp.MineralCost})",
            () => _runner.Enqueue(new ResearchCommand(p, b.Id, capturedUp.Id)));
    }
```
Use `capturedUp.MineralCost` for the label cost (UpgradeDef has `MineralCost`, not `Spec`). Match the file's existing button helper/signature and `using System.Linq;` (add if absent). Filter out already-applied upgrades. Keep it minimal — a row of research buttons, same pattern as train.

- [ ] **Step 3: Build the Godot project.**

Run: `dotnet build godot/LlmRts.Godot.csproj -c Debug`
Expected: 0 errors. Then `dotnet test` to confirm SimCore unaffected (no src/SimCore or tests changes here).

- [ ] **Step 4: Commit**

```bash
git add godot/scripts/Hud.cs
git commit -m "feat: Godot research buttons from the upgrades catalog"
```

---

## Done Criteria

- `dotnet test --configuration Release` passes (~208 tests: 183 + ~25 new) including the determinism scenario that researches an upgrade mid-run.
- Upgrades are catalogued in `FactionDef`, validated, researchable via `ResearchCommand`, and retroactively affect combat/movement/vision via effective-stat accessors.
- Only new hashed state: per-player `AppliedUpgrades` + per-building research slot. `Unit.DefId`/`TrainingItem.UnitDefId` documented as unhashed labels.
- Godot builds and offers research buttons.
- `grep -rni godot src/SimCore` → no hits; no float/double in src/SimCore outside `Fix.ToString()`.

**Next plan (3c):** Faction-mechanic framework + 1 exemplar — a pluggable hook in the deterministic Step loop and one concrete mechanic (e.g. regenerating shields), extending `FactionDef` with a mechanic selector.

## Plan-3c Inputs (carried forward from 3b final review — STATUS: 3b COMPLETE, merged 2026-06-13, ~210 SimCore tests, golden 6959374437731592347UL)

The seams favor a pluggable mechanic; nothing fights it:
1. **`Step()` is a clean ordered pipeline** — slot a `UpdateMechanics()` hook in deliberately (likely after UpdateProduction/UpdateResearch, before RemoveDead); hook ordering affects the hash, so choose it consciously and document.
2. **`FactionDef` additive-ctor precedent** — add a `mechanic` selector via a new ctor overload (as upgrades were added), keeping all existing `FactionDef(...)` call sites compiling.
3. **`Validate()`** is the home for mechanic referential checks (extend the existing walk).
4. **StateHasher convention is explicit** — any mechanic-introduced mutable per-player/per-unit state (cooldowns, charges, shield HP) MUST be folded into the hash and the golden re-pinned in the same commit. Per-player mechanic state goes in the `world.Players` hash loop.
5. **Prefer compute-on-read** (the `UpgradeStat`/`UpgradeDelta` template) for stat-modifying mechanics — keeps retroactivity + hash simplicity, avoids baked per-unit state.
6. **Reuse `Has()`/`PrerequisitesMet`** if a mechanic gates on tech (building or upgrade).
7. **Player-triggered mechanics** drop into the sealed-record `Command` + single `Apply()` switch with the guard-then-mutate pattern (mirror `ResearchCommand`).

PROCESS NOTE (cost a recovery in 3b): a background implementer's commit was silently lost (branch advanced from a sibling commit), requiring a cherry-pick of orphaned work. Mitigation adopted: run implementers FOREGROUND (synchronous) and verify `git log` HEAD after each task. Keep doing this for 3c/3d.
