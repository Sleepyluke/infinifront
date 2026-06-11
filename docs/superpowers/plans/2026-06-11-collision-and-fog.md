# Plan 2c: Unit Collision & Fog of War Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Units occupy cells exclusively (hard cell blocking, per playtest feedback — units currently stack), and fog of war hides unseen enemies with a debug toggle (F3) that disables both sim vision-gating and the fog overlay.

**Architecture:** Both features are sim-core (deterministic fixed-point, TDD, golden-hash protocol) plus thin Godot rendering. Collision: an occupancy grid (one unit per cell) enforced in `MoveUnits`; movement into an occupied cell stops at the boundary. Fog: per-player visible/explored grids recomputed each tick from unit/building sight radii; target acquisition and attack commands see only visible enemies; `FogEnabled` is a runtime debug flag excluded from the state hash (documented exception — debug-only, never toggled in multiplayer).

**Tech Stack:** C# net8.0, xUnit, fixed-point `Fix` math. Godot 4.6 .NET for the overlay. Repo `C:\Users\lssha\llm-rts`; `$env:Path += ';C:\Program Files\dotnet'` in fresh shells; Godot via the winget .NET binary (never Steam).

**Behavior changes are intentional:** collision and fog-gating change unit behavior, so `GoldenTrajectoryHash` gets re-pinned (Tasks 4 and 9, same commit as the change, per protocol). Several existing tests that implicitly assume stacking or omniscient acquisition will need updates — adjust their setups (spawn on distinct cells / pre-reveal) rather than weakening assertions.

---

## File structure

```
src/SimCore/Sim/SimWorld.Occupancy.cs   NEW  occupancy grid, claim/release, queries
src/SimCore/Sim/SimWorld.cs             MOD  MoveUnits blocking; spawn claims; FogEnabled flag
src/SimCore/Sim/SimWorld.Vision.cs      NEW  per-player vision grids, UpdateVision, IsVisibleTo
src/SimCore/Sim/SimWorld.Combat.cs      MOD  acquisition + chase gated on visibility
src/SimCore/Sim/Specs.cs                MOD  UnitSpec.SightRange, BuildingSpec.SightRange (defaults)
src/SimCore/Sim/ReferenceSpecs.cs       MOD  sight values
src/SimCore/Sim/StateHasher.cs          MOD  hash occupancy? NO (derived); hash vision? NO (derived); doc both
tests/SimCore.Tests/OccupancyTests.cs   NEW
tests/SimCore.Tests/VisionTests.cs      NEW
tests/SimCore.Tests/FogGatingTests.cs   NEW
tests/SimCore.Tests/DeterminismTests.cs MOD  re-pin (twice: after collision, after fog)
godot/scripts/FogView.cs               NEW  per-cell overlay (black unexplored / grey explored / clear visible)
godot/scripts/Minimap.cs               MOD  honor fog for enemy dots
godot/scripts/Main.cs                  MOD  FogView wiring; F3 toggle
godot/README.md                        MOD  F3 in controls table
```

**Derived-state hashing rule:** occupancy is recomputed from unit positions and vision from positions+specs every tick — both are derived caches like `Unit.Path`, so they are EXCLUDED from `StateHasher` (document in its convention comment). `FogEnabled` is also excluded: it is a debug-only toggle, documented as "never toggle mid-match in anything that must stay in lockstep."

---

### Task 1: Occupancy grid

**Files:** Create `src/SimCore/Sim/SimWorld.Occupancy.cs`; Test `tests/SimCore.Tests/OccupancyTests.cs`

- [ ] **Step 1: Failing tests** at `tests/SimCore.Tests/OccupancyTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class OccupancyTests
{
    [Fact]
    public void Spawned_Unit_Claims_Its_Cell()
    {
        var w = new SimWorld(new MapGrid(10, 10), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(3, 3), Fix.FromFraction(1, 2), 50);
        Assert.Equal(id, w.OccupantAt(3, 3));
        Assert.Equal(0, w.OccupantAt(4, 4)); // empty
    }

    [Fact]
    public void Dead_Unit_Releases_Its_Cell()
    {
        var w = new SimWorld(new MapGrid(10, 10), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(3, 3), Fix.FromFraction(1, 2), 50);
        w.GetUnit(id)!.Hp = 0;
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.OccupantAt(3, 3));
    }

    [Fact]
    public void Spawn_On_Occupied_Cell_Is_Rejected()
    {
        var w = new SimWorld(new MapGrid(10, 10), seed: 1);
        w.SpawnUnit(0, w.Map.CellCenter(3, 3), Fix.FromFraction(1, 2), 50);
        var second = w.SpawnUnit(0, w.Map.CellCenter(3, 3), Fix.FromFraction(1, 2), 50);
        Assert.Equal(0, second); // 0 = rejected (no unit id is ever 0)
        Assert.Single(w.Units);
    }
}
```

- [ ] **Step 2:** `dotnet test --filter OccupancyTests` — compile FAILURE (`OccupantAt` missing).

- [ ] **Step 3: Implement** `src/SimCore/Sim/SimWorld.Occupancy.cs`:

```csharp
namespace SimCore.Sim;

public sealed partial class SimWorld
{
    // unit id per cell, 0 = empty. Derived from unit positions — EXCLUDED from StateHasher.
    private int[] _occupancy = System.Array.Empty<int>();

    private void EnsureOccupancy()
    {
        if (_occupancy.Length != Map.Width * Map.Height)
            _occupancy = new int[Map.Width * Map.Height];
    }

    public int OccupantAt(int cx, int cy) =>
        cx < 0 || cy < 0 || cx >= Map.Width || cy >= Map.Height ? 0
        : _occupancy[cy * Map.Width + cx];

    private void ClaimCell(int cx, int cy, int unitId) => _occupancy[cy * Map.Width + cx] = unitId;
    private void ReleaseCell(int cx, int cy, int unitId)
    {
        var i = cy * Map.Width + cx;
        if (_occupancy[i] == unitId) _occupancy[i] = 0; // only the claimant releases
    }
}
```

Then wire into existing code (read each site first):
- `Spawn(...)` in `SimWorld.cs`: call `EnsureOccupancy()`; compute the spawn cell via `Map.WorldToCell(pos)`; if `OccupantAt(cell) != 0`, return 0 WITHOUT creating the unit (callers: `FindSpawnCell` in production already picks free-perimeter cells by passability — it must now also check `OccupantAt == 0`; update it). On success, `ClaimCell`.
- `RemoveDead()` in `SimWorld.cs`: `ReleaseCell` for each removed unit's current cell.
- NOTE: `SimWorld` constructor or first spawn must init the array — `EnsureOccupancy()` at the top of `Spawn` and `Step` covers both.

- [ ] **Step 4:** Full suite. EXPECT some existing tests to fail where they spawn two units on the same cell (find them; fix their setups to use distinct cells — assertions unchanged). DeterminismTests must still pass UNCHANGED at this point (no movement blocking yet; scenario spawns are on distinct cells — verify; if the scenario itself stacks spawns, fix the scenario AND re-pin in Task 4 instead, noting it here).
- [ ] **Step 5: Commit** `feat: occupancy grid - one unit per cell (claims at spawn, release at death)`

---

### Task 2: Movement blocking

Units may not enter a cell claimed by another unit. On crossing a cell boundary, the mover atomically releases its old cell and claims the new one; if the destination cell is taken, the unit stops at the boundary this tick (retries next tick — natural queueing).

**Files:** Modify `src/SimCore/Sim/SimWorld.cs` (`MoveUnits`); Test: append to `OccupancyTests.cs`

- [ ] **Step 1: Failing tests** (append):

```csharp
    [Fact]
    public void Unit_Cannot_Enter_Occupied_Cell()
    {
        var w = new SimWorld(new MapGrid(10, 10), seed: 1);
        var blocker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50);
        var mover = w.SpawnUnit(0, w.Map.CellCenter(3, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new MoveCommand(0, new[] { mover }, w.Map.CellCenter(5, 5)) });
        for (int i = 0; i < 50; i++) w.Step(System.Array.Empty<Command>());
        var (cx, cy) = w.Map.WorldToCell(w.GetUnit(mover)!.Position);
        Assert.NotEqual((5, 5), (cx, cy));               // never entered
        Assert.Equal(blocker, w.OccupantAt(5, 5));        // blocker still owns it
    }

    [Fact]
    public void Units_Queue_Through_A_Corridor()
    {
        // 3-wide map with a 1-wide corridor: two units ordered through must both arrive (one waits)
        var g = new MapGrid(7, 3);
        for (int x = 0; x < 7; x++) { g.SetPassable(x, 0, false); g.SetPassable(x, 2, false); }
        var w = new SimWorld(g, seed: 1);
        var a = w.SpawnUnit(0, w.Map.CellCenter(0, 1), Fix.FromFraction(1, 2), 50);
        var b = w.SpawnUnit(0, w.Map.CellCenter(1, 1), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new MoveCommand(0, new[] { a, b }, w.Map.CellCenter(6, 1)) });
        for (int i = 0; i < 200; i++) w.Step(System.Array.Empty<Command>());
        var (ax, _) = w.Map.WorldToCell(w.GetUnit(a)!.Position);
        var (bx, _) = w.Map.WorldToCell(w.GetUnit(b)!.Position);
        Assert.True(ax >= 5, $"a stalled at x={ax}");
        Assert.True(bx >= 5, $"b stalled at x={bx}");
        Assert.NotEqual(w.Map.WorldToCell(w.GetUnit(a)!.Position), w.Map.WorldToCell(w.GetUnit(b)!.Position));
    }

    [Fact]
    public void Moving_Unit_Updates_Occupancy_As_It_Crosses_Cells()
    {
        var w = new SimWorld(new MapGrid(10, 10), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, w.Map.CellCenter(7, 5)) });
        for (int i = 0; i < 100; i++) w.Step(System.Array.Empty<Command>());
        var (cx, cy) = w.Map.WorldToCell(w.GetUnit(id)!.Position);
        Assert.Equal(id, w.OccupantAt(cx, cy));   // owns where it stands
        Assert.Equal(0, w.OccupantAt(2, 5));      // released origin
    }
```

- [ ] **Step 2:** Verify they fail (units pass through / occupancy stale).
- [ ] **Step 3: Implement.** Read `MoveUnits` in `SimWorld.cs` fully first. The movement step computes a new position from the flow-field direction. Add, per unit, after computing the tentative new position:

```csharp
            var (ocx, ocy) = Map.WorldToCell(u.Position);
            var (ncx, ncy) = Map.WorldToCell(newPos);
            if ((ncx != ocx || ncy != ocy))
            {
                var occ = OccupantAt(ncx, ncy);
                if (occ != 0 && occ != u.Id)
                {
                    // destination cell taken — hold at current position this tick (retry next)
                    continue; // skip the position write for this unit
                }
                ReleaseCell(ocx, ocy, u.Id);
                ClaimCell(ncx, ncy, u.Id);
            }
            u.Position = newPos;
```

Adapt names to the real loop body (there may be already-existing position-write logic, arrival checks, etc. — integrate, don't duplicate; the arrival/stop logic must still run when blocked so units don't burn move orders). Iteration over `_units` in spawn order = stable priority (earlier unit wins contested cells) = deterministic.

- [ ] **Step 4:** Full suite. Existing movement/combat tests may shift (units that used to stack now queue): fix setups (spacing) where the test's INTENT is unaffected; where blocking changes legit expected positions, update expectations deliberately. DeterminismTests `Trajectory_Hash_Matches_Golden_Constant` WILL fail — DO NOT re-pin yet (Task 4 re-pins once, after spawn-spreading lands). Mark it `[Fact(Skip="re-pin in collision task 4")]` TEMPORARILY only if needed to keep the suite readable during Tasks 2-3; remove the skip in Task 4.
- [ ] **Step 5: Commit** `feat: hard cell blocking - movement respects occupancy, stable-priority queueing`

---

### Task 3: Stationary spread — arrivals don't pile onto one destination cell

With blocking, multiple units ordered to one point jam adjacent forever retrying. Add arrival relaxation: a unit whose destination cell is occupied (by another unit) and which is adjacent to that cell considers itself arrived (clears its move order).

**Files:** Modify `src/SimCore/Sim/SimWorld.cs` (`MoveUnits` arrival logic); Test: append to `OccupancyTests.cs`

- [ ] **Step 1: Failing test** (append):

```csharp
    [Fact]
    public void Group_Ordered_To_One_Point_Settles_On_Adjacent_Cells()
    {
        var w = new SimWorld(new MapGrid(12, 12), seed: 1);
        var ids = new int[4];
        for (int i = 0; i < 4; i++)
            ids[i] = w.SpawnUnit(0, w.Map.CellCenter(2 + i, 2), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new MoveCommand(0, ids, w.Map.CellCenter(6, 6)) });
        for (int i = 0; i < 300; i++) w.Step(System.Array.Empty<Command>());
        foreach (var id in ids)
            Assert.False(w.GetUnit(id)!.HasMoveOrder, $"unit {id} never settled");
        // all on distinct cells, all within chebyshev 2 of the target
        var cells = ids.Select(id => w.Map.WorldToCell(w.GetUnit(id)!.Position)).ToHashSet();
        Assert.Equal(4, cells.Count);
        foreach (var (cx, cy) in cells)
            Assert.True(System.Math.Abs(cx - 6) <= 2 && System.Math.Abs(cy - 6) <= 2, $"({cx},{cy}) too far");
    }
```
(add `using System.Linq;` to the test file.)

- [ ] **Step 2:** Verify it fails (units retry forever, HasMoveOrder stays true).
- [ ] **Step 3: Implement** in the blocked branch of Task 2's code: when blocked AND the blocked unit's move target cell == the contested cell or is within chebyshev 1 of the unit's current cell, clear the move order (`HasMoveOrder = false; Path = null;`). Also handle the second-ring case: if blocked and the blocker itself has no move order and shares our destination cell target (`Map.WorldToCell(MoveTarget)` equal), treat as arrived. Keep it minimal — the test defines sufficiency; don't build formation logic.
- [ ] **Step 4:** Full suite (golden still pending re-pin).
- [ ] **Step 5: Commit** `feat: arrival relaxation - groups settle on adjacent cells instead of jamming`

---

### Task 4: Determinism scenario v4a — collision under the net, re-pin

**Files:** Modify `tests/SimCore.Tests/DeterminismTests.cs`

- [ ] **Step 1:** Read the scenario; ensure its spawns are on distinct cells (fix if not). Add a scripted phase that exercises collision: order two squads through the same gap around tick 100 (collision queueing now runs under the 500-tick hash net). Remove any temporary Skip from Task 2.
- [ ] **Step 2:** Run; take the new combined hash from the failure output; re-pin `GoldenTrajectoryHash`.
- [ ] **Step 3:** `dotnet test` AND `dotnet test --configuration Release` — same constant, all green (Debug/Release disagreement = BLOCKED).
- [ ] **Step 4: Commit** `test: determinism scenario v4a - collision queueing under the net (re-pin golden)`

---

### Task 5: Sight ranges on specs

**Files:** Modify `src/SimCore/Sim/Specs.cs`, `src/SimCore/Sim/ReferenceSpecs.cs`; Test: append to `tests/SimCore.Tests/ReferenceSpecsTests.cs`

- [ ] **Step 1: Failing test** (append to ReferenceSpecsTests):

```csharp
    [Fact]
    public void Everything_Has_Positive_Sight()
    {
        foreach (var s in new[] { ReferenceSpecs.Fabber, ReferenceSpecs.Trooper, ReferenceSpecs.Outrider, ReferenceSpecs.Tank })
            Assert.True(s.SightRange > 0);
        Assert.True(ReferenceSpecs.Depot.SightRange > 0);
        Assert.True(ReferenceSpecs.Barracks.SightRange > 0);
    }

    [Fact]
    public void Sight_Reaches_At_Least_Weapon_Range()
    {
        foreach (var s in new[] { ReferenceSpecs.Trooper, ReferenceSpecs.Outrider, ReferenceSpecs.Tank })
            Assert.True(Fix.FromInt(s.SightRange) >= s.Weapon!.Range);
    }
```

- [ ] **Step 2:** Compile failure. **Step 3:** Add `int SightRange = 7` param to `UnitSpec`, `int SightRange = 8` to `BuildingSpec` (defaults keep all existing construction sites compiling). ReferenceSpecs: Fabber 6, Trooper 7, Outrider 9, Tank 7; Depot 9, Barracks 8. (Placeholders like everything else.)
- [ ] **Step 4:** Full suite green; golden unchanged (sight is inert data until Task 6). **Step 5: Commit** `feat: sight ranges on unit and building specs`

---

### Task 6: Vision grids

Per-player boolean grids: `visible` (recomputed each tick) and `explored` (sticky). Chebyshev-circle stamp around each unit/building (cell distance² ≤ range² in cells, integer math only).

**Files:** Create `src/SimCore/Sim/SimWorld.Vision.cs`; Modify `SimWorld.cs` (`Step` order: `UpdateVision()` FIRST, before `UpdateCombat`); Test: `tests/SimCore.Tests/VisionTests.cs`

- [ ] **Step 1: Failing tests:**

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class VisionTests
{
    private static SimWorld World() => new(new MapGrid(30, 30), seed: 1);

    [Fact]
    public void Unit_Sees_Its_Surroundings_Not_The_Far_Map()
    {
        var w = World();
        w.SpawnUnit(0, w.Map.CellCenter(5, 5), ReferenceSpecs.Trooper); // sight 7
        w.Step(System.Array.Empty<Command>());
        Assert.True(w.IsVisibleTo(0, 5, 5));
        Assert.True(w.IsVisibleTo(0, 11, 5));   // distance 6 <= 7
        Assert.False(w.IsVisibleTo(0, 25, 25)); // far corner
        Assert.False(w.IsVisibleTo(1, 5, 5));   // other player sees nothing
    }

    [Fact]
    public void Vision_Moves_With_The_Unit_And_Leaves_Explored_Behind()
    {
        var w = World();
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), ReferenceSpecs.Outrider); // sight 9, fast
        w.Step(System.Array.Empty<Command>());
        Assert.True(w.IsVisibleTo(0, 5, 5));
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, w.Map.CellCenter(25, 5)) });
        for (int i = 0; i < 120; i++) w.Step(System.Array.Empty<Command>());
        Assert.False(w.IsVisibleTo(0, 5, 5));   // moved away — no longer visible
        Assert.True(w.IsExploredBy(0, 5, 5));   // but remembered
        Assert.True(w.IsVisibleTo(0, 25, 5));
    }

    [Fact]
    public void Buildings_Grant_Vision()
    {
        var w = World();
        w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 10, 10); // sight 9
        w.Step(System.Array.Empty<Command>());
        Assert.True(w.IsVisibleTo(0, 15, 10));
        Assert.False(w.IsVisibleTo(0, 25, 10));
    }

    [Fact]
    public void FogDisabled_Sees_Everything()
    {
        var w = World();
        w.FogEnabled = false;
        w.Step(System.Array.Empty<Command>());
        Assert.True(w.IsVisibleTo(0, 25, 25));
        Assert.True(w.IsVisibleTo(1, 0, 0));
    }
}
```

- [ ] **Step 2:** Compile failure. **Step 3: Implement** `SimWorld.Vision.cs`:

```csharp
namespace SimCore.Sim;

public sealed partial class SimWorld
{
    /// <summary>Debug-only toggle. Default true. EXCLUDED from StateHasher by design:
    /// never toggle mid-match in any context that must stay in lockstep.</summary>
    public bool FogEnabled { get; set; } = true;

    // visible recomputed each tick; explored is sticky. Derived state — EXCLUDED from hash.
    private bool[][] _visible = System.Array.Empty<bool[]>();   // [player][cell]
    private bool[][] _explored = System.Array.Empty<bool[]>();

    public bool IsVisibleTo(int player, int cx, int cy) =>
        !FogEnabled || (InBounds(cx, cy) && _visible.Length > player && _visible[player][cy * Map.Width + cx]);

    public bool IsExploredBy(int player, int cx, int cy) =>
        !FogEnabled || (InBounds(cx, cy) && _explored.Length > player && _explored[player][cy * Map.Width + cx]);

    private bool InBounds(int cx, int cy) => cx >= 0 && cy >= 0 && cx < Map.Width && cy < Map.Height;

    private void UpdateVision()
    {
        int players = _players.Count, cells = Map.Width * Map.Height;
        if (_visible.Length != players)
        {
            _visible = new bool[players][];
            _explored = new bool[players][];
            for (int p = 0; p < players; p++) { _visible[p] = new bool[cells]; _explored[p] = new bool[cells]; }
        }
        for (int p = 0; p < players; p++) System.Array.Clear(_visible[p]);

        foreach (var u in _units)
        {
            var (cx, cy) = Map.WorldToCell(u.Position);
            Stamp(u.OwnerId, cx, cy, u.SightRange);
        }
        foreach (var b in _buildings)
            Stamp(b.OwnerId, b.CellX + b.Spec.Width / 2, b.CellY + b.Spec.Height / 2, b.Spec.SightRange);
    }

    private void Stamp(int player, int cx, int cy, int range)
    {
        int r2 = range * range;
        for (int y = System.Math.Max(0, cy - range); y <= System.Math.Min(Map.Height - 1, cy + range); y++)
            for (int x = System.Math.Max(0, cx - range); x <= System.Math.Min(Map.Width - 1, cx + range); x++)
            {
                int dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy > r2) continue;
                var i = y * Map.Width + x;
                _visible[player][i] = true;
                _explored[player][i] = true;
            }
    }
}
```
- `Unit` needs `SightRange` carried from spec at spawn (add `public int SightRange { get; set; }` to Unit.cs, set in `Spawn` from the spec — the legacy non-spec `SpawnUnit` overload uses a default of 7). EXTEND `StateHasher` with `u.SightRange` (it's real per-unit state, hashable — unlike the grids) and note the grid/flag exclusions in the convention doc comment. This changes the hash function → golden re-pins in Task 9 along with gating (do NOT re-pin here; mark skip temporarily if needed, same protocol as Task 2).
- Wire `UpdateVision();` as the FIRST system in `Step` (before `UpdateCombat` — combat reads fresh vision).

- [ ] **Step 4:** Suite (golden pending). **Step 5: Commit** `feat: per-player vision grids with explored memory and FogEnabled debug flag`

---

### Task 7: Fog gating — combat sees only visible enemies

**Files:** Modify `src/SimCore/Sim/SimWorld.Combat.cs`, `SimWorld.cs` (AttackCommand); Test: `tests/SimCore.Tests/FogGatingTests.cs`

- [ ] **Step 1: Failing tests:**

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class FogGatingTests
{
    private static Weapon TestWeapon() =>
        new() { Damage = 5, Range = Fix.FromInt(4), CooldownTicks = 3 };

    [Fact]
    public void AttackMove_Ignores_Enemies_In_Fog()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 1);
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        // enemy outside sight (legacy spawn default sight 7); within acquire range never matters — fog wins
        var enemy = w.SpawnUnit(1, w.Map.CellCenter(30, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new AttackMoveCommand(0, new[] { attacker }, w.Map.CellCenter(10, 5)) });
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.GetUnit(attacker)!.AttackTargetId);
    }

    [Fact]
    public void Explicit_Attack_On_Fogged_Target_Is_Rejected()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 1);
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var enemy = w.SpawnUnit(1, w.Map.CellCenter(30, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new AttackCommand(0, new[] { attacker }, enemy) });
        Assert.Equal(0, w.GetUnit(attacker)!.AttackTargetId);
    }

    [Fact]
    public void Chase_Drops_When_Target_Escapes_Into_Fog()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 1);
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 8), 50, TestWeapon());
        var runner = w.SpawnUnit(1, w.Map.CellCenter(9, 5), Fix.FromFraction(1, 2), 500);
        w.Step(new Command[] { new AttackCommand(0, new[] { attacker }, runner) });
        Assert.Equal(runner, w.GetUnit(attacker)!.AttackTargetId);
        w.Step(new Command[] { new MoveCommand(1, new[] { runner }, w.Map.CellCenter(35, 5)) });
        for (int i = 0; i < 150; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.GetUnit(attacker)!.AttackTargetId); // lost in the fog
    }

    [Fact]
    public void FogDisabled_Restores_Omniscient_Acquisition()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 1) { FogEnabled = false };
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var enemy = w.SpawnUnit(1, w.Map.CellCenter(30, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new AttackCommand(0, new[] { attacker }, enemy) });
        Assert.Equal(enemy, w.GetUnit(attacker)!.AttackTargetId);
    }
}
```
NOTE: first test — pick the enemy distance so it's beyond sight but the test stays meaningful vs `AcquireBonus` acquisition range; (30,5) vs sight 7 is safely fogged. Verify legacy-overload default sight = 7 from Task 6.

- [ ] **Step 2:** Verify failures (acquisition is currently omniscient).
- [ ] **Step 3: Implement.** In `SimWorld.Combat.cs`:
  - `AcquireTarget`: skip enemies (units and buildings) whose cell is not `IsVisibleTo(u.OwnerId, ...)`.
  - Chase/stale-clear: in the per-unit pass, after resolving the target, if the target's cell is not visible to the attacker's owner → `u.AttackTargetId = 0; continue;` (placed after `TryResolveTarget`, before the leash check).
  In `SimWorld.cs` `AttackCommand` case: reject targets whose cell isn't visible to the issuing player (same pattern as the friendly-fire check).
  Buildings: use their footprint-center cell for the visibility check (CenterOf → WorldToCell).
- [ ] **Step 4:** Full suite; existing combat tests that rely on omniscient acquisition need their setups moved into sight range OR `FogEnabled = false` added — choose per test INTENT (combat-mechanics tests: disable fog; targeting tests: keep fog, fix distances). Golden still pending.
- [ ] **Step 5: Commit** `feat: fog gates acquisition, explicit attacks, and chases`

---

### Task 8: StateHasher doc + exclusion notes

**Files:** Modify `src/SimCore/Sim/StateHasher.cs` (doc comment only, plus the Task-6 `SightRange` addition if not already in)

- [ ] **Step 1:** Confirm `u.SightRange` is hashed (Task 6). Update the convention doc comment: occupancy grid, vision grids, and `FogEnabled` are documented exclusions (derived / debug-only respectively).
- [ ] **Step 2:** Full suite. **Step 3: Commit** `docs: StateHasher conventions for occupancy, vision, FogEnabled`

---

### Task 9: Determinism scenario v4b — fog under the net, re-pin

**Files:** Modify `tests/SimCore.Tests/DeterminismTests.cs`

- [ ] **Step 1:** Remove any temporary skips. Extend the scenario: the existing combat phases now run under fog (acquisition behavior changes — that's the point); verify economy/combat still actually execute (temporary instrumentation: counters for damage events and deposits > 0 — then remove).
- [ ] **Step 2:** Re-pin `GoldenTrajectoryHash` (hash function changed in Task 6 + behavior in Task 7).
- [ ] **Step 3:** Both configs green, same constant.
- [ ] **Step 4: Commit** `test: determinism scenario v4b - combat under fog (re-pin golden)`

---

### Task 10: Godot fog overlay + F3 toggle + minimap fog

**Files:** Create `godot/scripts/FogView.cs`; Modify `godot/scripts/Main.cs`, `godot/scripts/Minimap.cs`, `godot/scripts/ViewSync.cs`, `godot/README.md`

- [ ] **Step 1: `godot/scripts/FogView.cs`** — draws per-cell fog for the CONTROLLED player, redrawn per tick and on player switch:

```csharp
using Godot;

namespace LlmRts.Godot;

/// <summary>Fog overlay for the controlled player: black = unexplored,
/// half-dark = explored-but-not-visible, clear = visible. Tick-driven.</summary>
public partial class FogView : Node2D
{
    private SimRunner _runner = null!;
    private SelectionController _sel = null!;

    public void Init(SimRunner runner, SelectionController sel)
    {
        _runner = runner;
        _sel = sel;
        ZIndex = 50; // above world, below HUD (CanvasLayer)
        runner.Ticked += QueueRedraw;
        sel.PlayerSwitched += QueueRedraw;
    }

    public override void _Draw()
    {
        var w = _runner.World;
        if (!w.FogEnabled) return;
        int p = _sel.ControlledPlayer;
        const int px = RenderMath.CellPx;
        var black = new Color(0, 0, 0, 0.95f);
        var dim = new Color(0, 0, 0, 0.45f);
        for (int y = 0; y < w.Map.Height; y++)
            for (int x = 0; x < w.Map.Width; x++)
            {
                if (w.IsVisibleTo(p, x, y)) continue;
                DrawRect(new Rect2(x * px, y * px, px, px),
                    w.IsExploredBy(p, x, y) ? dim : black);
            }
    }
}
```

- [ ] **Step 2: Hide fogged enemy entities.** In `ViewSync.OnTick`, after syncing each unit/building view: `v.Visible = u.OwnerId == controlled || world.IsVisibleTo(controlled, cellOf(u))` — get the controlled player from SelectionController (pass it into ViewSync.Init or expose via Main). Buildings in explored-but-fogged cells: ALSO visible when `IsExploredBy` (classic RTS building memory — buildings don't move; acceptable simplification: show current state rather than a snapshot). Corpse views: leave as-is (brief).
- [ ] **Step 3: F3 toggle** in `Main` (`_UnhandledKeyInput`): `if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 }) Runner.World.FogEnabled = !Runner.World.FogEnabled;` plus a `[FOG OFF]` indicator string surfaced through the existing Hud paused/status label.
- [ ] **Step 4: Minimap:** in the entity-dot loops, skip enemy units in non-visible cells and enemy buildings in non-explored cells; overlay unexplored cells in near-black (cheap: same loop as terrain). Respect `FogEnabled`.
- [ ] **Step 5:** README controls table: add `F3 | toggle fog of war (debug)`.
- [ ] **Step 6:** Build + headless 60-frame smoke clean; full suite both configs.
- [ ] **Step 7: Commit** `feat: fog overlay, fogged-entity hiding, minimap fog, F3 debug toggle`

---

## Self-review notes (applied)

- Plan covers both playtest asks: collision (Tasks 1-4) and fog with debug toggle (Tasks 5-10).
- Golden re-pins: exactly twice (4, 9), each in the same commit as its intentional change, both-config verified — per protocol.
- Hash treatment decided explicitly: occupancy/vision grids excluded (derived), FogEnabled excluded (debug-only, documented), SightRange included (real state).
- Engineer-verify flags: `MoveUnits` loop integration (Task 2), legacy-spawn sight default (Tasks 6/7), existing-test triage policy stated (fix setups, not assertions; choose FogEnabled=false per test intent).
- Known scope cuts (deliberate): no formation movement, no vision-blocking terrain (high ground), no building-snapshot memory, no per-unit fog smoothing/dithering — all post-slice polish.
