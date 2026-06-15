# CPU Opponent Framework + Easy Tier Implementation Plan (5c)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a deterministic in-sim CPU opponent (per-player controller/difficulty + an `UpdateAi` Step phase) and the SC2-style Easy tier (light macro + supply + cheapest-combat army + occasional weak attack), keyed off faction-catalog roles.

**Architecture:** A new `UpdateAi()` phase in `Step` runs each CPU player's `EasyDecide` on a fixed cadence; decisions read hashed state and issue `Command`s through the existing `Apply` path. Easy is stateless (recomputed each decision tick), so only constant `Controller`/`Difficulty` are added to `PlayerState` + `StateHasher` (v7, golden re-pin). Roles (worker/combat/supply/producer) are derived from `FactionFor(p)`, so it works for any pack.

**Tech Stack:** C# / .NET 8, xUnit. SimCore (deterministic core) — integer/Fix only, no RNG (deterministic scans), commands are the only mutation path.

**Source spec:** `docs/superpowers/specs/2026-06-14-cpu-framework-easy-tier-design.md`

---

## Conventions for every task

- Run from repo root `C:\Users\lssha\llm-rts`. If `dotnet` missing: bash `export PATH="$PATH:/c/Program Files/dotnet"`.
- Run SimCore tests: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
- **Baseline:** 297 SimCore tests pass; golden trajectory hash = `9352778236967924871UL` (re-pinned in Task 1; unchanged thereafter).
- After each commit, confirm `git log --oneline -1`. End commit messages with `Co-Authored-By: RuFlo <ruv@ruv.net>`.

## Engine facts (verified against source)

- `Step` pipeline (`SimWorld.cs:88`): `EnsureOccupancy → UpdateVision → Apply(commands) → UpdateCombat → MoveUnits → UpdateHarvest → UpdateConstruction → UpdateProduction → UpdateResearch → UpdateShields → RemoveDead → RemoveDeadBuildings → UpdateMatchState → Tick++`. `Apply(Command)` is a private method (`SimWorld.cs:106`).
- `PlayerState` (`PlayerState.cs`): `Minerals`, `SupplyUsed`, `SupplyCap` (all `int {get;set;}`). `SimWorld._players` (`PlayerState[]`), `Players` public; `FactionFor(int)` (5b). `Tick` is `public int { get; private set; }`.
- `Unit` fields: `OwnerId`, `Position` (`FixVec`), `Harvester` (`HarvesterSpec?`), `Weapon` (`Weapon?`), `HarvestPhase` (`enum {None,MovingToNode,Gathering,Returning}`), `HasMoveOrder`, `AttackTargetId` (0 = none), `Id`.
- `Building` fields: `Id`, `OwnerId`, `IsComplete`, `DefId`, `Spec` (`BuildingSpec`), `CellX`/`CellY`. `_buildings` (`List<Building>`, stable), `CenterOf(Building)` (internal, `SimWorld.Buildings.cs:16`), `FootprintCenter(x,y,w,h)` (internal static), `FootprintPlaceable(x,y,w,h)` (private, `SimWorld.Buildings.cs:18`).
- `ResourceNode`: `Id`, `CellX`, `CellY`, `Remaining`. `_nodes` (private), `Nodes` public, `GetNode(id)`, `AddResourceNode(x,y,amount)` (`SimWorld.Economy.cs`).
- `FactionDef`: `UnitList` (`IReadOnlyList<UnitDef>`), `BuildingList`. `UnitDef(Id, Tier, ProducedBy, Requires, Spec)` where `UnitSpec` has `MaxHp, Speed, MineralCost, SupplyCost, BuildTimeTicks, Weapon?, Harvester?, SightRange`. `BuildingDef(Id, Tier, Requires, Spec)` where `BuildingSpec` has `MaxHp, Width, Height, MineralCost, BuildTimeTicks, SupplyProvided, IsDepot, CanTrain, SightRange`.
- Commands: `TrainCommand(PlayerId, BuildingId, UnitDefId)`, `HarvestCommand(PlayerId, int[] UnitIds, NodeId)`, `BuildCommand(PlayerId, WorkerUnitId, BuildingDefId, CellX, CellY)`, `AttackMoveCommand(PlayerId, int[] UnitIds, FixVec Target)`.
- `FixVec` has `LengthSquared()` returning `Fix` (compare via `.Raw`); `Map.CellCenter(x,y)`, `Map.WorldToCell(pos)`. `Fix.FromInt(16)` = build-range² (BuildCommand rejects beyond `LengthSquared > Fix.FromInt(16)`).
- `ReferenceSpecs` (public static): `Depot`, `Barracks` (BuildingSpec), `Fabber` (worker UnitSpec). `ReferenceFaction.Def`. `StateHasher.Hash` player loop at `StateHasher.cs:86-97`; golden constant `DeterminismTests.cs:238`.

## File Structure

- `src/SimCore/Sim/PlayerState.cs` — MODIFY: `Controller`/`Difficulty` fields + the two enums (Task 1).
- `src/SimCore/Sim/SimWorld.Ai.cs` — NEW: `SetCpu`, `UpdateAi`, `EasyDecide`, role/scan helpers, AI constants.
- `src/SimCore/Sim/SimWorld.cs` — MODIFY: call `UpdateAi()` in `Step` (Task 1).
- `src/SimCore/Sim/StateHasher.cs` — MODIFY: fold Controller+Difficulty (Task 1).
- `tests/SimCore.Tests/DeterminismTests.cs` — MODIFY: golden re-pin (Task 1).
- `tests/SimCore.Tests/CpuAiTests.cs` — NEW: across Tasks 1-4.

---

## Task 1: Framework — controller/difficulty + UpdateAi phase + hash v7 + golden re-pin

**Files:**
- Modify: `src/SimCore/Sim/PlayerState.cs`
- Create: `src/SimCore/Sim/SimWorld.Ai.cs`
- Modify: `src/SimCore/Sim/SimWorld.cs`, `src/SimCore/Sim/StateHasher.cs`
- Modify: `tests/SimCore.Tests/DeterminismTests.cs`
- Test: `tests/SimCore.Tests/CpuAiTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/SimCore.Tests/CpuAiTests.cs`:

```csharp
using System.Collections.Generic;
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class CpuAiTests
{
    private static readonly List<Command> Empty = new();

    [Fact]
    public void SetCpu_Sets_Controller_And_Difficulty()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        Assert.Equal(PlayerController.Human, w.Players[0].Controller); // default
        w.SetCpu(1, AiDifficulty.Easy);
        Assert.Equal(PlayerController.Cpu, w.Players[1].Controller);
        Assert.Equal(AiDifficulty.Easy, w.Players[1].Difficulty);
        Assert.Equal(PlayerController.Human, w.Players[0].Controller);
    }

    [Fact]
    public void Cpu_With_No_Assets_Does_Not_Crash_Or_Spawn()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, new FactionDef?[] { ReferenceFaction.Def, ReferenceFaction.Def });
        w.SetCpu(1, AiDifficulty.Easy);
        for (int t = 0; t < 30; t++) w.Step(Empty); // no buildings/workers → AI finds no producer; no throw
        Assert.Empty(w.Units);
    }
}
```

- [ ] **Step 2: Run, expect failure** — `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`. Compile FAILS — `PlayerController`/`AiDifficulty`/`SetCpu`/`PlayerState.Controller` missing.

- [ ] **Step 3: Add the enums + PlayerState fields**

In `src/SimCore/Sim/PlayerState.cs`, add the enums above the class and the two fields inside it:

```csharp
public enum PlayerController { Human, Cpu }
public enum AiDifficulty { Easy, Medium, Hard }
```

Inside `PlayerState` (after `SupplyCap`):

```csharp
    public PlayerController Controller { get; set; } = PlayerController.Human;
    public AiDifficulty Difficulty { get; set; } = AiDifficulty.Easy; // only meaningful when Controller == Cpu
```

- [ ] **Step 4: Create the AI partial (framework + stub)**

Create `src/SimCore/Sim/SimWorld.Ai.cs`:

```csharp
using SimCore.Math;

namespace SimCore.Sim;

public sealed partial class SimWorld
{
    // Easy-tier tunables (first-pass; tuned later).
    private const int AiDecisionInterval = 10;   // act roughly every 10 ticks
    private const int EasyWorkerCap = 8;
    private const int EasySupplyBuffer = 2;       // build supply when SupplyUsed >= SupplyCap - this
    private const int EasyAttackThreshold = 6;    // min combat units before attacking
    private const int EasyAttackInterval = 300;   // attack-move cadence (ticks)

    /// <summary>Mark a player as CPU-controlled at the given difficulty (setup-time).</summary>
    public void SetCpu(int playerId, AiDifficulty difficulty)
    {
        _players[playerId].Controller = PlayerController.Cpu;
        _players[playerId].Difficulty = difficulty;
    }

    /// <summary>Deterministic AI phase: each CPU player decides on a fixed cadence and issues
    /// commands through Apply. Integer/Fix only, no RNG (stable scans). Skips once the match is
    /// decided.</summary>
    private void UpdateAi()
    {
        if (Phase == MatchPhase.Over) return;
        if (Tick % AiDecisionInterval != 0) return;
        for (int p = 0; p < _players.Length; p++)
        {
            if (_players[p].Controller != PlayerController.Cpu) continue;
            switch (_players[p].Difficulty)
            {
                default: EasyDecide(p); break; // Medium/Hard fall back to Easy until 5d
            }
        }
    }

    /// <summary>Easy tier: stub in Task 1 (does nothing). Filled in Tasks 2-3.</summary>
    private void EasyDecide(int playerId)
    {
    }
}
```

- [ ] **Step 5: Wire `UpdateAi()` into `Step`**

In `src/SimCore/Sim/SimWorld.cs`, in `Step`, insert `UpdateAi();` immediately after the command-application loop and before `UpdateCombat();`:

```csharp
        foreach (var cmd in commands) Apply(cmd);
        UpdateAi();
        UpdateCombat();
```

- [ ] **Step 6: Fold controller + difficulty into the hash (v7)**

In `src/SimCore/Sim/StateHasher.cs`, inside the `foreach (var p in world.Players)` loop (after the `SupplyCap` mix, before the AppliedUpgrades block), add:

```csharp
            h = Mix(h, (ulong)(int)p.Controller);
            h = Mix(h, (ulong)(int)p.Difficulty);
```

Extend the convention doc-comment with:

```
    /// PlayerState.Controller + Difficulty ARE hashed (v7, Plan 5c) — constant config, folded
    /// per player after SupplyCap.
```

- [ ] **Step 7: Run determinism, capture new golden, verify counterfactually, re-pin**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q --filter "FullyQualifiedName~DeterminismTests"`
Expected: the two replay tests PASS; `Trajectory_Hash_Matches_Golden_Constant` FAILS showing `Expected: 9352778236967924871, Actual: <NEW>`. Record `<NEW>`.

Counterfactual: comment out the two new `Mix` lines from Step 6, rerun — `Actual` must revert to `9352778236967924871`. Restore the two lines.

Then set `DeterminismTests.cs` `GoldenTrajectoryHash` to `<NEW>UL`, and add a one-line note to the `Scenario()` doc-comment:

```
    ///   Plan-5c: PlayerState.Controller/Difficulty now hashed; this scenario is all-Human
    ///   (no CPU), so both fold as constant 0 — additive re-pin only, no AI commands.
```

- [ ] **Step 8: Run full suite (Release + Debug)**

Run: `dotnet test --configuration Release --nologo -v q` then `dotnet test --configuration Debug --nologo -v q`
Expected: PASS — SimCore.Tests 297 + 2 = 299, SpriteSlicer.Tests 6, 0 failures; determinism green with the new golden; Debug == Release.

- [ ] **Step 9: Commit**

```bash
git add src/SimCore/Sim/PlayerState.cs src/SimCore/Sim/SimWorld.Ai.cs src/SimCore/Sim/SimWorld.cs src/SimCore/Sim/StateHasher.cs tests/SimCore.Tests/DeterminismTests.cs tests/SimCore.Tests/CpuAiTests.cs
git commit -m "feat(sim): CPU controller/difficulty + UpdateAi phase; StateHasher v7 (re-pin golden)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 2: Easy economy — workers, harvest, supply + role helpers

**Files:**
- Modify: `src/SimCore/Sim/SimWorld.Ai.cs`
- Test: `tests/SimCore.Tests/CpuAiTests.cs` (add)

- [ ] **Step 1: Write the failing tests**

Add to `CpuAiTests.cs` (inside the class):

```csharp
    // A CPU player (id 1) with a depot+barracks+fabber, a node, and minerals.
    // Player 0 gets a lone depot too, so the match stays InProgress (otherwise it latches
    // Over at tick 0 and UpdateAi's Phase==Over guard would stop the CPU after one tick).
    private static SimWorld EasyEcoWorld()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 3, new FactionDef?[] { ReferenceFaction.Def, ReferenceFaction.Def });
        w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 3, 3, "depot"); // keep player 0 alive → match InProgress
        w.Players[1].Minerals = 1000;
        w.AddCompletedBuilding(1, ReferenceSpecs.Depot, 30, 30, "depot");
        w.AddCompletedBuilding(1, ReferenceSpecs.Barracks, 33, 30, "barracks");
        w.SpawnUnit(1, w.Map.CellCenter(30, 28), ReferenceSpecs.Fabber, "fabber");
        w.AddResourceNode(28, 28, 1000);
        w.SetCpu(1, AiDifficulty.Easy);
        return w;
    }

    private static int Workers(SimWorld w, int p)
    {
        int c = 0; foreach (var u in w.Units) if (u.OwnerId == p && u.Harvester is not null) c++; return c;
    }

    [Fact]
    public void Easy_Trains_Workers_Up_To_Cap()
    {
        var w = EasyEcoWorld();
        int start = Workers(w, 1);
        for (int t = 0; t < 1200; t++) w.Step(Empty);
        int now = Workers(w, 1);
        Assert.True(now > start, $"expected CPU to train workers (start {start}, now {now})");
        Assert.True(now <= 8, $"worker cap is 8, got {now}");
    }

    [Fact]
    public void Easy_Harvests_So_Minerals_Are_Spent_And_Earned()
    {
        var w = EasyEcoWorld();
        // Run long enough to see harvest income (workers deliver minerals back).
        for (int t = 0; t < 600; t++) w.Step(Empty);
        // At least one worker is in a harvest phase (assigned to the node).
        bool anyHarvesting = false;
        foreach (var u in w.Units)
            if (u.OwnerId == 1 && u.Harvester is not null && u.HarvestPhase != HarvestPhase.None) anyHarvesting = true;
        Assert.True(anyHarvesting, "expected CPU workers to be harvesting");
    }

    [Fact]
    public void Easy_Builds_Supply_When_Blocked()
    {
        var w = EasyEcoWorld();
        // Depot gives 8 supply; workers cost 1 each. Drive toward the cap so the AI builds supply.
        for (int t = 0; t < 2000; t++) w.Step(Empty);
        int depots = 0;
        foreach (var b in w.Buildings) if (b.OwnerId == 1 && b.Spec.SupplyProvided > 0) depots++;
        Assert.True(depots >= 2, $"expected CPU to build at least one extra supply building, total {depots}");
    }
```

- [ ] **Step 2: Run, expect failure** — the three new tests fail (EasyDecide is still a stub).

- [ ] **Step 3: Implement role helpers + economy decisions**

In `src/SimCore/Sim/SimWorld.Ai.cs`, add these helpers to the class:

```csharp
    // --- role resolution (from the acting player's faction catalog) ---
    private UnitDef? WorkerDef(int p)
    {
        var f = FactionFor(p);
        if (f is null) return null;
        foreach (var u in f.UnitList) if (u.Spec.Harvester is not null) return u;
        return null;
    }

    private UnitDef? CombatDef(int p)
    {
        var f = FactionFor(p);
        if (f is null) return null;
        UnitDef? best = null;
        foreach (var u in f.UnitList)
            if (u.Spec.Weapon is not null && (best is null || u.Spec.MineralCost < best.Spec.MineralCost))
                best = u;
        return best;
    }

    private BuildingDef? SupplyDef(int p)
    {
        var f = FactionFor(p);
        if (f is null) return null;
        foreach (var b in f.BuildingList) if (b.Spec.SupplyProvided > 0) return b;
        return null;
    }

    /// <summary>Id of a complete, train-capable building of p that produces the given def id; 0 if none.</summary>
    private int ProducerBuildingId(int p, string producedByDefId)
    {
        foreach (var b in _buildings)
            if (b.OwnerId == p && b.IsComplete && b.Spec.CanTrain && b.DefId == producedByDefId) return b.Id;
        return 0;
    }

    private int CountUnits(int p, bool combat)
    {
        int c = 0;
        foreach (var u in _units)
            if (u.OwnerId == p && (combat ? u.Weapon is not null : u.Harvester is not null)) c++;
        return c;
    }

    /// <summary>First idle worker of p (no harvest/move/attack order); null if none.</summary>
    private Unit? IdleWorker(int p)
    {
        foreach (var u in _units)
            if (u.OwnerId == p && u.Harvester is not null
                && u.HarvestPhase == HarvestPhase.None && !u.HasMoveOrder && u.AttackTargetId == 0)
                return u;
        return null;
    }

    /// <summary>Nearest non-empty node to a position (squared distance; first wins ties); 0 if none.</summary>
    private int NearestNodeId(FixVec pos)
    {
        int best = 0;
        long bestSq = long.MaxValue;
        foreach (var n in _nodes)
        {
            if (n.Remaining <= 0) continue;
            long sq = (Map.CellCenter(n.CellX, n.CellY) - pos).LengthSquared().Raw;
            if (sq < bestSq) { bestSq = sq; best = n.Id; }
        }
        return best;
    }

    /// <summary>First placeable wxh footprint near pos (ring scan) within build range of pos; null if none.</summary>
    private (int x, int y)? FreeFootprintNear(FixVec pos, int w, int h)
    {
        var (cx, cy) = Map.WorldToCell(pos);
        for (int r = 1; r <= 4; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (!FootprintPlaceable(x, y, w, h)) continue;
                    if ((pos - FootprintCenter(x, y, w, h)).LengthSquared() <= Fix.FromInt(16)) return (x, y);
                }
        return null;
    }
```

Then replace the `EasyDecide` stub with the economy steps:

```csharp
    private void EasyDecide(int playerId)
    {
        var ps = _players[playerId];

        // 1. Train workers up to the cap.
        var wdef = WorkerDef(playerId);
        if (wdef is not null && CountUnits(playerId, combat: false) < EasyWorkerCap)
        {
            int prod = ProducerBuildingId(playerId, wdef.ProducedBy);
            if (prod != 0) Apply(new TrainCommand(playerId, prod, wdef.Id));
        }

        // 2. Send every idle worker to the nearest node.
        var idleIds = new System.Collections.Generic.List<int>();
        foreach (var u in _units)
            if (u.OwnerId == playerId && u.Harvester is not null
                && u.HarvestPhase == HarvestPhase.None && !u.HasMoveOrder && u.AttackTargetId == 0)
                idleIds.Add(u.Id);
        foreach (var id in idleIds)
        {
            var u = GetUnit(id);
            if (u is null) continue;
            int node = NearestNodeId(u.Position);
            if (node != 0) Apply(new HarvestCommand(playerId, new[] { id }, node));
        }

        // 3. Build a supply building when near the cap.
        var sdef = SupplyDef(playerId);
        if (sdef is not null && ps.SupplyUsed >= ps.SupplyCap - EasySupplyBuffer
            && ps.Minerals >= sdef.Spec.MineralCost)
        {
            var bw = IdleWorker(playerId);
            if (bw is not null)
            {
                var cell = FreeFootprintNear(bw.Position, sdef.Spec.Width, sdef.Spec.Height);
                if (cell is not null) Apply(new BuildCommand(playerId, bw.Id, sdef.Id, cell.Value.x, cell.Value.y));
            }
        }
    }
```

- [ ] **Step 4: Run, expect pass** — `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`. Expected: 299 + 3 = 302; the determinism golden is UNCHANGED (the human-only scenario issues no AI commands).

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/SimWorld.Ai.cs tests/SimCore.Tests/CpuAiTests.cs
git commit -m "feat(sim): Easy CPU economy — train workers, harvest, build supply (catalog roles)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 3: Easy army + occasional weak attack

**Files:**
- Modify: `src/SimCore/Sim/SimWorld.Ai.cs`
- Test: `tests/SimCore.Tests/CpuAiTests.cs` (add)

- [ ] **Step 1: Write the failing tests**

Add to `CpuAiTests.cs` (inside the class):

```csharp
    [Fact]
    public void Easy_Trains_Combat_Units()
    {
        var w = EasyEcoWorld();
        for (int t = 0; t < 1500; t++) w.Step(Empty);
        int combat = 0;
        foreach (var u in w.Units) if (u.OwnerId == 1 && u.Weapon is not null) combat++;
        Assert.True(combat > 0, "expected CPU to train combat units from the barracks");
    }

    [Fact]
    public void Easy_Attacks_Enemy_Base_Once_Army_Is_Built()
    {
        // Player 0 (human) has a building to be the attack target; player 1 is the CPU.
        var w = new SimWorld(new MapGrid(40, 40), seed: 5, new FactionDef?[] { ReferenceFaction.Def, ReferenceFaction.Def });
        w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 3, 3, "depot"); // human base (attack target)
        w.Players[1].Minerals = 5000;
        w.AddCompletedBuilding(1, ReferenceSpecs.Depot, 30, 30, "depot");
        w.AddCompletedBuilding(1, ReferenceSpecs.Barracks, 33, 30, "barracks");
        w.SpawnUnit(1, w.Map.CellCenter(30, 28), ReferenceSpecs.Fabber, "fabber");
        w.AddResourceNode(28, 28, 5000);
        w.SetCpu(1, AiDifficulty.Easy);

        for (int t = 0; t < 2000; t++) w.Step(Empty);

        // Once the CPU has >= threshold combat units, an attack tick issues an attack-move toward
        // the human base — at least one CPU combat unit should be attack-moving (or moving west).
        bool attacking = false;
        foreach (var u in w.Units)
            if (u.OwnerId == 1 && u.Weapon is not null && (u.IsAttackMoving || u.HasMoveOrder)) attacking = true;
        Assert.True(attacking, "expected CPU combat units to be attack-moving toward the enemy base");
    }
```

- [ ] **Step 2: Run, expect failure** — the two new tests fail (EasyDecide has no army/attack steps).

- [ ] **Step 3: Add army + attack helpers and steps**

In `src/SimCore/Sim/SimWorld.Ai.cs`, add two helpers:

```csharp
    /// <summary>Center of the first enemy building (lowest id, stable order); null if none.</summary>
    private FixVec? EnemyBaseCenter(int p)
    {
        foreach (var b in _buildings) if (b.OwnerId != p) return CenterOf(b);
        return null;
    }

    private int[] CombatUnitIds(int p)
    {
        var ids = new System.Collections.Generic.List<int>();
        foreach (var u in _units) if (u.OwnerId == p && u.Weapon is not null) ids.Add(u.Id);
        return ids.ToArray();
    }
```

Then append steps 4-5 to `EasyDecide` (after the supply block, before the method's closing brace):

```csharp
        // 4. Train the cheapest combat unit when supply/minerals allow.
        var cdef = CombatDef(playerId);
        if (cdef is not null)
        {
            int prod = ProducerBuildingId(playerId, cdef.ProducedBy);
            if (prod != 0 && ps.Minerals >= cdef.Spec.MineralCost
                && ps.SupplyUsed + cdef.Spec.SupplyCost <= ps.SupplyCap)
                Apply(new TrainCommand(playerId, prod, cdef.Id));
        }

        // 5. Occasionally attack-move the whole army at the enemy base.
        if (CountUnits(playerId, combat: true) >= EasyAttackThreshold && Tick % EasyAttackInterval == 0)
        {
            var target = EnemyBaseCenter(playerId);
            if (target is not null)
            {
                var army = CombatUnitIds(playerId);
                if (army.Length > 0) Apply(new AttackMoveCommand(playerId, army, target.Value));
            }
        }
```

- [ ] **Step 4: Run, expect pass** — `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`. Expected: 302 + 2 = 304; golden unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/SimWorld.Ai.cs tests/SimCore.Tests/CpuAiTests.cs
git commit -m "feat(sim): Easy CPU trains army + occasional attack-move at enemy base

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 4: CPU determinism proof + full gate + 5d inputs

**Files:**
- Test: `tests/SimCore.Tests/CpuAiTests.cs` (add)
- Modify: `docs/superpowers/plans/2026-06-14-cpu-framework-easy-tier.md` (append note)

- [ ] **Step 1: Write the CPU-determinism test**

Add to `CpuAiTests.cs` (inside the class):

```csharp
    private static SimWorld CpuVsCpuWorld()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 7, new FactionDef?[] { ReferenceFaction.Def, ReferenceFaction.Def });
        w.Players[0].Minerals = 800;
        w.Players[1].Minerals = 800;
        w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 3, 3, "depot");
        w.AddCompletedBuilding(0, ReferenceSpecs.Barracks, 6, 3, "barracks");
        w.AddCompletedBuilding(1, ReferenceSpecs.Depot, 34, 34, "depot");
        w.AddCompletedBuilding(1, ReferenceSpecs.Barracks, 31, 34, "barracks");
        w.SpawnUnit(0, w.Map.CellCenter(5, 6), ReferenceSpecs.Fabber, "fabber");
        w.SpawnUnit(1, w.Map.CellCenter(33, 32), ReferenceSpecs.Fabber, "fabber");
        w.AddResourceNode(8, 8, 5000);
        w.AddResourceNode(30, 30, 5000);
        w.SetCpu(0, AiDifficulty.Easy);
        w.SetCpu(1, AiDifficulty.Easy);
        return w;
    }

    [Fact]
    public void Cpu_Vs_Cpu_Is_Deterministic_Across_Runs()
    {
        var a = CpuVsCpuWorld();
        var b = CpuVsCpuWorld();
        for (int t = 0; t < 400; t++)
        {
            a.Step(Empty);
            b.Step(Empty);
            Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
        }
    }

    [Fact]
    public void Cpu_Vs_Cpu_Produces_Activity()
    {
        // Sanity: the AI actually does something over 400 ticks (units get created).
        var w = CpuVsCpuWorld();
        for (int t = 0; t < 400; t++) w.Step(Empty);
        Assert.True(w.Units.Count > 2, $"expected CPUs to build up forces, got {w.Units.Count} units");
    }
```

- [ ] **Step 2: Run, expect pass** — `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`. Expected: 304 + 2 = 306. The determinism test passing proves the AI is reproducible from the seed.

- [ ] **Step 3: Full determinism gate (Release + Debug)**

Run: `dotnet test --configuration Release --nologo -v q` then `dotnet test --configuration Debug --nologo -v q`
Expected: PASS — SimCore.Tests 306, SpriteSlicer.Tests 6, 0 failures; the 3 golden/replay determinism tests green; Debug == Release.

- [ ] **Step 4: Append the 5d carry-forward note**

Append to the end of this plan file:

```markdown
---

## Plan-5d Inputs (carry-forward)

5c shipped the deterministic CPU framework + Easy tier. 5d adds Medium & Hard:
- **Dispatch:** `UpdateAi` switches on `Difficulty`; add `MediumDecide`/`HardDecide` (currently
  all fall back to `EasyDecide`). Share role helpers in `SimWorld.Ai.cs`.
- **Medium = stronger/earlier sustained pressure + rebuild:** lower attack threshold, shorter
  attack interval, rebuild lost production/supply, maybe a second combat unit type. May need a
  small hashed `AiState` (e.g., attack timing) — add to PlayerState or a per-player struct and
  fold into StateHasher (v8) with a golden re-pin.
- **Hard = reactive macro + aggression:** scale army before committing, pull back when behind,
  defend threatened buildings (attack-move home when an enemy is near a base).
- **Determinism:** keep the golden scenario human-only; prove each tier with CPU-vs-CPU
  determinism tests. Any new AI memory must be hashed.
- **Constants:** the Easy tunables (`AiDecisionInterval`, `EasyWorkerCap`, `EasySupplyBuffer`,
  `EasyAttackThreshold`, `EasyAttackInterval`) are the template; Medium/Hard get their own.
- **5e (Godot):** `SetCpu(playerId, difficulty)` is the entry point the match-setup menu calls;
  wire it into `TestMap`/match setup when building the menu.
```

- [ ] **Step 5: Commit**

```bash
git add tests/SimCore.Tests/CpuAiTests.cs docs/superpowers/plans/2026-06-14-cpu-framework-easy-tier.md
git commit -m "test(sim): CPU-vs-CPU determinism proof; record 5d inputs

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Self-Review (author checklist — completed)

- **Spec coverage:** controller/difficulty + SetCpu + UpdateAi phase + hash v7 + re-pin (Task 1); role resolution + Easy economy/supply (Task 2); Easy army + weak attack (Task 3); CPU determinism proof + gate + 5d inputs (Task 4). Stateless Easy (no AI memory hashed) — only constant Controller/Difficulty added. Every spec "In scope" item maps to a task.
- **Placeholder scan:** none — complete code in every step; the golden `<NEW>` is computed at execution time from the failing-test message (protocol) and counterfactually verified in Task 1 Step 7.
- **Type consistency:** `PlayerController`/`AiDifficulty`/`SetCpu`/`UpdateAi`/`EasyDecide`/`WorkerDef`/`CombatDef`/`SupplyDef`/`ProducerBuildingId`/`CountUnits`/`IdleWorker`/`NearestNodeId`/`FreeFootprintNear`/`EnemyBaseCenter`/`CombatUnitIds` named consistently across tasks; command shapes match `Commands.cs`; `_units`/`_buildings`/`_nodes`/`_players`/`FactionFor`/`GetUnit`/`CenterOf`/`FootprintCenter`/`FootprintPlaceable` verified against source; `Fix.FromInt(16)` build-range² matches `Apply(BuildCommand)`.

---

## Plan-5d Inputs (carry-forward)

5c shipped the deterministic CPU framework + Easy tier. 5d adds Medium & Hard:
- **Dispatch:** `UpdateAi` switches on `Difficulty`; add `MediumDecide`/`HardDecide` (currently
  all fall back to `EasyDecide`). Share role helpers in `SimWorld.Ai.cs`.
- **Medium = stronger/earlier sustained pressure + rebuild:** lower attack threshold, shorter
  attack interval, rebuild lost production/supply, maybe a second combat unit type. May need a
  small hashed `AiState` (e.g., attack timing) — add to PlayerState or a per-player struct and
  fold into StateHasher (v8) with a golden re-pin.
- **Hard = reactive macro + aggression:** scale army before committing, pull back when behind,
  defend threatened buildings (attack-move home when an enemy is near a base).
- **Determinism:** keep the golden scenario human-only; prove each tier with CPU-vs-CPU
  determinism tests. Any new AI memory must be hashed.
- **Shared helpers / fixes from 5c:** `WorkerDef`/`CombatDef`/`SupplyDef`/`ProducerBuildingId`/
  `CountUnits`/`QueuedUnits`/`IdleWorker`/`BuilderWorker`/`NearestNodeId`/`FreeFootprintNear`/
  `EnemyBaseCenter`/`CombatUnitIds` are reusable. NOTE the worker-cap guard must include
  `QueuedUnits` (in-flight trains) or it overshoots; supply-build uses `BuilderWorker` (a
  harvesting worker can place a building without interrupting its route).
- **Reference-faction fix (5c):** `ReferenceSpecs.Depot` is now `CanTrain: true` (the fabber's
  producer); `packs/reference/faction.json` regenerated to match.
- **5e (Godot):** `SetCpu(playerId, difficulty)` is the entry point the match-setup menu calls;
  wire it into `TestMap`/match setup when building the menu.
