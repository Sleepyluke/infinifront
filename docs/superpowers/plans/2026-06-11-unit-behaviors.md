# Unit Behaviors Implementation Plan — Stances, Patrol, Rally, Del-to-Destroy

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stances (AutoAttack default / Defend / Passive) for idle units, patrol orders, building rally points, and Del-to-destroy — sim commands + hashed state + Godot UI.

**Architecture:** Spec `docs/superpowers/specs/2026-06-11-unit-behaviors-design.md`. All behavior is deterministic sim state mutated only by commands; the Godot layer adds buttons/hotkeys that enqueue them. Idle acquisition extends `UpdateCombat`'s existing acquisition machinery with a per-unit anchor; patrol reuses attack-move; rally hooks `UpdateProduction`; destroy reuses the death sweeps.

**Tech Stack:** C# net8.0 fixed-point sim + xUnit (TDD, golden-hash protocol: re-pin in the same commit as any intentional behavior/hash change, cross-run tests green first, Debug==Release). Godot 4.6 .NET via the winget binary (never Steam). Repo `C:\Users\lssha\llm-rts`; `$env:Path += ';C:\Program Files\dotnet'` in fresh shells.

---

## File structure

```
src/SimCore/Sim/Unit.cs              MOD  Stance, HasAnchor/Anchor, IsPatrolling/PatrolA/PatrolB
src/SimCore/Sim/Building.cs          MOD  HasRally, RallyPoint
src/SimCore/Sim/Commands.cs          MOD  SetStanceCommand, PatrolCommand, SetRallyCommand, DestroyCommand
src/SimCore/Sim/SimWorld.cs          MOD  Apply cases; order-clearing also clears anchor/patrol
src/SimCore/Sim/SimWorld.Combat.cs   MOD  idle acquisition + anchor leash + defend return; patrol leg swap
src/SimCore/Sim/SimWorld.Buildings.cs MOD rally move on production spawn
src/SimCore/Sim/StateHasher.cs       MOD  new fields
tests/SimCore.Tests/StanceTests.cs   NEW
tests/SimCore.Tests/PatrolTests.cs   NEW
tests/SimCore.Tests/RallyDestroyTests.cs NEW
tests/SimCore.Tests/DeterminismTests.cs MOD scenario v5 + re-pins
godot/scripts/Hud.cs                 MOD  stance buttons
godot/scripts/CommandController.cs   MOD  P-patrol mode, rally right-click, (armed-mode plumbing)
godot/scripts/SelectionController.cs MOD  Del → DestroyCommand
godot/scripts/UnitView.cs            MOD  patrol glyph
godot/scripts/BuildingView.cs        MOD  Selected + rally flag
godot/scripts/ViewSync.cs            MOD  pass IsPatrolling/rally to views; building selection highlight
godot/README.md                      MOD  controls table
```

**Conventions reminders for every task:** read the current file before editing; sim iterates lists in stable order only; no floats in sim; every new mutable sim field gets hashed (or documented excluded); golden re-pin in the same commit as the behavior change with cross-run tests green first and both configs agreeing; commits end with the `Co-Authored-By: RuFlo <ruv@ruv.net>` trailer.

---

### Task 1: Stance state + SetStanceCommand (no behavior yet)

**Files:** Modify `Unit.cs`, `Commands.cs`, `SimWorld.cs` (Apply), `StateHasher.cs`; Test `tests/SimCore.Tests/StanceTests.cs`

- [ ] **Step 1: Failing tests** at `tests/SimCore.Tests/StanceTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class StanceTests
{
    private static Weapon Gun() => new() { Damage = 5, Range = Fix.FromInt(3), CooldownTicks = 4 };

    [Fact]
    public void Units_Default_To_AutoAttack()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        Assert.Equal(Stance.AutoAttack, w.GetUnit(id)!.Stance);
    }

    [Fact]
    public void SetStance_Applies_To_Owned_Units_Only()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var mine = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        var theirs = w.SpawnUnit(1, w.Map.CellCenter(8, 8), Fix.FromFraction(1, 2), 50, Gun());
        w.Step(new Command[] { new SetStanceCommand(0, new[] { mine, theirs }, Stance.Passive) });
        Assert.Equal(Stance.Passive, w.GetUnit(mine)!.Stance);
        Assert.Equal(Stance.AutoAttack, w.GetUnit(theirs)!.Stance);
    }

    [Fact]
    public void Stance_Changes_Hash()
    {
        SimWorld World()
        {
            var w = new SimWorld(new MapGrid(16, 16), seed: 5);
            w.SpawnUnit(0, FixVec.FromInts(1, 1), Fix.FromFraction(1, 2), 50);
            return w;
        }
        var a = World();
        var b = World();
        b.GetUnit(1)!.Stance = Stance.Defend;
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }
}
```

- [ ] **Step 2:** `dotnet test --filter StanceTests` — compile failure.
- [ ] **Step 3: Implement.**
  - `Unit.cs`: add `public Stance Stance { get; set; } = Stance.AutoAttack;` and at file bottom `public enum Stance : byte { AutoAttack = 0, Defend = 1, Passive = 2 }`.
  - `Commands.cs`: `public sealed record SetStanceCommand(int PlayerId, int[] UnitIds, Stance Stance) : Command(PlayerId);`
  - `SimWorld.cs` Apply: new case — for each id, `GetUnit`, skip null/not-owned, set `u.Stance`.
  - `StateHasher.cs`: in the unit loop add `h = Mix(h, (ulong)u.Stance);` (after SightRange).
- [ ] **Step 4: Golden.** Hash function changed → cross-run determinism tests green, then re-pin `GoldenTrajectoryHash` in the SAME commit, Debug+Release agree.
- [ ] **Step 5:** Full suite both configs (142 expected: 139 + 3). **Commit** `feat: stance state and SetStanceCommand (re-pin golden)`

---

### Task 2: Idle acquisition — AutoAttack engages, Passive doesn't, anchored leash

**Files:** Modify `Unit.cs` (anchor), `SimWorld.Combat.cs`, `SimWorld.cs` (order-clearing), `StateHasher.cs`; Test append `StanceTests.cs`

- [ ] **Step 1: Failing tests** (append to StanceTests):

```csharp
    [Fact]
    public void Idle_AutoAttack_Unit_Engages_Visible_Enemy()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var guard = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        var intruder = w.SpawnUnit(1, w.Map.CellCenter(9, 5), Fix.FromFraction(1, 2), 50); // dist 4 <= range3+bonus2, sight 7
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(intruder, w.GetUnit(guard)!.AttackTargetId);
        Assert.True(w.GetUnit(guard)!.HasAnchor);
    }

    [Fact]
    public void Idle_Passive_Unit_Never_Engages()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var guard = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        w.Step(new Command[] { new SetStanceCommand(0, new[] { guard }, Stance.Passive) });
        w.SpawnUnit(1, w.Map.CellCenter(9, 5), Fix.FromFraction(1, 2), 50);
        for (int i = 0; i < 5; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.GetUnit(guard)!.AttackTargetId);
    }

    [Fact]
    public void AutoAttack_Chase_Drops_At_Anchor_Leash()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 1);
        var guard = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 4), 50, Gun());
        var bait = w.SpawnUnit(1, w.Map.CellCenter(9, 5), Fix.FromFraction(1, 2), 500);
        w.Step(System.Array.Empty<Command>()); // guard acquires, anchor set
        Assert.Equal(bait, w.GetUnit(guard)!.AttackTargetId);
        w.Step(new Command[] { new MoveCommand(1, new[] { bait }, w.Map.CellCenter(35, 5)) });
        for (int i = 0; i < 200; i++) w.Step(System.Array.Empty<Command>());
        var g = w.GetUnit(guard)!;
        Assert.Equal(0, g.AttackTargetId);                      // dropped (leash or fog)
        var (gx, _) = w.Map.WorldToCell(g.Position);
        Assert.True(gx <= 5 + 3 + 4 + 1, $"guard chased to x={gx}"); // never beyond anchor + Range + LeashBonus (+1 slack)
        Assert.False(g.HasAnchor);                              // anchor cleared after disengage
    }

    [Fact]
    public void Explicit_Move_Order_Clears_Anchor_Engagement()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var guard = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        w.SpawnUnit(1, w.Map.CellCenter(9, 5), Fix.FromFraction(1, 2), 500);
        w.Step(System.Array.Empty<Command>()); // engaged
        w.Step(new Command[] { new MoveCommand(0, new[] { guard }, w.Map.CellCenter(2, 2)) });
        Assert.False(w.GetUnit(guard)!.HasAnchor);
        Assert.Equal(0, w.GetUnit(guard)!.AttackTargetId);
    }
```

- [ ] **Step 2:** Verify failures (idle units don't engage today).
- [ ] **Step 3: Implement.**
  - `Unit.cs`: `public bool HasAnchor { get; set; }` and `public FixVec Anchor { get; set; }`.
  - `SimWorld.Combat.cs` pass 2, AFTER the attack-move acquisition block, add idle acquisition:

```csharp
            // Idle stance acquisition: units with no orders engage per stance.
            if (!u.IsAttackMoving && !u.HasMoveOrder && u.HarvestPhase == HarvestPhase.None
                && u.Weapon is not null && u.AttackTargetId == 0 && u.Stance != Stance.Passive)
            {
                var acquired = AcquireTarget(u, u.Weapon.Range + AcquireBonus);
                if (acquired != 0)
                {
                    u.AttackTargetId = acquired;
                    u.Anchor = u.Position;
                    u.HasAnchor = true;
                }
            }
```

  - Leash generalization: the existing leash branch applies to `u.IsAttackMoving`; add an anchored variant after it:

```csharp
            if (u.HasAnchor)
            {
                var leash = u.Weapon.Range + LeashBonus;
                if ((targetPos - u.Anchor).LengthSquared() > leash * leash)
                {
                    u.AttackTargetId = 0;
                    Disengage(u);   // defined in Task 3 (Task 2 interim: clear anchor inline)
                    continue;
                }
            }
```
    Task 2 interim (before Task 3 adds Disengage): inline `u.HasAnchor = false;`.
  - Disengage must ALSO fire where an anchored engagement ends for other reasons: target dead/missing (the stale-clear at the top of pass 2 and the TryResolveTarget-fail branch) and fog chase-drop. In each of those, clear the anchor when `u.HasAnchor` (Task 3 routes these through `Disengage`; in this task clear inline).
  - Order-clearing: in `SimWorld.cs` Apply — MoveCommand, AttackCommand, AttackMoveCommand, HarvestCommand cases already reset combat fields; add `u.HasAnchor = false;` to each (explicit orders override stance engagements).
  - `StateHasher.cs`: hash `u.HasAnchor ? 1UL : 0UL`, `u.Anchor.X.Raw`, `u.Anchor.Y.Raw` after Stance.
- [ ] **Step 4: Golden** — idle acquisition WILL change the scenario trajectory (battle units now engage while idle). Cross-run green first, re-pin same commit, both configs.
- [ ] **Step 5:** Full suite both configs. Existing tests that rely on idle units NOT fighting (e.g. fog tests with idle enemies, harvest tests with armed idlers) may shift — triage per intent: targeting tests adjust distances/stances (`Stance.Passive` where the test wants inert dummies); mechanics tests keep working. List touched tests. **Commit** `feat: idle stance acquisition with anchored leash (re-pin golden)`

---

### Task 3: Defend returns to anchor

**Files:** Modify `SimWorld.Combat.cs`; Test append `StanceTests.cs`

- [ ] **Step 1: Failing test** (append):

```csharp
    [Fact]
    public void Defend_Unit_Returns_To_Post_After_Engagement()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 1);
        var guard = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        w.Step(new Command[] { new SetStanceCommand(0, new[] { guard }, Stance.Defend) });
        var bait = w.SpawnUnit(1, w.Map.CellCenter(9, 5), Fix.FromFraction(3, 4), 500);
        w.Step(System.Array.Empty<Command>()); // engage + anchor
        w.Step(new Command[] { new MoveCommand(1, new[] { bait }, w.Map.CellCenter(35, 5)) });
        for (int i = 0; i < 300 && !(w.GetUnit(guard)!.AttackTargetId == 0 && !w.GetUnit(guard)!.HasMoveOrder); i++)
            w.Step(System.Array.Empty<Command>());
        var (gx, gy) = w.Map.WorldToCell(w.GetUnit(guard)!.Position);
        Assert.Equal((5, 5), (gx, gy));     // back at post
        Assert.False(w.GetUnit(guard)!.HasAnchor);
    }
```

- [ ] **Step 2:** Verify failure (guard stays where the leash dropped).
- [ ] **Step 3: Implement.** Add to the combat partial:

```csharp
    /// <summary>Ends an anchored stance engagement. Defend walks home; AutoAttack
    /// stays put. Anchor cleared either way (Defend clears on arrival via the
    /// normal arrival logic — the move order is an ordinary move).</summary>
    private void Disengage(Unit u)
    {
        if (!u.HasAnchor) return;
        if (u.Stance == Stance.Defend && !u.Position.Equals(u.Anchor))
        {
            var (ax, ay) = Map.WorldToCell(u.Anchor);
            u.HasMoveOrder = true;
            u.MoveTarget = u.Anchor;
            u.Path = GetField(ax, ay);
            u.PathVersion = Map.Version;
        }
        u.HasAnchor = false;
    }
```
Route every anchored-engagement end through it: the anchored leash drop, the stale-target clear, the TryResolveTarget failure, and the fog chase-drop (call `Disengage(u)` after zeroing AttackTargetId — it no-ops when `!HasAnchor`, so it is safe to call unconditionally in those branches).
- [ ] **Step 4: Golden** — defend isn't in the scenario yet, but the Disengage routing may alter AutoAttack disengagement ordering — run cross-run tests; if golden changes, re-pin same commit (intentional); if unchanged, fine.
- [ ] **Step 5:** Full suite both configs. **Commit** `feat: defend stance returns to anchor after engagement`

---

### Task 4: Patrol

**Files:** Modify `Unit.cs`, `Commands.cs`, `SimWorld.cs` (Apply + order-clearing), `SimWorld.Combat.cs` (leg swap), `StateHasher.cs`; Test `tests/SimCore.Tests/PatrolTests.cs`

- [ ] **Step 1: Failing tests:**

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class PatrolTests
{
    private static Weapon Gun() => new() { Damage = 5, Range = Fix.FromInt(3), CooldownTicks = 4 };

    [Fact]
    public void Patrol_Loops_Between_Both_Points()
    {
        var w = new SimWorld(new MapGrid(30, 30), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        w.Step(new Command[] { new PatrolCommand(0, new[] { id }, w.Map.CellCenter(15, 5)) });
        bool reachedB = false, returnedA = false;
        for (int i = 0; i < 400; i++)
        {
            w.Step(System.Array.Empty<Command>());
            var (cx, _) = w.Map.WorldToCell(w.GetUnit(id)!.Position);
            if (cx == 15) reachedB = true;
            if (reachedB && cx == 5) returnedA = true;
        }
        Assert.True(reachedB, "never reached patrol point B");
        Assert.True(returnedA, "never returned to patrol point A");
        Assert.True(w.GetUnit(id)!.IsPatrolling); // still looping
    }

    [Fact]
    public void Patrolling_Unit_Engages_And_Resumes()
    {
        var w = new SimWorld(new MapGrid(30, 30), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        var victim = w.SpawnUnit(1, w.Map.CellCenter(10, 6), Fix.FromFraction(1, 2), 10);
        w.Step(new Command[] { new PatrolCommand(0, new[] { id }, w.Map.CellCenter(15, 5)) });
        for (int i = 0; i < 100; i++) w.Step(System.Array.Empty<Command>());
        Assert.Null(w.GetUnit(victim));            // engaged and killed en route
        Assert.True(w.GetUnit(id)!.IsPatrolling);  // resumed the loop
    }

    [Fact]
    public void Passive_Patrol_Walks_Without_Engaging()
    {
        var w = new SimWorld(new MapGrid(30, 30), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        var bystander = w.SpawnUnit(1, w.Map.CellCenter(10, 6), Fix.FromFraction(1, 2), 10);
        w.Step(new Command[] { new SetStanceCommand(0, new[] { id }, Stance.Passive) });
        w.Step(new Command[] { new PatrolCommand(0, new[] { id }, w.Map.CellCenter(15, 5)) });
        for (int i = 0; i < 100; i++) w.Step(System.Array.Empty<Command>());
        Assert.NotNull(w.GetUnit(bystander));      // untouched
    }

    [Fact]
    public void New_Order_Cancels_Patrol()
    {
        var w = new SimWorld(new MapGrid(30, 30), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        w.Step(new Command[] { new PatrolCommand(0, new[] { id }, w.Map.CellCenter(15, 5)) });
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, w.Map.CellCenter(20, 20)) });
        Assert.False(w.GetUnit(id)!.IsPatrolling);
    }
}
```

- [ ] **Step 2:** Compile failure.
- [ ] **Step 3: Implement.**
  - `Unit.cs`: `public bool IsPatrolling { get; set; }`, `public FixVec PatrolA { get; set; }`, `public FixVec PatrolB { get; set; }`.
  - `Commands.cs`: `public sealed record PatrolCommand(int PlayerId, int[] UnitIds, FixVec Target) : Command(PlayerId);`
  - Apply case (mirror AttackMoveCommand): per owned unit — `PatrolA = u.Position; PatrolB = pc.Target; IsPatrolling = true; AttackMoveDest = pc.Target; IsAttackMoving = u.Weapon is not null && u.Stance != Stance.Passive; HasMoveOrder = true; MoveTarget = pc.Target; Path = field(to target); PathVersion = Map.Version; AttackTargetId = 0; HasAnchor = false; HarvestPhase = None;`. Passive/weaponless patrol = plain looping move (IsAttackMoving false).
  - Leg swap: in `SimWorld.Combat.cs` the attack-move arrival branch (`u.Position.Equals(u.AttackMoveDest)` → `IsAttackMoving = false`) — when `u.IsPatrolling`, instead swap: `(PatrolA, PatrolB) = (PatrolB, PatrolA); AttackMoveDest = PatrolB; HasMoveOrder = true; MoveTarget = PatrolB; Path = GetField(...); keep IsAttackMoving`. ALSO handle the plain-move (passive) patrol case: passive patrol never enters the attack-move branch, so add the swap to `MoveUnits` arrival as well — when a unit with `IsPatrolling && !IsAttackMoving` arrives (move order cleared), issue the next leg. NOTE: collision arrival-relaxation can clear the move order one cell early — the swap must trigger on order-cleared-while-patrolling, not on exact position, so a body-blocked patroller keeps looping.
  - Cancellation: Move/Attack/AttackMove/Harvest/SetStance? — Move, Attack, AttackMove, Harvest Apply cases set `IsPatrolling = false` (SetStance does NOT cancel patrol). DestroyCommand irrelevant.
  - `StateHasher.cs`: hash `IsPatrolling`, `PatrolA.X/Y.Raw`, `PatrolB.X/Y.Raw`.
- [ ] **Step 4: Golden** — hash function changed (+ no scenario patrol yet): cross-run green, re-pin same commit, both configs.
- [ ] **Step 5:** Full suite both configs. **Commit** `feat: patrol orders - loop, engage per stance, resume (re-pin golden)`

---

### Task 5: Rally points

**Files:** Modify `Building.cs`, `Commands.cs`, `SimWorld.cs` (Apply), `SimWorld.Buildings.cs` (UpdateProduction), `StateHasher.cs`; Test `tests/SimCore.Tests/RallyDestroyTests.cs`

- [ ] **Step 1: Failing tests:**

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class RallyDestroyTests
{
    private static (SimWorld w, int rax) TrainerWorld()
    {
        var w = new SimWorld(new MapGrid(30, 30), seed: 1);
        w.Players[0].Minerals = 500;
        w.Players[0].SupplyCap = 10;
        var rax = w.AddCompletedBuilding(0, ReferenceSpecs.Barracks, 5, 5);
        return (w, rax);
    }

    [Fact]
    public void Trained_Unit_Moves_To_Rally()
    {
        var (w, rax) = TrainerWorld();
        w.Step(new Command[] {
            new SetRallyCommand(0, rax, w.Map.CellCenter(20, 20)),
            new TrainCommand(0, rax, ReferenceSpecs.Trooper),
        });
        for (int i = 0; i < ReferenceSpecs.Trooper.BuildTimeTicks + 200; i++) w.Step(System.Array.Empty<Command>());
        var trooper = w.Units[^1];
        var (cx, cy) = w.Map.WorldToCell(trooper.Position);
        Assert.Equal((20, 20), (cx, cy));
    }

    [Fact]
    public void Clear_Rally_Stops_Auto_Move()
    {
        var (w, rax) = TrainerWorld();
        w.Step(new Command[] { new SetRallyCommand(0, rax, w.Map.CellCenter(20, 20)) });
        w.Step(new Command[] { new SetRallyCommand(0, rax, default, Clear: true) });
        Assert.False(w.GetBuilding(rax)!.HasRally);
        w.Step(new Command[] { new TrainCommand(0, rax, ReferenceSpecs.Trooper) });
        for (int i = 0; i < ReferenceSpecs.Trooper.BuildTimeTicks + 5; i++) w.Step(System.Array.Empty<Command>());
        Assert.False(w.Units[^1].HasMoveOrder); // spawned idle at the perimeter
    }

    [Fact]
    public void Enemy_Cannot_Set_My_Rally()
    {
        var (w, rax) = TrainerWorld();
        w.Step(new Command[] { new SetRallyCommand(1, rax, w.Map.CellCenter(20, 20)) });
        Assert.False(w.GetBuilding(rax)!.HasRally);
    }
}
```

- [ ] **Step 2:** Compile failure.
- [ ] **Step 3: Implement.**
  - `Building.cs`: `public bool HasRally { get; set; }`, `public FixVec RallyPoint { get; set; }` (needs `using SimCore.Math;`).
  - `Commands.cs`: `public sealed record SetRallyCommand(int PlayerId, int BuildingId, FixVec Target, bool Clear = false) : Command(PlayerId);`
  - Apply case: building exists + owned → `Clear ? (HasRally = false) : (HasRally = true, RallyPoint = Target)`.
  - `UpdateProduction` in `SimWorld.Buildings.cs`: after a successful spawn, if `b.HasRally` issue the new unit a move (same pattern as Apply's MoveCommand: GetField to the rally cell, HasMoveOrder/MoveTarget/Path/PathVersion).
  - `StateHasher.cs`: in the building loop hash `HasRally`, `RallyPoint.X/Y.Raw`.
- [ ] **Step 4: Golden** — hash function changed: cross-run green, re-pin same commit, both configs.
- [ ] **Step 5:** Full suite both configs. **Commit** `feat: building rally points (re-pin golden)`

---

### Task 6: DestroyCommand

**Files:** Modify `Commands.cs`, `SimWorld.cs` (Apply); Test append `RallyDestroyTests.cs`

- [ ] **Step 1: Failing tests** (append):

```csharp
    [Fact]
    public void Destroy_Kills_Own_Units_And_Releases_Supply()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        w.Players[0].SupplyCap = 10;
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), ReferenceSpecs.Trooper);
        var used = w.Players[0].SupplyUsed;
        w.Step(new Command[] { new DestroyCommand(0, new[] { id }) });
        Assert.Null(w.GetUnit(id));
        Assert.Equal(used - ReferenceSpecs.Trooper.SupplyCost, w.Players[0].SupplyUsed);
        Assert.Equal(0, w.OccupantAt(5, 5)); // occupancy released
    }

    [Fact]
    public void Destroy_Kills_Own_Building_And_Restores_Passability()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var b = w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 5, 5);
        w.Step(new Command[] { new DestroyCommand(0, new[] { b }) });
        Assert.Null(w.GetBuilding(b));
        Assert.True(w.Map.IsPassable(5, 5));
    }

    [Fact]
    public void Destroy_Ignores_Enemy_Ids()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var theirs = w.SpawnUnit(1, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new DestroyCommand(0, new[] { theirs }) });
        Assert.NotNull(w.GetUnit(theirs));
    }
```
(Check `RemoveDead` actually releases supply — read it; if supply release on death isn't implemented for units, that's existing behavior to verify, not to invent: adjust the first test's supply assertion to match ACTUAL death semantics, and note it.)

- [ ] **Step 2:** Compile failure. **Step 3: Implement.** `Commands.cs`: `public sealed record DestroyCommand(int PlayerId, int[] Ids) : Command(PlayerId);` Apply case: per id — owned unit → `u.Hp = 0`; else owned building → `b.Hp = 0`; else ignore. The death sweeps do the rest.
- [ ] **Step 4: Golden** — no hash-function change, no scenario change: golden should be UNCHANGED (verify; if it fails something is wrong).
- [ ] **Step 5:** Full suite both configs. **Commit** `feat: DestroyCommand - Del-key self-destruct for owned entities`

---

### Task 7: Determinism scenario v5 + full-suite audit

**Files:** Modify `tests/SimCore.Tests/DeterminismTests.cs`

- [ ] **Step 1:** Extend the scenario to exercise: an idle AutoAttack defender meeting a raid (already implicit — verify idle acquisition fires: the fog snipers/runners from v4b now auto-engage; confirm via temp instrumentation that at least one idle acquisition happens), a Defend-stance unit (SetStanceCommand early), a patrol crossing near the gap (PatrolCommand ~tick 60), a rally point + train (SetRallyCommand before the tick-250 trains), and a DestroyCommand (~tick 400 on an own unit). Only ADD script entries / SetStance on existing units — spawn order unchanged.
- [ ] **Step 2:** Temp instrumentation proving each new mechanic fired (idle-acquisition count > 0, patrol leg swap > 0, rallied unit moved, destroyed unit gone) — then remove.
- [ ] **Step 3:** Cross-run green → re-pin golden, both configs, full suite green.
- [ ] **Step 4: Commit** `test: determinism scenario v5 - stances, patrol, rally, destroy under the net (re-pin golden)`

---

### Task 8: Godot UI

**Files:** Modify `godot/scripts/Hud.cs`, `CommandController.cs`, `SelectionController.cs`, `UnitView.cs`, `BuildingView.cs`, `ViewSync.cs`, `godot/README.md`

Environment: `$env:GODOT = "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe"`; headless smoke `--quit-after 60`; commit any new `.uid` files; no stray files.

- [ ] **Step 1: Stance buttons** (`Hud.cs`): in `RebuildButtons`, when ≥1 owned unit with a weapon is selected, add three buttons `Auto`/`Defend`/`Passive` issuing `SetStanceCommand(p, selectedIds, Stance.X)`. Highlight = prefix the label of the stance shared by ALL selected armed units with `>` (mixed → no marker; recompute on rebuild). Buttons rebuild on tick-key changes already (`CheckButtonRelevance`) — include the common-stance value in `_lastButtonKey` so the marker updates after issuing.
- [ ] **Step 2: Patrol mode** (`CommandController.cs`): mirror the A-key attack-move pattern — `Key.P` (Echo:false, `_ghostSpec is null`, units selected) arms `_patrolArmed`; armed left-click issues `PatrolCommand(p, ids, ToSim(click))` and consumes; Esc clears it (add to the existing Esc case).
- [ ] **Step 3: Rally** (`CommandController.cs` ContextOrder + early branch): right-click when NO units selected but an owned production building is selected (`_sel.SelectedBuilding != 0`): clicked cell inside that building's footprint → `SetRallyCommand(p, b.Id, default, Clear: true)`; else → `SetRallyCommand(p, b.Id, ToSim(click))`. (ContextOrder currently early-returns when no units are selected — insert the rally branch before that return; read SelectionController for the building id.)
- [ ] **Step 4: Del** (`SelectionController.cs`): `Key.Delete` (Pressed, Echo:false) with anything owned selected → build `ids = SelectedUnits ∪ {SelectedBuilding if != 0}` → `_runner.Enqueue(new DestroyCommand(ControlledPlayer, ids))`, consume the event. (SelectionController already holds `_runner`.)
- [ ] **Step 5: Patrol glyph** (`UnitView.cs` + `ViewSync.cs`): `SyncTick` copies `u.IsPatrolling` to a field; `_Draw` renders a small cyan double-arrow (two 6px triangles) above the unit when patrolling and selected-or-owned.
- [ ] **Step 6: Rally flag** (`BuildingView.cs` + `ViewSync.cs` + `SelectionController`): BuildingView gains `Selected` (set from SelectionController.ApplyHighlights via `_view.Buildings`) plus `RallyPx` (Vector2?) copied in `SyncTick` from `b.HasRally ? RenderMath.ToPx(b.RallyPoint) : null`. `_Draw` (when Selected && RallyPx != null): dashed line from footprint center to rally + a small flag triangle. NOTE positions: BuildingView draws in LOCAL space — convert rally world px to local by subtracting `Position`.
- [ ] **Step 7:** README controls: `P + left click | patrol`, `Del | destroy selected (own)`, `right click (building selected) | set/clear rally`. Build clean; headless smoke; full suite both configs; commit `feat: behaviors UI - stance buttons, patrol, rally flag, Del destroy`.

---

## Self-review (applied)

- Spec coverage: stances T1-3, patrol T4, rally T5, destroy T6, scenario T7, UI T8. Spec's "fog still gates idle acquisition" is inherited free (AcquireTarget is already fog-gated) — StanceTests' engage tests implicitly cover it; T7 instrumentation confirms.
- Type consistency: `Stance` enum in Unit.cs; `SetStanceCommand(int, int[], Stance)`; `PatrolCommand(int, int[], FixVec)`; `SetRallyCommand(int, int, FixVec, bool Clear = false)`; `DestroyCommand(int, int[])` — used consistently across tasks.
- Engineer-verify flags: RemoveDead supply semantics (T6), arrival-relaxation interaction with patrol leg swap (T4 — swap on order-cleared, not exact arrival), existing-test triage policy (T2).
- Golden protocol: re-pins expected in T1, T2, T4, T5, T7 (same-commit, cross-run-first, both configs); T3 conditional; T6 must NOT change.
