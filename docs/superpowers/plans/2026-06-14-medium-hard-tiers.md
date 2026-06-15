# CPU Medium & Hard Tiers Implementation Plan (5d)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add fair, stateless Medium and Hard CPU tiers on the 5c framework — Medium = bigger economy + rebuild + earlier/sustained attacks; Hard = reactive (defend threatened bases, commit only when at-or-ahead of the enemy army).

**Architecture:** Refactor `EasyDecide` into shared parameterized helpers (`MacroEconomy`/`TrainCheapestCombat`/`MaybeAttack`), add `RebuildProduction` + Hard's reactive helpers, and dispatch `UpdateAi` on `Difficulty`. All tiers stateless → no new hashed state, golden unchanged.

**Tech Stack:** C# / .NET 8, xUnit. SimCore — integer/Fix only, no RNG, commands via `Apply`.

**Source spec:** `docs/superpowers/specs/2026-06-14-medium-hard-tiers-design.md`

---

## Conventions for every task

- Run from repo root `C:\Users\lssha\llm-rts`. If `dotnet` missing: bash `export PATH="$PATH:/c/Program Files/dotnet"`.
- Run SimCore tests: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
- **Baseline:** 306 SimCore tests pass; golden = `1571756151672809223UL` (must stay UNCHANGED — tiers add no hashed state).
- After each commit, confirm `git log --oneline -1`. End commit messages with `Co-Authored-By: RuFlo <ruv@ruv.net>`.

## Existing code (in `src/SimCore/Sim/SimWorld.Ai.cs`, from 5c)

Helpers present: `WorkerDef`, `CombatDef`, `SupplyDef`, `ProducerBuildingId`, `CountUnits(p, combat)`, `QueuedUnits(p, defId)`, `IdleWorker`, `BuilderWorker`, `NearestNodeId`, `EnemyBaseCenter`, `CombatUnitIds`, `FreeFootprintNear`. Easy consts: `AiDecisionInterval=10`, `EasyWorkerCap=8`, `EasySupplyBuffer=2`, `EasyAttackThreshold=6`, `EasyAttackInterval=300`. `UpdateAi` dispatches all difficulties to `EasyDecide`. `EasyDecide(playerId)` runs 5 inline steps (train workers w/ cap incl. QueuedUnits; harvest all idle workers; build supply via BuilderWorker; train cheapest combat; attack-move at threshold/interval).
Other accessible `SimWorld` members: `_players`, `_units`, `_buildings`, `_nodes`, `FactionFor(p)`, `GetUnit`, `GetBuilding`, `CenterOf(Building)`, `PrerequisitesMet(p, IReadOnlyList<string>)`, `Apply(Command)`, `Tick`, `Phase`, `Map`. `FactionDef.GetBuilding(id)` returns `BuildingDef?`. `Fix.FromInt(n)`. `Unit.AttackMoveDest`/`IsAttackMoving`/`HasMoveOrder`/`Weapon`/`Position`. `CpuAiTests.cs` already has `Empty`, `Workers(w,p)`, `EasyEcoWorld`, `CpuVsCpuWorld`.

---

## Task 1: Refactor Easy into shared helpers (behavior-preserving)

**Files:** Modify `src/SimCore/Sim/SimWorld.Ai.cs`.

This task extracts helpers and rewrites `EasyDecide` to call them — NO behavior change. The existing 5c tests (Easy economy/army/attack + CPU-vs-CPU determinism) are the guard.

- [ ] **Step 1: Add the three shared helpers**

In `src/SimCore/Sim/SimWorld.Ai.cs`, add these methods to the class (e.g. just above `EasyDecide`):

```csharp
    /// <summary>Economy macro: train workers up to workerCap (counting in-flight trains), send
    /// idle workers to the nearest node, and build supply when within supplyBuffer of the cap.</summary>
    private void MacroEconomy(int playerId, int workerCap, int supplyBuffer)
    {
        var ps = _players[playerId];

        var wdef = WorkerDef(playerId);
        if (wdef is not null
            && CountUnits(playerId, combat: false) + QueuedUnits(playerId, wdef.Id) < workerCap)
        {
            int prod = ProducerBuildingId(playerId, wdef.ProducedBy);
            if (prod != 0) Apply(new TrainCommand(playerId, prod, wdef.Id));
        }

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

        var sdef = SupplyDef(playerId);
        if (sdef is not null && ps.SupplyUsed >= ps.SupplyCap - supplyBuffer
            && ps.Minerals >= sdef.Spec.MineralCost)
        {
            var bw = BuilderWorker(playerId);
            if (bw is not null)
            {
                var cell = FreeFootprintNear(bw.Position, sdef.Spec.Width, sdef.Spec.Height);
                if (cell is not null) Apply(new BuildCommand(playerId, bw.Id, sdef.Id, cell.Value.x, cell.Value.y));
            }
        }
    }

    /// <summary>Train the cheapest combat unit from its producer when minerals + supply allow.</summary>
    private void TrainCheapestCombat(int playerId)
    {
        var ps = _players[playerId];
        var cdef = CombatDef(playerId);
        if (cdef is null) return;
        int prod = ProducerBuildingId(playerId, cdef.ProducedBy);
        if (prod != 0 && ps.Minerals >= cdef.Spec.MineralCost
            && ps.SupplyUsed + cdef.Spec.SupplyCost <= ps.SupplyCap)
            Apply(new TrainCommand(playerId, prod, cdef.Id));
    }

    /// <summary>Attack-move the whole army at the enemy base when the army is big enough, on cadence.</summary>
    private void MaybeAttack(int playerId, int threshold, int interval)
    {
        if (CountUnits(playerId, combat: true) >= threshold && Tick % interval == 0)
        {
            var target = EnemyBaseCenter(playerId);
            if (target is not null)
            {
                var army = CombatUnitIds(playerId);
                if (army.Length > 0) Apply(new AttackMoveCommand(playerId, army, target.Value));
            }
        }
    }
```

- [ ] **Step 2: Rewrite `EasyDecide` to call them**

Replace the entire body of `EasyDecide` (the 5 inline steps) with:

```csharp
    /// <summary>Easy tier: light macro + cheapest-combat army + occasional weak attack.</summary>
    private void EasyDecide(int playerId)
    {
        MacroEconomy(playerId, EasyWorkerCap, EasySupplyBuffer);
        TrainCheapestCombat(playerId);
        MaybeAttack(playerId, EasyAttackThreshold, EasyAttackInterval);
    }
```

- [ ] **Step 3: Run the full suite, expect pass (unchanged)**

Run: `dotnet test --configuration Release --nologo -v q`
Expected: PASS — SimCore.Tests 306, SpriteSlicer.Tests 6, 0 failures. Every 5c Easy test (`Easy_Trains_Workers_Up_To_Cap`, `Easy_Harvests...`, `Easy_Builds_Supply_When_Blocked`, `Easy_Trains_Combat_Units`, `Easy_Attacks...`) and `Cpu_Vs_Cpu_Is_Deterministic_Across_Runs` pass unchanged (behavior preserved); golden unchanged `1571756151672809223UL`.

- [ ] **Step 4: Commit**

```bash
git add src/SimCore/Sim/SimWorld.Ai.cs
git commit -m "refactor(sim): extract Easy AI into shared MacroEconomy/TrainCheapestCombat/MaybeAttack

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 2: Medium tier (rebuild + earlier/sustained pressure)

**Files:**
- Modify: `src/SimCore/Sim/SimWorld.Ai.cs`
- Test: `tests/SimCore.Tests/CpuAiTests.cs` (add)

- [ ] **Step 1: Write the failing tests**

Add to `CpuAiTests.cs` (inside the class):

```csharp
    // A CPU player (id 1) at the given difficulty with a full base + big node; player 0 has a lone
    // depot so the match stays InProgress.
    private static SimWorld OneCpuWorld(AiDifficulty diff)
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 9, new FactionDef?[] { ReferenceFaction.Def, ReferenceFaction.Def });
        w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 3, 3, "depot");
        w.Players[1].Minerals = 2000;
        w.AddCompletedBuilding(1, ReferenceSpecs.Depot, 30, 30, "depot");
        w.AddCompletedBuilding(1, ReferenceSpecs.Barracks, 33, 30, "barracks");
        w.SpawnUnit(1, w.Map.CellCenter(30, 28), ReferenceSpecs.Fabber, "fabber");
        w.AddResourceNode(28, 28, 100000);
        w.SetCpu(1, diff);
        return w;
    }

    [Fact]
    public void Medium_Trains_More_Workers_Than_Easy_Cap()
    {
        var w = OneCpuWorld(AiDifficulty.Medium);
        for (int t = 0; t < 3000; t++) w.Step(Empty);
        int workers = Workers(w, 1);
        Assert.True(workers > 8, $"Medium worker cap is 10 (> Easy's 8); got {workers}");
        Assert.True(workers <= 10, $"Medium worker cap is 10; got {workers}");
    }

    [Fact]
    public void Medium_Rebuilds_Lost_Production_Building()
    {
        var w = OneCpuWorld(AiDifficulty.Medium);
        for (int t = 0; t < 300; t++) w.Step(Empty); // establish economy
        // Destroy the CPU's only barracks (combat producer).
        foreach (var b in w.Buildings) if (b.OwnerId == 1 && b.DefId == "barracks") { b.Hp = 0; break; }
        w.Step(Empty); // RemoveDeadBuildings clears it
        Assert.DoesNotContain(w.Buildings, b => b.OwnerId == 1 && b.DefId == "barracks");
        for (int t = 0; t < 1000; t++) w.Step(Empty);
        Assert.Contains(w.Buildings, b => b.OwnerId == 1 && b.DefId == "barracks"); // rebuilt
    }
```

- [ ] **Step 2: Run, expect failure** — both new tests fail (Medium falls back to Easy: cap 8, no rebuild).

- [ ] **Step 3: Add Medium constants, `RebuildProduction`, `MediumDecide`, and dispatch**

In `src/SimCore/Sim/SimWorld.Ai.cs`, add the Medium consts next to the Easy ones:

```csharp
    private const int MediumWorkerCap = 10;
    private const int MediumSupplyBuffer = 3;
    private const int MediumAttackThreshold = 4;
    private const int MediumAttackInterval = 150;
```

Add the `RebuildProduction` helper to the class:

```csharp
    /// <summary>If the player owns no building (any construction state) that produces its combat
    /// unit, build that producer (when affordable, prereqs met, and a worker exists). The
    /// supply/worker producer (depot) is rebuilt by MacroEconomy's supply step instead.</summary>
    private void RebuildProduction(int playerId)
    {
        var cdef = CombatDef(playerId);
        if (cdef is null) return;
        foreach (var b in _buildings)
            if (b.OwnerId == playerId && b.DefId == cdef.ProducedBy) return; // already have/building one
        var bdef = FactionFor(playerId)?.GetBuilding(cdef.ProducedBy);
        if (bdef is null) return;
        var ps = _players[playerId];
        if (ps.Minerals < bdef.Spec.MineralCost) return;
        if (!PrerequisitesMet(playerId, bdef.Requires)) return;
        var bw = BuilderWorker(playerId);
        if (bw is null) return;
        var cell = FreeFootprintNear(bw.Position, bdef.Spec.Width, bdef.Spec.Height);
        if (cell is not null) Apply(new BuildCommand(playerId, bw.Id, bdef.Id, cell.Value.x, cell.Value.y));
    }

    /// <summary>Medium tier: bigger economy, rebuilds lost production, earlier/sustained attacks.</summary>
    private void MediumDecide(int playerId)
    {
        MacroEconomy(playerId, MediumWorkerCap, MediumSupplyBuffer);
        RebuildProduction(playerId);
        TrainCheapestCombat(playerId);
        MaybeAttack(playerId, MediumAttackThreshold, MediumAttackInterval);
    }
```

Update `UpdateAi`'s switch to dispatch Medium:

```csharp
            switch (_players[p].Difficulty)
            {
                case AiDifficulty.Medium: MediumDecide(p); break;
                default: EasyDecide(p); break; // Easy (Hard added in Task 3)
            }
```

- [ ] **Step 4: Run, expect pass** — `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`. Expected: 306 + 2 = 308; golden unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/SimWorld.Ai.cs tests/SimCore.Tests/CpuAiTests.cs
git commit -m "feat(sim): Medium CPU tier — bigger economy, rebuild production, sustained pressure

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 3: Hard tier (reactive — defend + commit-when-ahead)

**Files:**
- Modify: `src/SimCore/Sim/SimWorld.Ai.cs`
- Test: `tests/SimCore.Tests/CpuAiTests.cs` (add)

- [ ] **Step 1: Write the failing tests**

Add to `CpuAiTests.cs` (inside the class):

```csharp
    [Fact]
    public void Hard_Defends_A_Threatened_Base()
    {
        // Hard CPU (player 1) base at (30,30) with a pre-built army; an enemy attacker next to it.
        var w = new SimWorld(new MapGrid(40, 40), seed: 11, new FactionDef?[] { ReferenceFaction.Def, ReferenceFaction.Def });
        w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 3, 3, "depot");        // far human base (commit target)
        w.AddCompletedBuilding(1, ReferenceSpecs.Barracks, 30, 30, "barracks"); // CPU base to defend
        for (int i = 0; i < 8; i++) w.SpawnUnit(1, w.Map.CellCenter(25 + i % 4, 35), ReferenceSpecs.Trooper, "trooper");
        var enemy = w.SpawnUnit(0, w.Map.CellCenter(31, 31), ReferenceSpecs.Trooper, "trooper"); // next to CPU base
        w.SetCpu(1, AiDifficulty.Hard);

        for (int t = 0; t <= 10; t++) w.Step(Empty); // reach a decision tick (Tick % 10 == 0 at t=10)

        // The CPU army should be recalled to defend its base (~x=30), not marching to (3,3).
        bool defending = false;
        foreach (var u in w.Units)
            if (u.OwnerId == 1 && u.Weapon is not null && u.IsAttackMoving && u.AttackMoveDest.X > Fix.FromInt(20))
                defending = true;
        Assert.True(defending, "expected Hard CPU to recall its army to the threatened base (east), not attack west");
    }

    [Fact]
    public void Hard_Holds_When_Outnumbered()
    {
        // CPU (player 1) has a small army; human (player 0) has a bigger army far away. No threat near
        // the CPU base → Hard should NOT commit (outnumbered).
        var w = new SimWorld(new MapGrid(40, 40), seed: 12, new FactionDef?[] { ReferenceFaction.Def, ReferenceFaction.Def });
        w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 3, 3, "depot");
        w.AddCompletedBuilding(1, ReferenceSpecs.Barracks, 30, 30, "barracks");
        for (int i = 0; i < 8; i++) w.SpawnUnit(1, w.Map.CellCenter(28, 30 + i % 6), ReferenceSpecs.Trooper, "trooper"); // CPU army 8
        for (int i = 0; i < 20; i++) w.SpawnUnit(0, w.Map.CellCenter(3, 5 + i % 20), ReferenceSpecs.Trooper, "trooper"); // human army 20 (far)
        w.SetCpu(1, AiDifficulty.Hard);

        for (int t = 0; t <= 120; t++) w.Step(Empty); // reach an attack tick (120 % 120 == 0)

        bool committed = false;
        foreach (var u in w.Units)
            if (u.OwnerId == 1 && u.Weapon is not null && u.IsAttackMoving && u.AttackMoveDest.X < Fix.FromInt(20))
                committed = true;
        Assert.False(committed, "expected Hard CPU to hold (not march west) while outnumbered and unthreatened");
    }

    [Fact]
    public void Hard_Commits_When_Ahead()
    {
        // CPU army >= HardMinArmy and >= enemy army (enemy has none) → commits at the attack tick.
        var w = new SimWorld(new MapGrid(40, 40), seed: 13, new FactionDef?[] { ReferenceFaction.Def, ReferenceFaction.Def });
        w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 3, 3, "depot");
        w.AddCompletedBuilding(1, ReferenceSpecs.Barracks, 30, 30, "barracks");
        for (int i = 0; i < 10; i++) w.SpawnUnit(1, w.Map.CellCenter(28, 30 + i % 8), ReferenceSpecs.Trooper, "trooper");
        w.SetCpu(1, AiDifficulty.Hard);

        for (int t = 0; t <= 120; t++) w.Step(Empty);

        bool committed = false;
        foreach (var u in w.Units)
            if (u.OwnerId == 1 && u.Weapon is not null && u.IsAttackMoving && u.AttackMoveDest.X < Fix.FromInt(20))
                committed = true;
        Assert.True(committed, "expected Hard CPU to commit (march toward the enemy base at x=3) when ahead");
    }
```

- [ ] **Step 2: Run, expect failure** — the three Hard tests fail (Hard falls back to Easy: no defend, attacks at threshold 6/interval 300 toward the enemy base regardless of army comparison).

- [ ] **Step 3: Add Hard constants, helpers, `HardDecide`, and dispatch**

In `src/SimCore/Sim/SimWorld.Ai.cs`, add the Hard consts:

```csharp
    private const int HardWorkerCap = 14;
    private const int HardSupplyBuffer = 4;
    private const int HardMinArmy = 8;
    private const int HardAttackInterval = 120;
    private const int HardDefendRadius = 10; // cells
```

Add these helpers to the class:

```csharp
    /// <summary>Center of the first owned building (stable order) with an enemy combat unit within
    /// HardDefendRadius cells; null if none threatened.</summary>
    private FixVec? ThreatenedBuildingCenter(int playerId)
    {
        long radiusSq = Fix.FromInt(HardDefendRadius * HardDefendRadius).Raw;
        foreach (var b in _buildings)
        {
            if (b.OwnerId != playerId) continue;
            var c = CenterOf(b);
            foreach (var u in _units)
            {
                if (u.OwnerId == playerId || u.Weapon is null) continue;
                if ((u.Position - c).LengthSquared().Raw <= radiusSq) return c;
            }
        }
        return null;
    }

    /// <summary>Count of all non-p units with a weapon (the enemy army; full-information).</summary>
    private int EnemyCombatCount(int playerId)
    {
        int c = 0;
        foreach (var u in _units) if (u.OwnerId != playerId && u.Weapon is not null) c++;
        return c;
    }

    /// <summary>Reactive attack: defend a threatened base; else commit only when at-or-ahead of the
    /// enemy army (and past a minimum), on cadence; otherwise keep massing.</summary>
    private void ReactiveAttack(int playerId)
    {
        var threat = ThreatenedBuildingCenter(playerId);
        if (threat is not null)
        {
            var defenders = CombatUnitIds(playerId);
            if (defenders.Length > 0) Apply(new AttackMoveCommand(playerId, defenders, threat.Value));
            return;
        }
        int own = CountUnits(playerId, combat: true);
        if (own >= HardMinArmy && own >= EnemyCombatCount(playerId) && Tick % HardAttackInterval == 0)
        {
            var target = EnemyBaseCenter(playerId);
            if (target is not null)
            {
                var army = CombatUnitIds(playerId);
                if (army.Length > 0) Apply(new AttackMoveCommand(playerId, army, target.Value));
            }
        }
    }

    /// <summary>Hard tier: strong economy, rebuild, reactive attack (defend / commit-when-ahead).</summary>
    private void HardDecide(int playerId)
    {
        MacroEconomy(playerId, HardWorkerCap, HardSupplyBuffer);
        RebuildProduction(playerId);
        TrainCheapestCombat(playerId);
        ReactiveAttack(playerId);
    }
```

Update `UpdateAi`'s switch to dispatch Hard:

```csharp
            switch (_players[p].Difficulty)
            {
                case AiDifficulty.Medium: MediumDecide(p); break;
                case AiDifficulty.Hard:   HardDecide(p);   break;
                default: EasyDecide(p); break; // Easy
            }
```

- [ ] **Step 4: Run, expect pass** — `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`. Expected: 308 + 3 = 311; golden unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/SimWorld.Ai.cs tests/SimCore.Tests/CpuAiTests.cs
git commit -m "feat(sim): Hard CPU tier — reactive (defend threatened base, commit when ahead)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 4: Per-tier determinism + full gate + 5e inputs

**Files:**
- Test: `tests/SimCore.Tests/CpuAiTests.cs` (add)
- Modify: `docs/superpowers/plans/2026-06-14-medium-hard-tiers.md` (append note)

- [ ] **Step 1: Write the determinism tests**

Add to `CpuAiTests.cs` (inside the class):

```csharp
    [Fact]
    public void Medium_Vs_Medium_Is_Deterministic_Across_Runs()
    {
        static SimWorld Build() { var w = OneCpuWorld(AiDifficulty.Medium); w.AddCompletedBuilding(0, ReferenceSpecs.Barracks, 6, 3, "barracks"); w.Players[0].Minerals = 2000; w.SpawnUnit(0, w.Map.CellCenter(5, 6), ReferenceSpecs.Fabber, "fabber"); w.AddResourceNode(8, 8, 100000); w.SetCpu(0, AiDifficulty.Medium); return w; }
        var a = Build();
        var b = Build();
        for (int t = 0; t < 400; t++) { a.Step(Empty); b.Step(Empty); Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b)); }
    }

    [Fact]
    public void Hard_Vs_Hard_Is_Deterministic_Across_Runs()
    {
        static SimWorld Build() { var w = OneCpuWorld(AiDifficulty.Hard); w.AddCompletedBuilding(0, ReferenceSpecs.Barracks, 6, 3, "barracks"); w.Players[0].Minerals = 2000; w.SpawnUnit(0, w.Map.CellCenter(5, 6), ReferenceSpecs.Fabber, "fabber"); w.AddResourceNode(8, 8, 100000); w.SetCpu(0, AiDifficulty.Hard); return w; }
        var a = Build();
        var b = Build();
        for (int t = 0; t < 400; t++) { a.Step(Empty); b.Step(Empty); Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b)); }
    }
```

- [ ] **Step 2: Run, expect pass** — `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`. Expected: 311 + 2 = 313. Both determinism tests pass (each tier reproducible from the seed).

- [ ] **Step 3: Full gate (Release + Debug)**

Run: `dotnet test --configuration Release --nologo -v q` then `dotnet test --configuration Debug --nologo -v q`
Expected: PASS — SimCore.Tests 313, SpriteSlicer.Tests 6, 0 failures; the 3 golden/replay determinism tests green with golden UNCHANGED `1571756151672809223UL`; Debug == Release.

- [ ] **Step 4: Append the 5e carry-forward note**

Append to the end of this plan file:

```markdown
---

## Plan-5e Inputs (carry-forward)

5d completed the difficulty ladder (Easy/Medium/Hard, all fair + deterministic). 5e is the
Godot match-flow capstone:
- **Start menu:** title screen → "Play vs CPU" → choose difficulty (Easy/Medium/Hard), choose
  your faction (scan `packs/` via `FactionPackLoader` + the in-code reference), optional map;
  then start the match.
- **Wire-up:** construct `SimWorld` with the per-player factions (5b `FactionDef?[]` ctor) and
  call `SetCpu(1, difficulty)` (5c) for the AI player; player 0 = human.
- **Victory/Defeat screen:** poll `world.Phase`/`WinnerId` (5a) each tick; on `Over`, freeze
  input and show Victory (WinnerId == human) / Defeat / Draw with a Restart button.
- **TestMap:** today it boots a 1v1 sandbox with player 1 idle — wire `SetCpu` into it (or the
  new match-setup path) so manual play has a real opponent.
- All AI/match logic is SimCore; 5e is Godot-only (RenderMath floats OK; no determinism impact).
```

- [ ] **Step 5: Commit**

```bash
git add tests/SimCore.Tests/CpuAiTests.cs docs/superpowers/plans/2026-06-14-medium-hard-tiers.md
git commit -m "test(sim): Medium/Hard CPU-vs-CPU determinism; record 5e inputs

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Self-Review (author checklist — completed)

- **Spec coverage:** refactor into shared helpers (Task 1); `RebuildProduction` + Medium (bigger eco via MediumWorkerCap, rebuild, earlier/sustained attack via lower threshold/interval) (Task 2); Hard reactive (`ThreatenedBuildingCenter` defend, `EnemyCombatCount` commit-when-ahead-and-past-min, rebuild) (Task 3); per-tier determinism + gate + 5e inputs (Task 4). Dispatch updated each task. All tiers stateless → no hashed state, golden unchanged. Every spec "In scope" item maps to a task.
- **Placeholder scan:** none — complete code in every step.
- **Type consistency:** `MacroEconomy(p,workerCap,supplyBuffer)`/`TrainCheapestCombat(p)`/`MaybeAttack(p,threshold,interval)`/`RebuildProduction(p)`/`MediumDecide`/`HardDecide`/`ReactiveAttack`/`ThreatenedBuildingCenter`/`EnemyCombatCount` consistent across tasks; consts named per tier; `EasyDecide` rewrite preserves the 5c step order/params; `AttackMoveDest`/`IsAttackMoving` used in tests match `Unit` fields; `ReferenceSpecs.Trooper` (has Weapon) used for spawned armies; the refactor is behavior-preserving (guarded by 5c tests).

---

## Plan-5e Inputs (carry-forward)

5d completed the difficulty ladder (Easy/Medium/Hard, all fair + deterministic). 5e is the
Godot match-flow capstone:
- **Start menu:** title screen -> "Play vs CPU" -> choose difficulty (Easy/Medium/Hard), choose
  your faction (scan `packs/` via `FactionPackLoader` + the in-code reference), optional map;
  then start the match.
- **Wire-up:** construct `SimWorld` with the per-player factions (5b `FactionDef?[]` ctor) and
  call `SetCpu(1, difficulty)` (5c) for the AI player; player 0 = human.
- **Victory/Defeat screen:** poll `world.Phase`/`WinnerId` (5a) each tick; on `Over`, freeze
  input and show Victory (WinnerId == human) / Defeat / Draw with a Restart button.
- **TestMap:** today it boots a 1v1 sandbox with player 1 idle -- wire `SetCpu` into it (or the
  new match-setup path) so manual play has a real opponent.
- All AI/match logic is SimCore; 5e is Godot-only (RenderMath floats OK; no determinism impact).
