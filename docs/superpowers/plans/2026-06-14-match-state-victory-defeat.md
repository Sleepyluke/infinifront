# Match State & Victory/Defeat Implementation Plan (5a)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add deterministic, latched match-outcome state (who has won / lost) to `SimWorld`, so later Plan-5 pieces (CPU, match-flow UI) have an outcome to act on.

**Architecture:** A player is alive iff they own ≥1 building (units never count — StarCraft-style). A new `UpdateMatchState()` phase runs each tick after `RemoveDeadBuildings()`, latching `Phase`/`WinnerId` once ≤1 player remains alive. The outcome is read-only public, folded into `StateHasher` (v6), and golden-re-pinned. The sim does NOT halt on game-over — the outcome is metadata the front-end (5e) reacts to.

**Tech Stack:** C# / .NET 8, xUnit. SimCore (deterministic core) — no floats, all integer/Fix; only commands mutate sim state, but match state is derived in-Step from building ownership.

**Source spec:** `docs/superpowers/specs/2026-06-14-match-state-victory-defeat-design.md`

---

## Conventions for every task

- Run from repo root `C:\Users\lssha\llm-rts`. If `dotnet` missing: bash `export PATH="$PATH:/c/Program Files/dotnet"`.
- Run SimCore tests: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
- **Baseline:** 288 SimCore tests pass at the start of this plan; golden trajectory hash = `5141900307592480923UL`.
- After each commit, confirm `git log --oneline -1` shows your commit.
- End commit messages with `Co-Authored-By: RuFlo <ruv@ruv.net>`.

## Engine facts (verified against source)

- `SimWorld` is `public sealed partial class` (files `SimWorld.cs`, `SimWorld.Buildings.cs`, `SimWorld.Mechanics.cs`, …). Add a new partial file for match state.
- `SimWorld.Step(IReadOnlyList<Command> commands)` (`SimWorld.cs:88`) pipeline ends:
  `… RemoveDead(); RemoveDeadBuildings(); Tick++;` (lines 100-102).
- `private readonly List<Building> _buildings` (`SimWorld.Buildings.cs:7`); `Building.OwnerId` (`init`), `Building.Hp` (`public int Hp { get; set; }`). `RemoveDeadBuildings()` removes buildings with `Hp <= 0` (it continues while `b.Hp > 0`).
- `private readonly PlayerState[] _players` (`SimWorld.cs:20`); `_players.Length` = player count.
- Setup APIs: `public int AddCompletedBuilding(int ownerId, BuildingSpec spec, int cellX, int cellY, string defId = "")` (`SimWorld.Buildings.cs:45`); `public int SpawnUnit(int ownerId, FixVec pos, Fix speedPerTick, int hp, Weapon? weapon = null)` (`SimWorld.cs:35`). `BuildingSpec(int MaxHp, int Width, int Height, int MineralCost, int BuildTimeTicks, int SupplyProvided = 0, bool IsDepot = false, bool CanTrain = false, int SightRange = 8)`.
- `StateHasher.Hash(SimWorld world)` (`StateHasher.cs:31`) folds every mutable field; convention doc-comment at the top. Golden constant + the 3 determinism tests live in `tests/SimCore.Tests/DeterminismTests.cs` (`GoldenTrajectoryHash` at line 236).
- The determinism `Scenario()` gives **player 0** buildings (depot via BuildCommand t=0, rax t=80) and **player 1 only units** — so once match state is hashed, the scenario latches `Over`/`WinnerId==0` at tick 0. This is expected and harmless (see plan Task 2).

## File Structure

- `src/SimCore/Sim/SimWorld.Match.cs` — NEW. `enum MatchPhase`, the `Phase`/`WinnerId` properties, `IsDefeated`, `UpdateMatchState`.
- `src/SimCore/Sim/SimWorld.cs` — MODIFY. One line in `Step` to call `UpdateMatchState()`.
- `src/SimCore/Sim/StateHasher.cs` — MODIFY (Task 2). Fold `Phase` + `WinnerId`; update doc-comment.
- `tests/SimCore.Tests/MatchStateTests.cs` — NEW. Outcome semantics.
- `tests/SimCore.Tests/DeterminismTests.cs` — MODIFY (Task 2). Re-pin golden + scenario doc note.

---

## Task 1: Match-state core + semantics tests

**Files:**
- Create: `src/SimCore/Sim/SimWorld.Match.cs`
- Modify: `src/SimCore/Sim/SimWorld.cs` (Step pipeline)
- Test: `tests/SimCore.Tests/MatchStateTests.cs`

This task does NOT touch `StateHasher`, so the determinism golden test stays green throughout.

- [ ] **Step 1: Write the failing tests**

Create `tests/SimCore.Tests/MatchStateTests.cs`:

```csharp
using System.Collections.Generic;
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class MatchStateTests
{
    private static BuildingSpec Bld() => new(100, 2, 2, 100, 10);
    private static readonly List<Command> Empty = new();

    private static (SimWorld w, int b0, int b1) TwoBaseWorld()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        int b0 = w.AddCompletedBuilding(0, Bld(), 2, 2);
        int b1 = w.AddCompletedBuilding(1, Bld(), 14, 14);
        return (w, b0, b1);
    }

    [Fact]
    public void Both_Players_With_Buildings_Is_InProgress()
    {
        var (w, _, _) = TwoBaseWorld();
        w.Step(Empty);
        Assert.Equal(MatchPhase.InProgress, w.Phase);
        Assert.Equal(-1, w.WinnerId);
        Assert.False(w.IsDefeated(0));
        Assert.False(w.IsDefeated(1));
    }

    [Fact]
    public void Destroying_Last_Building_Eliminates_Player_And_Decides_Winner()
    {
        var (w, _, b1) = TwoBaseWorld();
        w.Step(Empty);
        Assert.Equal(MatchPhase.InProgress, w.Phase);

        w.GetBuilding(b1)!.Hp = 0;
        w.Step(Empty); // RemoveDeadBuildings clears b1, then UpdateMatchState runs

        Assert.True(w.IsDefeated(1));
        Assert.Equal(MatchPhase.Over, w.Phase);
        Assert.Equal(0, w.WinnerId);
    }

    [Fact]
    public void Player_With_Units_But_No_Buildings_Is_Defeated()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        w.AddCompletedBuilding(0, Bld(), 2, 2);
        w.SpawnUnit(1, w.Map.CellCenter(14, 14), Fix.One, 10); // p1: a unit, zero buildings
        w.Step(Empty);
        Assert.True(w.IsDefeated(1));
        Assert.Equal(MatchPhase.Over, w.Phase);
        Assert.Equal(0, w.WinnerId);
    }

    [Fact]
    public void Mutual_Elimination_Same_Tick_Is_A_Draw()
    {
        var (w, b0, b1) = TwoBaseWorld();
        w.Step(Empty);
        w.GetBuilding(b0)!.Hp = 0;
        w.GetBuilding(b1)!.Hp = 0;
        w.Step(Empty);
        Assert.Equal(MatchPhase.Over, w.Phase);
        Assert.Equal(-1, w.WinnerId);
    }

    [Fact]
    public void Outcome_Latches_Once_Decided()
    {
        var (w, _, b1) = TwoBaseWorld();
        w.Step(Empty);
        w.GetBuilding(b1)!.Hp = 0;
        w.Step(Empty);
        Assert.Equal(MatchPhase.Over, w.Phase);
        Assert.Equal(0, w.WinnerId);

        // p1 somehow regains a building — the decided outcome must NOT change.
        w.AddCompletedBuilding(1, Bld(), 14, 14);
        w.Step(Empty);
        Assert.Equal(MatchPhase.Over, w.Phase);
        Assert.Equal(0, w.WinnerId);
    }
}
```

- [ ] **Step 2: Run the tests, expect failure**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
Expected: compile FAILS — `MatchPhase`, `SimWorld.Phase`, `SimWorld.WinnerId`, `SimWorld.IsDefeated` do not exist.

- [ ] **Step 3: Create the match-state partial**

Create `src/SimCore/Sim/SimWorld.Match.cs`:

```csharp
namespace SimCore.Sim;

/// <summary>Match outcome phase. Latches to Over once decided.</summary>
public enum MatchPhase { InProgress, Over }

public sealed partial class SimWorld
{
    /// <summary>Current match outcome. Latches to Over once a winner (or draw) is decided.</summary>
    public MatchPhase Phase { get; private set; } = MatchPhase.InProgress;

    /// <summary>Winner's player id once Over; -1 while InProgress or on a draw (mutual elimination).</summary>
    public int WinnerId { get; private set; } = -1;

    /// <summary>A player is defeated when they own no buildings (units never count, by design).</summary>
    public bool IsDefeated(int playerId)
    {
        foreach (var b in _buildings)
            if (b.OwnerId == playerId) return false;
        return true;
    }

    /// <summary>Recompute the latched outcome: Over when &lt;= 1 player still owns a building.
    /// Runs after RemoveDeadBuildings each tick; never reverts once Over. Reads only hashed
    /// building ownership — deterministic, integer-only.</summary>
    private void UpdateMatchState()
    {
        if (Phase == MatchPhase.Over) return;
        var hasBuilding = new bool[_players.Length];
        foreach (var b in _buildings) hasBuilding[b.OwnerId] = true;
        int aliveCount = 0, lastAlive = -1;
        for (int p = 0; p < hasBuilding.Length; p++)
            if (hasBuilding[p]) { aliveCount++; lastAlive = p; }
        if (aliveCount <= 1)
        {
            Phase = MatchPhase.Over;
            WinnerId = aliveCount == 1 ? lastAlive : -1;
        }
    }
}
```

- [ ] **Step 4: Wire `UpdateMatchState()` into `Step`**

In `src/SimCore/Sim/SimWorld.cs`, change the end of `Step` (lines ~100-102) from:

```csharp
        RemoveDead();
        RemoveDeadBuildings();
        Tick++;
```

to:

```csharp
        RemoveDead();
        RemoveDeadBuildings();
        UpdateMatchState();
        Tick++;
```

- [ ] **Step 5: Run the tests, expect pass**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
Expected: PASS — 288 + 5 = 293. The 3 determinism tests STILL pass (StateHasher untouched), confirming the new phase is purely additive metadata that does not change sim behavior.

- [ ] **Step 6: Commit**

```bash
git add src/SimCore/Sim/SimWorld.Match.cs src/SimCore/Sim/SimWorld.cs tests/SimCore.Tests/MatchStateTests.cs
git commit -m "feat(sim): deterministic match state (victory/defeat, buildings-only)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 2: Hash match state (StateHasher v6) + golden re-pin

**Files:**
- Modify: `src/SimCore/Sim/StateHasher.cs`
- Modify: `tests/SimCore.Tests/DeterminismTests.cs`

Per the StateHasher convention, every mutable sim field must be folded in. `Phase` and `WinnerId` are now sim state, so they must be hashed and the golden re-pinned in the same commit.

- [ ] **Step 1: Fold the match-state fields into the hash**

In `src/SimCore/Sim/StateHasher.cs`, add the two fields just before the final `return h;` (after the map-passability block, line ~150):

```csharp
        h = Mix(h, (ulong)(int)world.Phase);
        h = Mix(h, (ulong)world.WinnerId);
        return h;
```

Then extend the convention doc-comment (the `<summary>` block at the top, around line 22-26 where prior versions are listed) by adding a line:

```
    /// Match state (SimWorld.Phase + WinnerId) IS hashed (v6, Plan 5a) — folded at the
    /// very end, after map passability.
```

- [ ] **Step 2: Run the determinism tests, expect the golden to fail**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q --filter "FullyQualifiedName~DeterminismTests"`
Expected: `Same_Script...` and `Replaying_After_Full_Run...` PASS (both runs identical to each other), but `Trajectory_Hash_Matches_Golden_Constant` FAILS — the assertion message shows `Assert.Equal() Failure: Expected: 5141900307592480923, Actual: <NEW VALUE>`. Record `<NEW VALUE>`.

- [ ] **Step 3: Counterfactually verify the re-pin is legitimate**

Confirm the fold changed *because of the new fields*, not coincidence: temporarily comment out the two new `Mix` lines from Step 1, run the trajectory test again, and confirm `Actual` reverts to the old `5141900307592480923`. Then un-comment them (restore the two lines). This proves the new constant is exactly the old trajectory plus the match-state fold.

Also confirm the scenario behaves as expected: the match latches at tick 0 because player 1 is unit-only. (Optional sanity check you can run mentally — no code needed: player 0 places a depot at t=0 via BuildCommand, player 1 has only units, so `UpdateMatchState` at the end of tick 0 sees aliveCount==1 → `Over`, `WinnerId==0`, constant thereafter.)

- [ ] **Step 4: Re-pin the golden constant + add the scenario note**

In `tests/SimCore.Tests/DeterminismTests.cs`, set the constant (line 236) to the recorded value:

```csharp
    private const ulong GoldenTrajectoryHash = <NEW VALUE>UL;
```

And add a one-line note to the `Scenario()` doc-comment (just below the `New in v6:` block, around line 28) recording the tick-0 latch:

```
    ///   Plan-5a: match state is now hashed; this scenario's player 1 is unit-only, so the
    ///   match latches Over/WinnerId=0 at tick 0 (deterministic; sim does not halt).
```

- [ ] **Step 5: Run the full suite (Release) — determinism gate**

Run: `dotnet test --configuration Release --nologo -v q`
Expected: PASS — SimCore.Tests 293, SpriteSlicer.Tests 6, 0 failures. All 3 determinism tests green with the new golden.

- [ ] **Step 6: Run the full suite (Debug)**

Run: `dotnet test --configuration Debug --nologo -v q`
Expected: PASS (Debug == Release).

- [ ] **Step 7: Commit**

```bash
git add src/SimCore/Sim/StateHasher.cs tests/SimCore.Tests/DeterminismTests.cs
git commit -m "feat(sim): StateHasher v6 folds match state; re-pin golden trajectory hash

Match state (Phase + WinnerId) added to the hash per the all-state convention. The
determinism scenario's player 1 is unit-only, so the match deterministically latches
Over/winner-0 at tick 0 (sim does not halt); re-pin counterfactually verified.

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Self-Review (author checklist — completed)

- **Spec coverage:** aliveness rule + IsDefeated + Phase/WinnerId + latch + draw + UpdateMatchState placement (Task 1); StateHasher v6 + golden re-pin + scenario note (Task 2); read-only public surface (Task 1 properties). The "sim does not halt" requirement is satisfied by NOT adding any early-return to `Step`. Every spec "In scope" item maps to a task.
- **Placeholder scan:** none — all code is complete; the only intentional fill-in is the golden constant `<NEW VALUE>`, which by protocol is computed at execution time from the test-failure message (cannot be precomputed) and is counterfactually verified in Step 3.
- **Type consistency:** `MatchPhase`/`Phase`/`WinnerId`/`IsDefeated`/`UpdateMatchState` named identically across tasks and tests; `UpdateMatchState()` is the exact method wired into `Step`; hash folds `(int)world.Phase` and `world.WinnerId` matching the property types.
