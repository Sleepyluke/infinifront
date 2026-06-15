# Multiplayer M3 — Lobby + 2v2 Team Play Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A host-driven lobby that configures up to 4 players (humans + CPUs) into 2 teams, each with a faction, and starts a synchronized deterministic-lockstep team match — with ally-immune combat, team victory, and shared team vision.

**Architecture:** SimCore gains a per-player `Team` (default = own index → solo) and routes combat targeting, victory, and vision through `SameTeam`, so everything is a no-op for solo teams (golden untouched). `MatchSetup.BuildMatch(slots, seed)` builds 2–4 players on a 4-corner map. A headless-tested `LobbyCodec` (SimCore.Net) serializes the slot list + start-config; `NetSession` carries host-authoritative lobby sync + a start broadcast; an in-scene `LobbyScreen` overlay configures it (no scene reload — the ENet peer must survive). On Start every peer builds the identical world and enters the M2 lockstep loop.

**Tech Stack:** C# (.NET 8), Godot 4.6 .NET multiplayer, `SimCore`/`SimCore.Net`, xUnit.

**Spec:** `docs/superpowers/specs/2026-06-15-multiplayer-m3-lobby-teams-design.md`. **Umbrella:** `docs/superpowers/specs/2026-06-15-multiplayer-design.md`.

**THE DETERMINISM KEYSTONE (every SimCore task must honor it):** each player defaults to a solo team (`Team[i] = i`). Under solo teams `SameTeam(a,b)` ⟺ `a == b`, so every team change below is behavior-identical to today. The golden scenario is 2 solo players → **golden trajectory hash `1571756151672809223UL` must stay unchanged with NO re-pin.** If a SimCore task changes the golden, the solo-equivalence was broken — fix it, don't re-pin. `Team` is **not** hashed (immutable start-config, agreed via broadcast).

**Process landmines (carried):**
- `src/SimCore*` files use `using SimCore.Math;` which shadows `System.Math` — qualify `System.Math.X` if needed (the codec/vision already use `System.Math.Max`/`Min`, fine).
- A background tool has, earlier this session, silently written code into committed files. **Run `git status -s` before every stage/commit; stage only intended files by explicit path; report any stray modifications, never commit them.**
- Run implementer subagents in the foreground; verify `git log -1` after each task.

---

## File Structure

- **Modify** `src/SimCore/Sim/PlayerState.cs` — add `Team`. (Task 1)
- **Modify** `src/SimCore/Sim/SimWorld.cs` — set `Team = i` in both ctors; add `SameTeam`/`SetTeam`; team-aware no-friendly-fire in `AttackCommand`. (Tasks 1, 2)
- **Modify** `src/SimCore/Sim/SimWorld.Combat.cs` — `AcquireTarget` ally filter. (Task 2)
- **Modify** `src/SimCore/Sim/SimWorld.Match.cs` — team-aware victory. (Task 3)
- **Modify** `src/SimCore/Sim/SimWorld.Vision.cs` — team-aware `IsVisibleTo`/`IsExploredBy`. (Task 4)
- **Modify** `src/SimCore/Sim/MatchSetup.cs` — `MatchSlot` + `BuildMatch` + 4-corner map; re-express the 1v1 builders. (Task 5)
- **Create** `src/SimCore.Net/LobbyTypes.cs` + `src/SimCore.Net/LobbyCodec.cs` — slot/start-config records + binary codec. (Task 6)
- **Create** `godot/scripts/NetSession.cs` additions — lobby RPCs (sync/claim/faction/start). (Task 7)
- **Create** `godot/scripts/LobbyScreen.cs` — the lobby UI. (Task 8)
- **Modify** `godot/scripts/Main.cs`, `godot/scripts/FogView.cs`, `godot/scripts/GameOverScreen.cs`, `godot/scripts/MatchConfig.cs` — build-on-start, team fog, team win, lobby intent. (Task 9)
- **Tests** under `tests/SimCore.Tests/` for Tasks 1–6.

---

## Task 1: Team model — `Team` + `SameTeam` + `SetTeam` (SimCore, TDD)

**Files:** Modify `src/SimCore/Sim/PlayerState.cs`, `src/SimCore/Sim/SimWorld.cs`; Test `tests/SimCore.Tests/TeamModelTests.cs`.

- [ ] **Step 1: Write the failing test**

Create `tests/SimCore.Tests/TeamModelTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class TeamModelTests
{
    private static SimWorld World(int players) =>
        new SimWorld(new MapGrid(16, 16), seed: 1, playerCount: players, faction: null);

    [Fact]
    public void Players_Default_To_Solo_Teams()
    {
        var w = World(4);
        Assert.Equal(0, w.Players[0].Team);
        Assert.Equal(1, w.Players[1].Team);
        Assert.Equal(2, w.Players[2].Team);
        Assert.Equal(3, w.Players[3].Team);
        Assert.True(w.SameTeam(2, 2));
        Assert.False(w.SameTeam(0, 1));
    }

    [Fact]
    public void SetTeam_Groups_Players()
    {
        var w = World(4);
        w.SetTeam(0, 0); w.SetTeam(1, 1); w.SetTeam(2, 0); w.SetTeam(3, 1);
        Assert.True(w.SameTeam(0, 2));   // team 0
        Assert.True(w.SameTeam(1, 3));   // team 1
        Assert.False(w.SameTeam(0, 1));
        Assert.False(w.SameTeam(2, 3));
    }
}
```

- [ ] **Step 2: Run, expect failure** — `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q` → compile FAIL (`Team`/`SameTeam`/`SetTeam` missing). (If `dotnet` missing in bash: `export PATH="$PATH:/c/Program Files/dotnet"`.)

- [ ] **Step 3: Implement**

In `src/SimCore/Sim/PlayerState.cs`, add after the `Difficulty` property (line ~14):

```csharp
    public int Team { get; set; }   // default 0; SimWorld sets it to the player's index (solo) at construction
```

In `src/SimCore/Sim/SimWorld.cs`, set `Team = i` in **both** ctor loops:

```csharp
        for (int i = 0; i < playerCount; i++) { _players[i] = new PlayerState { Team = i }; _factions[i] = faction; }
```
```csharp
        for (int i = 0; i < playerCount; i++) _players[i] = new PlayerState { Team = i };
```

Add these members to `SimWorld` (near the `Players` accessor, after line ~28):

```csharp
    /// <summary>True if two players are allied (same team). Solo teams (the default) make this ⟺ a==b.</summary>
    public bool SameTeam(int a, int b) => _players[a].Team == _players[b].Team;

    /// <summary>Assign a player's team (immutable config; set before the match runs). Mirrors SetCpu.</summary>
    public void SetTeam(int playerId, int team) => _players[playerId].Team = team;
```

- [ ] **Step 4: Run, expect pass** — both new tests green; SimCore count +2.

- [ ] **Step 5: Verify golden unchanged + commit**

```bash
dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q
git status -s   # confirm ONLY PlayerState.cs, SimWorld.cs, TeamModelTests.cs
git add src/SimCore/Sim/PlayerState.cs src/SimCore/Sim/SimWorld.cs tests/SimCore.Tests/TeamModelTests.cs
git commit -m "feat(sim): per-player Team + SameTeam/SetTeam (solo by default)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```
The determinism test is part of the suite; if it fails, the golden changed → the solo default wasn't applied correctly. Fix before committing.

---

## Task 2: Ally-immune combat (SimCore, TDD)

**Files:** Modify `src/SimCore/Sim/SimWorld.Combat.cs`, `src/SimCore/Sim/SimWorld.cs`; Test `tests/SimCore.Tests/AllyImmuneCombatTests.cs`.

- [ ] **Step 1: Write the failing test**

Create `tests/SimCore.Tests/AllyImmuneCombatTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class AllyImmuneCombatTests
{
    // A 1-range weapon; two adjacent units. Helper builds a world with both units placed close.
    private static (SimWorld w, int a, int b) TwoUnits(int ownerA, int ownerB)
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, playerCount: 4, faction: null);
        var weapon = new Weapon(Damage: Fix.FromInt(5), Range: Fix.FromInt(3), CooldownTicks: 10);
        int a = w.SpawnUnit(ownerA, w.Map.CellCenter(5, 5), Fix.FromInt(1), hp: 50, weapon: weapon);
        int b = w.SpawnUnit(ownerB, w.Map.CellCenter(6, 5), Fix.FromInt(1), hp: 50, weapon: weapon);
        w.FogEnabled = false; // isolate the ally test from vision
        return (w, a, b);
    }

    [Fact]
    public void Enemy_Is_Attacked()
    {
        var (w, a, b) = TwoUnits(ownerA: 0, ownerB: 1); // different solo teams
        for (int i = 0; i < 20; i++) w.Step(System.Array.Empty<Command>());
        Assert.True(w.GetUnit(b)!.Hp < 50, "an enemy in range should take damage");
    }

    [Fact]
    public void Teammate_Is_Not_Attacked()
    {
        var (w, a, b) = TwoUnits(ownerA: 0, ownerB: 1);
        w.SetTeam(0, 7); w.SetTeam(1, 7); // same team
        for (int i = 0; i < 20; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(50, w.GetUnit(b)!.Hp); // allies never auto-acquire each other
    }

    [Fact]
    public void Explicit_Attack_On_Teammate_Is_Ignored()
    {
        var (w, a, b) = TwoUnits(ownerA: 0, ownerB: 1);
        w.SetTeam(0, 7); w.SetTeam(1, 7);
        w.Step(new Command[] { new AttackCommand(0, new[] { a }, b) });
        for (int i = 0; i < 20; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(50, w.GetUnit(b)!.Hp);
    }
}
```

**Before running:** confirm the `Weapon` ctor signature (grep `record Weapon` / `class Weapon` in `src/SimCore/Sim/`) and the `SpawnUnit(int, FixVec, Fix, int, Weapon?)` overload (it exists — `SimWorld.cs:53`). Adjust the weapon construction to the real signature if it differs (e.g. named args / extra fields). Do not change the sim to fit the test.

- [ ] **Step 2: Run, expect failure** — the `Teammate_*` tests fail (allies currently attack because the filter is `OwnerId ==`, and same-team different-owner units are treated as enemies).

- [ ] **Step 3: Implement**

In `src/SimCore/Sim/SimWorld.Combat.cs` `AcquireTarget`, change the two owner filters:
- Line ~244: `if (e.OwnerId == u.OwnerId || e.Hp <= 0) continue;` → `if (SameTeam(e.OwnerId, u.OwnerId) || e.Hp <= 0) continue;`
- Line ~254: `if (b.OwnerId == u.OwnerId || b.Hp <= 0) continue;` → `if (SameTeam(b.OwnerId, u.OwnerId) || b.Hp <= 0) continue;`

In `src/SimCore/Sim/SimWorld.cs` `AttackCommand` handling, line ~155:
`if ((tu?.OwnerId ?? tb!.OwnerId) == atk.PlayerId) continue; // no friendly fire`
→
`if (SameTeam(tu?.OwnerId ?? tb!.OwnerId, atk.PlayerId)) continue; // no friendly fire (team-aware)`

- [ ] **Step 4: Run, expect pass** — all three tests green. SimCore count +3.

- [ ] **Step 5: Verify golden unchanged + commit** — golden still `1571756151672809223UL` (solo: `SameTeam` ⟺ same owner → identical targeting). `git status -s` (only the 3 files), then:

```bash
git add src/SimCore/Sim/SimWorld.Combat.cs src/SimCore/Sim/SimWorld.cs tests/SimCore.Tests/AllyImmuneCombatTests.cs
git commit -m "feat(sim): ally-immune combat (team-aware target acquisition + no-friendly-fire)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 3: Team-aware victory (SimCore, TDD)

**Files:** Modify `src/SimCore/Sim/SimWorld.Match.cs`; Test `tests/SimCore.Tests/TeamVictoryTests.cs`.

- [ ] **Step 1: Write the failing test**

Create `tests/SimCore.Tests/TeamVictoryTests.cs`:

```csharp
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class TeamVictoryTests
{
    // Place a building for the given owners, then step once to update match state.
    private static SimWorld WithBuildingOwners(int players, params int[] owners)
    {
        var w = new SimWorld(new MapGrid(24, 24), seed: 1, playerCount: players, faction: null);
        var spec = new BuildingSpec(Width: 2, Height: 2, MaxHp: 100, SightRange: 4);
        int x = 0;
        foreach (var o in owners) { w.AddCompletedBuilding(o, spec, x, 0); x += 3; }
        return w;
    }

    [Fact]
    public void Two_Teams_Both_Alive_Is_Not_Over()
    {
        var w = WithBuildingOwners(4, 0, 1, 2, 3);
        w.SetTeam(0, 0); w.SetTeam(1, 0); w.SetTeam(2, 1); w.SetTeam(3, 1);
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(MatchPhase.InProgress, w.Phase);
    }

    [Fact]
    public void Team_Wins_When_Only_Its_Members_Have_Buildings()
    {
        var w = WithBuildingOwners(4, 0, 2); // only team-0 player 0 and team-1 player 2 own buildings... set teams:
        w.SetTeam(0, 0); w.SetTeam(1, 0); w.SetTeam(2, 0); w.SetTeam(3, 1);
        // owners {0,2} are both team 0 -> team 0 wins, representative = 0
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(MatchPhase.Over, w.Phase);
        Assert.Equal(0, w.WinnerId);
        Assert.True(w.SameTeam(0, w.WinnerId));
        Assert.True(w.SameTeam(2, w.WinnerId));   // ally also "wins"
        Assert.False(w.SameTeam(3, w.WinnerId));  // the other team lost
    }

    [Fact]
    public void Mutual_Elimination_Is_A_Draw()
    {
        var w = new SimWorld(new MapGrid(24, 24), seed: 1, playerCount: 2, faction: null);
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(MatchPhase.Over, w.Phase);
        Assert.Equal(-1, w.WinnerId);
    }
}
```

**Before running:** confirm `BuildingSpec` ctor + `AddCompletedBuilding(int owner, BuildingSpec spec, int x, int y, ...)` signatures (grep in `src/SimCore/Sim/`; `AddCompletedBuilding` is used in `MatchSetup.PlaceBase` with a trailing def-id arg — match the real signature, the def-id may be optional or required). Adjust the helper to the real signatures; don't change the sim.

- [ ] **Step 2: Run, expect failure** — `Team_Wins_*` fails (current victory needs ≤1 *player*, not ≤1 *team*, with buildings).

- [ ] **Step 3: Implement** — replace `UpdateMatchState` in `src/SimCore/Sim/SimWorld.Match.cs`:

```csharp
    /// <summary>Recompute the latched outcome: Over when all players that still own a building
    /// share ONE team (or none remain = draw). WinnerId = the lowest-index surviving building-owner
    /// (a representative of the winning team); -1 on a draw. Solo teams reduce this to the old
    /// "≤1 player owns a building" rule, so the golden is unchanged. Reads only hashed building
    /// ownership + immutable team config — deterministic, integer-only.</summary>
    private void UpdateMatchState()
    {
        if (Phase == MatchPhase.Over) return;
        var hasBuilding = new bool[_players.Length];
        foreach (var b in _buildings) hasBuilding[b.OwnerId] = true;

        int firstOwner = -1;          // lowest-index player still owning a building (the representative)
        bool multipleTeams = false;
        for (int p = 0; p < hasBuilding.Length; p++)
        {
            if (!hasBuilding[p]) continue;
            if (firstOwner < 0) firstOwner = p;
            else if (!SameTeam(p, firstOwner)) { multipleTeams = true; break; }
        }
        if (!multipleTeams)
        {
            Phase = MatchPhase.Over;
            WinnerId = firstOwner;    // -1 if nobody owns a building (draw)
        }
    }
```

- [ ] **Step 4: Run, expect pass** — three tests green. SimCore count +3.

- [ ] **Step 5: Verify golden unchanged + commit** — solo equivalence: 1 surviving player ⇒ `WinnerId` = that player (= old behavior); 0 ⇒ -1; 2 solo players both alive ⇒ `multipleTeams` ⇒ not Over. Golden `1571756151672809223UL` unchanged. `git status -s`, then:

```bash
git add src/SimCore/Sim/SimWorld.Match.cs tests/SimCore.Tests/TeamVictoryTests.cs
git commit -m "feat(sim): team-aware victory (Over when one team remains; representative WinnerId)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 4: Shared team vision (SimCore, TDD)

**Files:** Modify `src/SimCore/Sim/SimWorld.Vision.cs`; Test `tests/SimCore.Tests/TeamVisionTests.cs`.

Vision is **not hashed** (`SimWorld.Vision.cs:9`), so this cannot affect the golden — but the targeting path reads `IsVisibleTo`, so keep solo behavior identical.

- [ ] **Step 1: Write the failing test**

Create `tests/SimCore.Tests/TeamVisionTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class TeamVisionTests
{
    private static SimWorld World()
    {
        var w = new SimWorld(new MapGrid(32, 32), seed: 1, playerCount: 4, faction: null);
        return w;
    }

    [Fact]
    public void Ally_Sees_Through_Teammates_Vision()
    {
        var w = World();
        w.SetTeam(0, 5); w.SetTeam(1, 5);                 // 0 and 1 allied
        // Only player 1 has a unit, far from origin; player 0 has none there.
        int cx = 25, cy = 25;
        w.SpawnUnit(1, w.Map.CellCenter(cx, cy), Fix.FromInt(1), hp: 10);
        w.Step(System.Array.Empty<Command>());            // UpdateVision runs in Step
        Assert.True(w.IsVisibleTo(1, cx, cy));            // the unit's owner sees it
        Assert.True(w.IsVisibleTo(0, cx, cy));            // the ALLY sees it too (shared vision)
        Assert.False(w.IsVisibleTo(2, cx, cy));           // an enemy (different team) does not
    }

    [Fact]
    public void Solo_Players_Do_Not_Share_Vision()
    {
        var w = World();                                  // default solo teams
        int cx = 25, cy = 25;
        w.SpawnUnit(1, w.Map.CellCenter(cx, cy), Fix.FromInt(1), hp: 10);
        w.Step(System.Array.Empty<Command>());
        Assert.True(w.IsVisibleTo(1, cx, cy));
        Assert.False(w.IsVisibleTo(0, cx, cy));           // solo → no sharing (today's behavior)
    }
}
```

- [ ] **Step 2: Run, expect failure** — `Ally_Sees_*` fails (vision is per-player today).

- [ ] **Step 3: Implement** — make `IsVisibleTo`/`IsExploredBy` team-aware in `src/SimCore/Sim/SimWorld.Vision.cs`:

```csharp
    public bool IsVisibleTo(int player, int cx, int cy)
    {
        if (!FogEnabled) return true;
        if (!InBounds(cx, cy)) return false;
        int idx = cy * Map.Width + cx;
        for (int p = 0; p < _visible.Length; p++)               // any same-team player seeing the cell
            if (_players[p].Team == _players[player].Team && _visible[p][idx]) return true;
        return false;
    }

    public bool IsExploredBy(int player, int cx, int cy)
    {
        if (!FogEnabled) return true;
        if (!InBounds(cx, cy)) return false;
        int idx = cy * Map.Width + cx;
        for (int p = 0; p < _explored.Length; p++)
            if (_players[p].Team == _players[player].Team && _explored[p][idx]) return true;
        return false;
    }
```

(`_visible.Length == _players.Length` after the first `UpdateVision`; before that the loop is empty → returns false, matching today's "not visible yet". ≤4 players → the O(players) loop is negligible.)

- [ ] **Step 4: Run, expect pass** — both tests green. SimCore count +2.

- [ ] **Step 5: Verify golden unchanged + commit** — vision isn't hashed and solo behavior is identical → golden `1571756151672809223UL` unchanged. `git status -s`, then:

```bash
git add src/SimCore/Sim/SimWorld.Vision.cs tests/SimCore.Tests/TeamVisionTests.cs
git commit -m "feat(sim): shared team vision (IsVisibleTo/IsExploredBy union same-team players)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 5: `MatchSetup.BuildMatch` + 4-corner map (SimCore, TDD)

**Files:** Modify `src/SimCore/Sim/MatchSetup.cs`; Test `tests/SimCore.Tests/BuildMatchTests.cs`.

- [ ] **Step 1: Write the failing test**

Create `tests/SimCore.Tests/BuildMatchTests.cs`:

```csharp
using System.Collections.Generic;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class BuildMatchTests
{
    [Fact]
    public void Builds_Four_Players_Two_Teams_Each_Based()
    {
        var slots = new List<MatchSlot>
        {
            new(ReferenceFaction.Def, PlayerController.Human, AiDifficulty.Easy, Team: 0),
            new(ReferenceFaction.Def, PlayerController.Cpu,   AiDifficulty.Hard, Team: 0),
            new(ReferenceFaction.Def, PlayerController.Cpu,   AiDifficulty.Easy, Team: 1),
            new(ReferenceFaction.Def, PlayerController.Cpu,   AiDifficulty.Medium, Team: 1),
        };
        var w = MatchSetup.BuildMatch(slots, seed: 42);

        Assert.Equal(4, w.Players.Count);
        Assert.Equal(PlayerController.Human, w.Players[0].Controller);
        Assert.Equal(PlayerController.Cpu, w.Players[1].Controller);
        Assert.Equal(AiDifficulty.Hard, w.Players[1].Difficulty);
        Assert.True(w.SameTeam(0, 1));
        Assert.True(w.SameTeam(2, 3));
        Assert.False(w.SameTeam(0, 2));
        for (int p = 0; p < 4; p++)
            Assert.Contains(w.Buildings, b => b.OwnerId == p);   // each player based at a distinct corner
        Assert.Equal(MatchPhase.InProgress, w.Phase);
    }

    [Fact]
    public void Two_Slot_Match_Equals_Old_Versus1v1()
    {
        // BuildVersus1v1 is now expressed on BuildMatch; both must produce the same building/unit layout.
        var viaVersus = MatchSetup.BuildVersus1v1(ReferenceFaction.Def, ReferenceFaction.Def, seed: 7);
        var viaMatch = MatchSetup.BuildMatch(new List<MatchSlot>
        {
            new(ReferenceFaction.Def, PlayerController.Human, AiDifficulty.Easy, Team: 0),
            new(ReferenceFaction.Def, PlayerController.Human, AiDifficulty.Easy, Team: 1),
        }, seed: 7);
        Assert.Equal(viaVersus.Buildings.Count, viaMatch.Buildings.Count);
        Assert.Equal(viaVersus.Units.Count, viaMatch.Units.Count);
        Assert.Equal(viaVersus.Players.Count, viaMatch.Players.Count);
    }
}
```

- [ ] **Step 2: Run, expect failure** — compile FAIL (`MatchSlot`/`BuildMatch` missing).

- [ ] **Step 3: Implement** — in `src/SimCore/Sim/MatchSetup.cs`, add the record + `BuildMatch` + 4 corners, and re-express the two 1v1 builders on top:

```csharp
public sealed record MatchSlot(FactionDef Faction, PlayerController Controller, AiDifficulty Difficulty, int Team);
```
(put the record above `public static class MatchSetup` or in the same file under the namespace.)

Inside `MatchSetup`, add the corner table and `BuildMatch`, and replace `BuildStandard1v1`/`BuildVersus1v1` bodies to delegate:

```csharp
    private readonly record struct Corner(int DepotX, int DepotY, int RaxX, int RaxY, int NodeX, int NodeY, int WorkerX, int WorkerY);

    // Up to 4 start corners on the 40x40 map. 0/1 match the legacy 1v1 placements exactly.
    private static readonly Corner[] Corners =
    {
        new(4, 4, 8, 4, 2, 8, 6, 8),        // top-left
        new(34, 34, 30, 34, 37, 28, 33, 28),// bottom-right
        new(4, 34, 8, 34, 2, 28, 6, 28),    // bottom-left
        new(34, 4, 30, 4, 37, 8, 33, 8),    // top-right
    };

    /// <summary>Build a 2–4 player match from slots. One role-resolved base per corner; CPU + team
    /// set per slot. Deterministic from the seed; identical on every peer (the lockstep contract).</summary>
    public static SimWorld BuildMatch(System.Collections.Generic.IReadOnlyList<MatchSlot> slots, ulong seed)
    {
        if (slots.Count < 2 || slots.Count > Corners.Length)
            throw new System.ArgumentException($"BuildMatch supports 2..{Corners.Length} slots", nameof(slots));
        var factions = new FactionDef?[slots.Count];
        for (int i = 0; i < slots.Count; i++) factions[i] = slots[i].Faction;
        var w = new SimWorld(BuildMap(), seed, factions);
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (s.Controller == PlayerController.Cpu) w.SetCpu(i, s.Difficulty);
            w.SetTeam(i, s.Team);
            var c = Corners[i];
            PlaceBase(w, i, s.Faction, c.DepotX, c.DepotY, c.RaxX, c.RaxY, c.NodeX, c.NodeY, c.WorkerX, c.WorkerY);
        }
        return w;
    }

    public static SimWorld BuildStandard1v1(FactionDef humanFaction, FactionDef cpuFaction,
                                            AiDifficulty difficulty, ulong seed) =>
        BuildMatch(new[]
        {
            new MatchSlot(humanFaction, PlayerController.Human, AiDifficulty.Easy, Team: 0),
            new MatchSlot(cpuFaction, PlayerController.Cpu, difficulty, Team: 1),
        }, seed);

    public static SimWorld BuildVersus1v1(FactionDef p0Faction, FactionDef p1Faction, ulong seed) =>
        BuildMatch(new[]
        {
            new MatchSlot(p0Faction, PlayerController.Human, AiDifficulty.Easy, Team: 0),
            new MatchSlot(p1Faction, PlayerController.Human, AiDifficulty.Easy, Team: 1),
        }, seed);
```

Remove the now-duplicated old bodies of `BuildStandard1v1`/`BuildVersus1v1` (the inline `new SimWorld(...)` + `PlaceBase` calls). Keep `BuildMap`, `PlaceBase`, `FirstWorker`, `CheapestCombat`, `MapSize` unchanged.

> Solo-team note: in the 2-slot delegations, `Team: 0`/`Team: 1` equal the default solo teams (player index), and `SetTeam(i, i)` is a no-op vs the default — so the worlds are byte-identical to the old builders. The golden (via the determinism scenario, not `MatchSetup`) is untouched regardless, but `TestMap`→`BuildStandard1v1` (the sandbox) stays identical.

- [ ] **Step 4: Run, expect pass** — both tests green; the existing `MatchSetupVersusTests`/sandbox tests stay green (delegation is behavior-preserving). SimCore count +2.

- [ ] **Step 5: Verify golden unchanged + commit** — `git status -s` (only MatchSetup.cs + the new test), golden `1571756151672809223UL` unchanged, then:

```bash
git add src/SimCore/Sim/MatchSetup.cs tests/SimCore.Tests/BuildMatchTests.cs
git commit -m "feat(sim): MatchSetup.BuildMatch (2-4 players, 4-corner map, teams); 1v1 builders delegate

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 6: `LobbyCodec` — slot/start-config serialization (SimCore.Net, TDD)

**Files:** Create `src/SimCore.Net/LobbyTypes.cs`, `src/SimCore.Net/LobbyCodec.cs`; Test `tests/SimCore.Tests/Net/LobbyCodecTests.cs`.

The wire types carry a faction **id** (string); each peer resolves it to a `FactionDef` via its own `PackCatalog` at build time. This keeps the codec free of `FactionDef` and headless-testable.

- [ ] **Step 1: Write the failing test**

Create `tests/SimCore.Tests/Net/LobbyCodecTests.cs`:

```csharp
using System.Collections.Generic;
using SimCore.Net;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests.Net;

public class LobbyCodecTests
{
    private static List<LobbySlot> Sample() => new()
    {
        new LobbySlot(SlotKind.Human, Team: 0, FactionId: "reference", Difficulty: AiDifficulty.Easy, OccupantPeerId: 1),
        new LobbySlot(SlotKind.Cpu,   Team: 0, FactionId: "swarm",     Difficulty: AiDifficulty.Hard, OccupantPeerId: 0),
        new LobbySlot(SlotKind.Open,  Team: 1, FactionId: "",          Difficulty: AiDifficulty.Easy, OccupantPeerId: 0),
        new LobbySlot(SlotKind.Human, Team: 1, FactionId: "reference", Difficulty: AiDifficulty.Medium, OccupantPeerId: 77),
    };

    [Fact]
    public void Slots_RoundTrip_Field_Exact()
    {
        var slots = Sample();
        var back = LobbyCodec.SlotsFromBytes(LobbyCodec.SlotsToBytes(slots));
        Assert.Equal(slots.Count, back.Count);
        for (int i = 0; i < slots.Count; i++)
        {
            Assert.Equal(slots[i].Kind, back[i].Kind);
            Assert.Equal(slots[i].Team, back[i].Team);
            Assert.Equal(slots[i].FactionId, back[i].FactionId);
            Assert.Equal(slots[i].Difficulty, back[i].Difficulty);
            Assert.Equal(slots[i].OccupantPeerId, back[i].OccupantPeerId);
        }
    }

    [Fact]
    public void Empty_Slot_List_RoundTrips()
    {
        var back = LobbyCodec.SlotsFromBytes(LobbyCodec.SlotsToBytes(new List<LobbySlot>()));
        Assert.Empty(back);
    }
}
```

- [ ] **Step 2: Run, expect failure** — compile FAIL.

- [ ] **Step 3: Implement**

Create `src/SimCore.Net/LobbyTypes.cs`:

```csharp
using SimCore.Sim;

namespace SimCore.Net;

/// <summary>Lobby slot kind. Human = a human player (local or remote); Cpu = an in-sim AI; Open = joinable.</summary>
public enum SlotKind : byte { Open = 0, Human = 1, Cpu = 2 }

/// <summary>One lobby slot. Faction is by ID (each peer resolves it via its own PackCatalog).
/// OccupantPeerId = the Godot peer occupying a Human slot (0 = none / not applicable).</summary>
public sealed record LobbySlot(SlotKind Kind, int Team, string FactionId, AiDifficulty Difficulty, long OccupantPeerId);
```

Create `src/SimCore.Net/LobbyCodec.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using SimCore.Sim;

namespace SimCore.Net;

/// <summary>Binary (de)serialization of the lobby slot list for NetSession RPCs. Little-endian +
/// length-prefixed strings (cross-OS stable), same discipline as CommandCodec.</summary>
public static class LobbyCodec
{
    public static byte[] SlotsToBytes(IReadOnlyList<LobbySlot> slots)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write(slots.Count);
            foreach (var s in slots)
            {
                w.Write((byte)s.Kind);
                w.Write(s.Team);
                w.Write(s.FactionId);
                w.Write((byte)s.Difficulty);
                w.Write(s.OccupantPeerId);
            }
        }
        return ms.ToArray();
    }

    public static IReadOnlyList<LobbySlot> SlotsFromBytes(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);
        int n = r.ReadInt32();
        var slots = new List<LobbySlot>(n);
        for (int i = 0; i < n; i++)
        {
            var kind = (SlotKind)r.ReadByte();
            int team = r.ReadInt32();
            string factionId = r.ReadString();
            var diff = (AiDifficulty)r.ReadByte();
            long peer = r.ReadInt64();
            slots.Add(new LobbySlot(kind, team, factionId, diff, peer));
        }
        return slots;
    }
}
```

- [ ] **Step 4: Run, expect pass** — both tests green. SimCore count +2.

- [ ] **Step 5: Commit** — `git status -s` (only the 3 files), then:

```bash
git add src/SimCore.Net/LobbyTypes.cs src/SimCore.Net/LobbyCodec.cs tests/SimCore.Tests/Net/LobbyCodecTests.cs
git commit -m "feat(net): LobbyCodec + LobbySlot — binary lobby-state serialization

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 7: NetSession lobby protocol (Godot, compile-checked)

**Files:** Modify `godot/scripts/NetSession.cs`.

Replace M2's immediate start handshake with a lobby protocol: the host holds the authoritative slot list, broadcasts it on change, accepts client claim/faction requests, and broadcasts a final start-config. Compile-only (playtested in Task 10).

- [ ] **Step 1: Implement** — extend `NetSession` (keep the existing M2 frame/hash RPCs + relay; replace the start handshake). Add:

```csharp
    using System.Collections.Generic;   // ensure present at top

    // ---- Lobby protocol (M3) ----
    /// <summary>Host-authoritative current slot list changed (or first received). UI re-renders.</summary>
    public event System.Action<IReadOnlyList<LobbySlot>>? LobbyUpdated;
    /// <summary>The match is starting: final slots + seed. Build the world + enter the loop.</summary>
    public event System.Action<IReadOnlyList<LobbySlot>, ulong>? MatchStarting;

    // Replace M2's OnPeerConnected auto-start. The HOST owns the slot list and assigns joiners.
    // Host code calls SetLobby(slots) to push state; clients receive it via SyncLobbyRpc.

    /// <summary>(Host) Broadcast the authoritative slot list to all clients and raise locally.</summary>
    public void SetLobby(IReadOnlyList<LobbySlot> slots)
    {
        if (!IsHost) return;
        var bytes = LobbyCodec.SlotsToBytes(slots);
        Rpc(MethodName.SyncLobbyRpc, bytes);
        LobbyUpdated?.Invoke(slots);
    }

    /// <summary>(Host) Broadcast the final config + seed; everyone (host local too) starts.</summary>
    public void StartMatch(IReadOnlyList<LobbySlot> slots, ulong seed)
    {
        if (!IsHost) return;
        var bytes = LobbyCodec.SlotsToBytes(slots);
        Rpc(MethodName.StartConfigRpc, bytes, unchecked((long)seed));
        MatchStarting?.Invoke(slots, seed);     // host starts locally (RPCs are CallLocal=false)
    }

    /// <summary>(Client) Ask the host to set MY slot's faction.</summary>
    public void RequestMyFaction(string factionId) => RpcId(1, MethodName.SetFactionRpc, factionId);
    /// <summary>(Client) Ask the host to claim an Open slot for me.</summary>
    public void RequestClaim(int slotIndex) => RpcId(1, MethodName.ClaimSlotRpc, slotIndex);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncLobbyRpc(byte[] bytes) => LobbyUpdated?.Invoke(LobbyCodec.SlotsFromBytes(bytes));

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StartConfigRpc(byte[] bytes, long seedBits) =>
        MatchStarting?.Invoke(LobbyCodec.SlotsFromBytes(bytes), unchecked((ulong)seedBits));

    // Host-side requests from clients. The host raises an event so LobbyScreen mutates + re-broadcasts.
    /// <summary>(Host) Fired when a client requests its faction: (senderPeerId, factionId).</summary>
    public event System.Action<long, string>? FactionRequested;
    /// <summary>(Host) Fired when a client requests to claim a slot: (senderPeerId, slotIndex).</summary>
    public event System.Action<long, int>? ClaimRequested;
    /// <summary>(Host) A peer connected (assign it an open slot) / disconnected.</summary>
    public event System.Action<long>? PeerConnectedToLobby;

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SetFactionRpc(string factionId) => FactionRequested?.Invoke(Multiplayer.GetRemoteSenderId(), factionId);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClaimSlotRpc(int slotIndex) => ClaimRequested?.Invoke(Multiplayer.GetRemoteSenderId(), slotIndex);
```

Change `OnPeerConnected` (M2 auto-started the match) to instead notify the lobby (host assigns slots there):

```csharp
    private void OnPeerConnected(long peerId)
    {
        if (IsHost) PeerConnectedToLobby?.Invoke(peerId);
    }
```

Keep `LocalPlayerId`, but it is now set by `Main` from the final slot list (the slot whose `OccupantPeerId == Multiplayer.GetUniqueId()`), not in the handshake. Remove the M2 `StartMatchRpc`/`MatchReady` members (superseded by `StartConfigRpc`/`MatchStarting`) — and remove the M2 prime/auto-start path in `OnPeerConnected`. Keep the channel-0 ordering comment (it still applies: `StartConfigRpc` must be processed before any frame, and the loop only primes after `MatchStarting`).

- [ ] **Step 2: Compile-check** — `dotnet build godot/LlmRts.Godot.csproj --nologo`. Fix RPC-arg/signature issues (use the `MethodName.X` form; string + byte[] + int + long are valid Variant args). `Main.cs` will not compile yet (it still references M2's `MatchReady`) — that's fixed in Task 9; if you need a green build here, you may temporarily comment the M2 networked block in Main, but **prefer doing Task 9 before building** and building once at the end of Task 9. Note your choice.

- [ ] **Step 3: Commit** — `git status -s` (only NetSession.cs), then:

```bash
git add godot/scripts/NetSession.cs
git commit -m "feat(net): NetSession lobby protocol (host-authoritative sync + start-config broadcast)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

> If the build can't be green until Task 9, this commit is allowed to be compile-incomplete *for Main only* (NetSession itself must be syntactically valid). State that in the task report. Tasks 7+9 land a green build together.

---

## Task 8: LobbyScreen UI (Godot, compile-checked + playtested)

**Files:** Create `godot/scripts/LobbyScreen.cs`.

An in-code `CanvasLayer` (matching `MenuScreen`'s style) showing a row per slot. The **host** edits slot kinds/teams/factions + difficulties and presses Start; a **client** sees the list read-only except its own slot's faction.

- [ ] **Step 1: Implement** — create `godot/scripts/LobbyScreen.cs`:

```csharp
using System.Collections.Generic;
using Godot;
using SimCore.Net;
using SimCore.Packs;
using SimCore.Sim;

namespace LlmRts.Godot;

/// <summary>Multiplayer lobby overlay. Host configures slots (kind/team/faction/difficulty) and
/// starts; clients claim an open slot + pick their own faction. Drives NetSession; on Start the
/// host broadcasts the final config and everyone builds the match (Main wires MatchStarting).</summary>
public partial class LobbyScreen : CanvasLayer
{
    private NetSession _net = null!;
    private bool _isHost;
    private IReadOnlyList<FactionEntry> _factions = System.Array.Empty<FactionEntry>();
    private List<LobbySlot> _slots = new();          // host: authoritative; client: last received
    private VBoxContainer _rows = null!;
    private Button _start = null!;

    public void Init(NetSession net, bool isHost, IReadOnlyList<FactionEntry> factions)
    {
        _net = net; _isHost = isHost; _factions = factions;
    }

    public override void _Ready()
    {
        Layer = 100;
        var panel = new PanelContainer();
        var box = new VBoxContainer();
        panel.AddChild(box);
        AddChild(panel);
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);

        box.AddChild(new Label { Text = _isHost ? "Lobby (Host)" : "Lobby — waiting for host", HorizontalAlignment = HorizontalAlignment.Center });
        _rows = new VBoxContainer();
        box.AddChild(_rows);

        if (_isHost)
        {
            var addRow = new HBoxContainer();
            var add = new Button { Text = "+ Slot" };
            add.Pressed += () => { if (_slots.Count < 4) { _slots.Add(new LobbySlot(SlotKind.Cpu, _slots.Count % 2, _factions[0].Faction.Id, AiDifficulty.Easy, 0)); PushAndRender(); } };
            var rem = new Button { Text = "- Slot" };
            rem.Pressed += () => { if (_slots.Count > 2) { _slots.RemoveAt(_slots.Count - 1); PushAndRender(); } };
            addRow.AddChild(add); addRow.AddChild(rem);
            box.AddChild(addRow);

            _start = new Button { Text = "Start Match" };
            _start.Pressed += OnStart;
            box.AddChild(_start);

            // Host's initial config: slot 0 = the host (Human, team 0), slot 1 = an Open seat (team 1).
            _slots = new List<LobbySlot>
            {
                new(SlotKind.Human, 0, _factions[0].Faction.Id, AiDifficulty.Easy, 1), // host peer id = 1
                new(SlotKind.Open,  1, _factions[0].Faction.Id, AiDifficulty.Easy, 0),
            };

            _net.PeerConnectedToLobby += OnPeerJoined;
            _net.FactionRequested += OnFactionRequested;
            _net.ClaimRequested += OnClaimRequested;
            PushAndRender();
        }
        else
        {
            _net.LobbyUpdated += slots => { _slots = new List<LobbySlot>(slots); Render(); };
        }
    }

    // ---- Host slot mutation ----
    private void PushAndRender() { _net.SetLobby(_slots); Render(); }   // SetLobby raises LobbyUpdated locally too, but we render directly

    private void OnPeerJoined(long peerId)
    {
        // Assign the joiner to the first Open slot (becomes a remote Human).
        for (int i = 0; i < _slots.Count; i++)
            if (_slots[i].Kind == SlotKind.Open)
            { _slots[i] = _slots[i] with { Kind = SlotKind.Human, OccupantPeerId = peerId }; PushAndRender(); return; }
    }

    private void OnFactionRequested(long peerId, string factionId)
    {
        for (int i = 0; i < _slots.Count; i++)
            if (_slots[i].OccupantPeerId == peerId) { _slots[i] = _slots[i] with { FactionId = factionId }; PushAndRender(); return; }
    }

    private void OnClaimRequested(long peerId, int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < _slots.Count && _slots[slotIndex].Kind == SlotKind.Open)
        { _slots[slotIndex] = _slots[slotIndex] with { Kind = SlotKind.Human, OccupantPeerId = peerId }; PushAndRender(); }
    }

    private void OnStart()
    {
        // Validity: every slot filled (not Open), both teams non-empty.
        bool anyOpen = false; bool t0 = false, t1 = false;
        foreach (var s in _slots) { if (s.Kind == SlotKind.Open) anyOpen = true; if (s.Team == 0) t0 = true; else t1 = true; }
        if (anyOpen || !t0 || !t1) { GD.Print("Lobby not ready: fill all slots, both teams non-empty"); return; }
        _net.StartMatch(_slots, NetSession.MatchSeed);
    }

    // ---- Rendering ----
    private void Render()
    {
        foreach (var c in _rows.GetChildren()) c.QueueFree();
        long myPeer = _net.Multiplayer.GetUniqueId();
        for (int i = 0; i < _slots.Count; i++)
        {
            var s = _slots[i];
            var row = new HBoxContainer();
            bool mine = s.OccupantPeerId == myPeer && s.Kind == SlotKind.Human;
            row.AddChild(new Label { Text = $"Slot {i}: {s.Kind} (team {(s.Team == 0 ? "A" : "B")})" });

            if (_isHost)
            {
                // Host can flip Open<->Cpu and toggle team on non-occupied-by-remote slots.
                var kind = new Button { Text = "Kind" };
                kind.Pressed += () => { _slots[i] = s with { Kind = s.Kind == SlotKind.Cpu ? SlotKind.Open : SlotKind.Cpu, OccupantPeerId = 0 }; PushAndRender(); };
                if (i != 0) row.AddChild(kind);   // slot 0 is always the host
                var team = new Button { Text = "Team" };
                team.Pressed += () => { _slots[i] = s with { Team = 1 - s.Team }; PushAndRender(); };
                row.AddChild(team);
            }

            // Faction picker: host edits CPU + its own; a client edits only its own slot.
            bool canEditFaction = (_isHost && (s.Kind == SlotKind.Cpu || s.OccupantPeerId == 1)) || mine;
            if (canEditFaction)
            {
                var opt = new OptionButton();
                for (int f = 0; f < _factions.Count; f++) opt.AddItem(_factions[f].Name, f);
                int sel = FactionIndex(s.FactionId); opt.Selected = sel < 0 ? 0 : sel;
                int slotIdx = i;
                opt.ItemSelected += id =>
                {
                    string fid = _factions[(int)id].Faction.Id;
                    if (_isHost) { _slots[slotIdx] = _slots[slotIdx] with { FactionId = fid }; PushAndRender(); }
                    else _net.RequestMyFaction(fid);
                };
                row.AddChild(opt);
            }
            else row.AddChild(new Label { Text = FactionName(s.FactionId) });

            if (_isHost && s.Kind == SlotKind.Cpu)
            {
                var diff = new Button { Text = s.Difficulty.ToString() };
                diff.Pressed += () => { var nd = (AiDifficulty)(((int)s.Difficulty + 1) % 3); _slots[i] = s with { Difficulty = nd }; PushAndRender(); };
                row.AddChild(diff);
            }

            // Client: a button to claim this slot if it's Open.
            if (!_isHost && s.Kind == SlotKind.Open)
            {
                int slotIdx = i;
                var claim = new Button { Text = "Claim" };
                claim.Pressed += () => _net.RequestClaim(slotIdx);
                row.AddChild(claim);
            }
            _rows.AddChild(row);
        }
    }

    private int FactionIndex(string id)
    {
        for (int i = 0; i < _factions.Count; i++) if (_factions[i].Faction.Id == id) return i;
        return -1;
    }
    private string FactionName(string id) { int i = FactionIndex(id); return i < 0 ? id : _factions[i].Name; }
}
```

> Confirm `FactionEntry.Faction.Id` and `FactionEntry.Name` exist (`PackCatalog`/`FactionEntry` from 5e). Confirm `Node.Multiplayer.GetUniqueId()` is reachable (it is — `Multiplayer` is a `Node` property). If `record with` on `LobbySlot` needs the record to be non-abstract (it is, `sealed record`) — fine.

- [ ] **Step 2: Compile-check** — built together with Task 9 (`dotnet build godot/LlmRts.Godot.csproj`). Fix UI API mismatches.

- [ ] **Step 3: Commit** — `git status -s` (only LobbyScreen.cs), then:

```bash
git add godot/scripts/LobbyScreen.cs
git commit -m "feat(net): LobbyScreen — host-configurable slot table + client claim/faction

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 9: Build-on-start wiring — Main / FogView / GameOverScreen / MatchConfig (Godot, compile + playtest)

**Files:** Modify `godot/scripts/Main.cs`, `godot/scripts/FogView.cs`, `godot/scripts/GameOverScreen.cs`, `godot/scripts/MatchConfig.cs`.

- [ ] **Step 1: MatchConfig** — `MatchConfig.SetNetwork` already exists (M2). No change needed unless the lobby intent needs a flag; the existing `IsNetworked`/`IsHost`/`Ip` suffice (Main now routes networked → lobby instead of straight to a match).

- [ ] **Step 2: Main — show lobby, build on start** — in `godot/scripts/Main.cs`, replace `StartNetworked()` (the M2 method) with a lobby-first version. Keep the eager default-world build at `_Ready` (so views init; the map is identical for every config):

```csharp
    private void StartNetworked()
    {
        Runner.Paused = true;
        var factions = PackCatalog.Load(PacksDir());

        var net = new NetSession { Name = "Net" };
        AddChild(net);

        var lobby = new LobbyScreen { Name = "Lobby" };
        lobby.Init(net, MatchConfig.IsHost, factions);
        AddChild(lobby);

        net.MatchStarting += (slots, seed) =>
        {
            lobby.QueueFree();
            BuildNetworkedMatch(net, slots, seed, factions);
        };
        net.PeerDropped += () => { Runner.Paused = true; GD.Print("Peer dropped"); };

        if (MatchConfig.IsHost) net.Host(); else net.Join(MatchConfig.Ip);
    }

    private void BuildNetworkedMatch(NetSession net, System.Collections.Generic.IReadOnlyList<LobbySlot> slots, ulong seed, System.Collections.Generic.IReadOnlyList<FactionEntry> factions)
    {
        // Resolve each slot's faction id -> FactionDef (fallback to reference) and map to MatchSlot.
        var matchSlots = new System.Collections.Generic.List<MatchSlot>(slots.Count);
        var humanIds = new System.Collections.Generic.List<int>();
        long myPeer = net.Multiplayer.GetUniqueId();
        int localSlot = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            var faction = ResolveFaction(factions, s.FactionId);
            var controller = s.Kind == SlotKind.Cpu ? PlayerController.Cpu : PlayerController.Human;
            matchSlots.Add(new MatchSlot(faction, controller, s.Difficulty, s.Team));
            if (s.Kind == SlotKind.Human) humanIds.Add(i);
            if (s.Kind == SlotKind.Human && s.OccupantPeerId == myPeer) localSlot = i;
        }

        var world = MatchSetup.BuildMatch(matchSlots, seed);
        Runner.Init(world);
        View.ForceSync();                                   // re-sync views to the new world (map unchanged)
        Selection.ControlledPlayer = localSlot;

        var coord = new SimCore.Net.LockstepCoordinator(localSlot, humanIds, NetSession.InputDelay);
        Runner.InitNetworked(world, coord, net, localSlot);
        Runner.Paused = false;

        var gameOver = new GameOverScreen { Name = "GameOver" };
        AddChild(gameOver);
        gameOver.Init(Runner);
    }

    private static FactionDef ResolveFaction(System.Collections.Generic.IReadOnlyList<FactionEntry> factions, string id)
    {
        foreach (var f in factions) if (f.Faction.Id == id) return f.Faction;
        return ReferenceFaction.Def;
    }
```

Add `using SimCore.Net;` and `using SimCore.Packs;` to Main if not present, and a `PacksDir()` helper (copy `MenuScreen`'s: `System.IO.Path.Combine(System.AppContext.BaseDirectory, "packs")`) or make `MenuScreen.PacksDir` internal+static and reuse it. Also update the `_Ready` networked world-build line (M2 used `BuildVersus1v1`) — it can keep building a default `BuildVersus1v1(Reference, Reference, MatchSeed)` as the placeholder before Start (the views need *a* world; it's replaced on Start).

> `LockstepCoordinator(localSlot, humanIds, …)` — humanIds is the list of human slot indices; M1's coordinator requires `localPlayerId` ∈ humanIds (it is, since the local slot is Human). CPU slots are absent from humanIds → excluded from frame exchange, computed in-sim.

- [ ] **Step 3: FogView — team vision** — `FogView` renders the controlled player's fog via `IsVisibleTo`/`IsExploredBy`. Those are now team-aware, so **no change may be needed** — confirm `FogView` calls `World.IsVisibleTo(Selection.ControlledPlayer, …)` / `IsExploredBy(...)` (grep `IsVisibleTo`/`IsExploredBy` in `godot/scripts/FogView.cs`). Since the methods now union the team, the controlled player automatically sees team vision. If `FogView` reads the raw `_visible` array some other way, route it through `IsVisibleTo`. Report what you found.

- [ ] **Step 4: GameOverScreen — team win** — in `godot/scripts/GameOverScreen.cs`, the Victory/Defeat check currently compares `WinnerId` to the local player (likely `== 0` or `== Selection.ControlledPlayer`). Change it to team-aware: Victory when `World.WinnerId >= 0 && World.SameTeam(localPlayer, World.WinnerId)`, Defeat when `WinnerId >= 0` but not same team, Draw when `WinnerId < 0`. Use the controlled player as `localPlayer`. Grep the current logic and adapt minimally; report the change.

- [ ] **Step 5: Compile-check the whole project** — `dotnet build godot/LlmRts.Godot.csproj --nologo` → must be **green** (this is where Tasks 7–9 land a buildable project). Fix all mismatches.

- [ ] **Step 6: Commit** — `git status -s` (only the intended Godot files), then:

```bash
git add godot/scripts/Main.cs godot/scripts/FogView.cs godot/scripts/GameOverScreen.cs
git commit -m "feat(net): build-on-start wiring (lobby->match, team fog, team-aware game over)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 10: Full gate + 2v2 determinism test + playtest checklist + M4 inputs

**Files:** Create `tests/SimCore.Tests/Team2v2DeterminismTests.cs`; Modify this plan doc.

- [ ] **Step 1: 2v2 CPU determinism replay test** — create `tests/SimCore.Tests/Team2v2DeterminismTests.cs`: build a 4-CPU 2v2 via `BuildMatch`, step N ticks twice from the same seed, assert the final `StateHasher.Hash` is identical across the two runs (proves team play is deterministic). Model it on the existing CPU-vs-CPU determinism test (grep `two-run` / `StateHasher.Hash` in `tests/SimCore.Tests/`); use 4 `MatchSlot`s, teams {0,0,1,1}, all `Cpu`, and ~300 ticks.

```csharp
using System.Collections.Generic;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class Team2v2DeterminismTests
{
    private static SimWorld Build() => MatchSetup.BuildMatch(new List<MatchSlot>
    {
        new(ReferenceFaction.Def, PlayerController.Cpu, AiDifficulty.Medium, 0),
        new(ReferenceFaction.Def, PlayerController.Cpu, AiDifficulty.Easy,   0),
        new(ReferenceFaction.Def, PlayerController.Cpu, AiDifficulty.Hard,   1),
        new(ReferenceFaction.Def, PlayerController.Cpu, AiDifficulty.Medium, 1),
    }, seed: 99);

    [Fact]
    public void Two_Runs_Of_A_2v2_Produce_Identical_Hashes()
    {
        var a = Build(); var b = Build();
        for (int i = 0; i < 300; i++) { a.Step(System.Array.Empty<Command>()); b.Step(System.Array.Empty<Command>()); }
        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
    }
}
```

- [ ] **Step 2: Full gate (Release + Debug)** — `git status -s` first, then:
```bash
dotnet test --configuration Release --nologo -v q
dotnet test --configuration Debug --nologo -v q
```
Expected both green. SimCore.Tests ≈ **345 + 14** new (2 team-model, 3 ally-combat, 3 victory, 2 vision, 2 BuildMatch, 2 codec, 1 2v2-determinism = 15 → ~360), SpriteSlicer 6, 0 failures. (Confirm the exact total; don't hard-fail on the number, hard-fail on any failure.)

- [ ] **Step 3: Determinism gate** — `git diff --stat master -- src/SimCore/Sim/StateHasher.cs tests/SimCore.Tests/DeterminismTests.cs` → **no diff**; `grep -n "GoldenTrajectoryHash =" tests/SimCore.Tests/DeterminismTests.cs` → still `1571756151672809223UL`. If the golden changed, a team change broke solo-equivalence — fix it, do not re-pin.

- [ ] **Step 4: Godot build gate** — `dotnet build godot/LlmRts.Godot.csproj --nologo` → green.

- [ ] **Step 5: Append the playtest checklist + M4 inputs** (below) to this plan and commit:
```bash
git add docs/superpowers/plans/2026-06-15-multiplayer-m3-lobby-teams.md tests/SimCore.Tests/Team2v2DeterminismTests.cs
git commit -m "test(sim): 2v2 CPU determinism replay; docs: M3 playtest checklist + M4 inputs

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Self-Review (author checklist — completed)

- **Spec coverage:** team model (Task 1); ally-immune combat (Task 2); team victory (Task 3); shared team vision (Task 4); `BuildMatch` + 4-corner map (Task 5); `LobbyCodec` testable seam (Task 6); `NetSession` lobby protocol (Task 7); `LobbyScreen` (Task 8); build-on-start + team fog + team game-over (Task 9); 2v2 determinism + gate + checklist + M4 inputs (Task 10). Every spec "in scope" item maps to a task; out-of-scope items (anti-cheat, desync UI, `_hashes` bound, disconnect recovery) are M4.
- **Determinism keystone honored:** Tasks 1–5 are each behavior-preserving for solo teams (`SameTeam` ⟺ same owner; vision unhashed; `Team` unhashed; `WinnerId` stays a player id; `BuildMatch` 2-slot ≡ old builders). Each SimCore task re-verifies the golden `1571756151672809223UL`.
- **Placeholder scan:** complete code in every code step. "Confirm signature" notes (Weapon/BuildingSpec/AddCompletedBuilding ctors, FogView/GameOverScreen current logic, FactionEntry fields) are explicit verification steps with fallbacks — the implementer checks the real source and adjusts the *test/wiring*, never the sim.
- **Type consistency:** `MatchSlot(FactionDef, PlayerController, AiDifficulty, int Team)`, `BuildMatch(IReadOnlyList<MatchSlot>, ulong)`, `SameTeam`/`SetTeam`, `LobbySlot(SlotKind, int, string, AiDifficulty, long)`, `SlotKind{Open,Human,Cpu}`, `LobbyCodec.SlotsToBytes/SlotsFromBytes`, `NetSession.SetLobby/StartMatch/RequestMyFaction/RequestClaim` + events `LobbyUpdated/MatchStarting/FactionRequested/ClaimRequested/PeerConnectedToLobby`, `LockstepCoordinator(localSlot, humanIds, InputDelay)`, `Runner.InitNetworked(world, coord, net, localSlot)` — names used identically across tasks. `Team` not hashed; `IsVisibleTo`/`IsExploredBy` signatures unchanged (behavior only).
- **Build ordering caveat:** Task 7 (NetSession) may leave Main non-compiling until Task 9; the green Godot build lands at Task 9 Step 5. Flagged in both tasks.

---

## Execution Outcome (M3, completed 2026-06-15)

All 10 tasks done via subagent-driven development (foreground implementers + two-stage review).
Branch `feat/net-m3-lobby-teams`. **Final gate: Release == Debug, 362 SimCore + 6 SpriteSlicer
tests, 0 failures; Godot build 0/0; `StateHasher`/`DeterminismTests` byte-untouched → golden
trajectory hash unchanged at `1571756151672809223UL` (the solo-team keystone held across every
SimCore change — no re-pin); working tree clean.** A 2v2 four-CPU determinism replay proves team
play (shared vision, ally-immune combat, AI team-awareness) is deterministic.

SimCore (TDD, all behavior-preserving for solo): per-player `Team` + `SameTeam`/`SetTeam`;
ally-immune `AcquireTarget` + no-friendly-fire; team victory (representative `WinnerId`); shared
team vision (`IsVisibleTo`/`IsExploredBy` union); `MatchSetup.BuildMatch(2–4 slots, 4-corner map)`
with the 1v1 builders delegating to it. **Review-caught gap fixed:** three CPU AI enemy-detection
helpers (`EnemyBaseCenter`/`ThreatenedBuildingCenter`/`EnemyCombatCount`) used owner-based "enemy",
so a CPU would target its ally — routed through `SameTeam`. A completeness review confirmed every
friend/enemy site is now team-aware and every "is-mine" site correctly stays owner-based, with no
targeting↔retaliation inconsistency.

`SimCore.Net`: `LobbyCodec`/`LobbySlot` (headless round-trip tested). Godot (compile + reviewed):
`NetSession` lobby protocol (host-authoritative sync + start-config broadcast, reliable channel-0);
`LobbyScreen` (slot table, claim, faction pick, Start gate); build-on-start wiring (resolve
factions → `BuildMatch` → `Runner.Init` + `ForceSync` + coordinator with `humanIds`); team-aware
`GameOverScreen`; `FogView` needed no change (it already reads the now-team-aware visibility).

**Network-layer logic review verdict (pre-playtest):** safe for a same-machine 2v2 playtest after
one must-fix, which was applied: a client that owns no Human slot at Start now **aborts loudly**
instead of silently driving slot 0 (the host's army). Also gated the debug Tab side-switch behind
`!IsNetworked`. Pack-content divergence across machines desync-halts loudly (not silent corruption)
and is deferred to M4.

## Two-instance 2v2 LAN playtest checklist (for the user)

Launch **two** instances (`scripts/play.ps1` twice). Same machine → same `packs/` → faction
resolution is automatically identical.
1. **Host a lobby:** Instance A → menu → "Host LAN game". Configure a 2v2: e.g. slot 0 = you
   (team A), add slots so one team is you + a CPU and the other is two CPUs (use the Kind/Team/
   Difficulty/faction controls). Instance B → "Join" (`127.0.0.1`).
2. **Claim a seat (important):** on Instance B, click **Claim** on an open seat and wait for the
   lobby to show you as a Human slot before the host presses **Start Match**. (If a peer owns no
   seat at Start, it now aborts with a console error rather than mis-driving the host.)
3. **Start + team sync:** both instances build the same 2v2 and run. Each human commands only its
   own units. Allies **do not** attack each other; you **share vision** with your ally. Economy/
   combat look identical on both screens; no "DESYNC detected" in the console.
4. **Team victory:** eliminate a team's buildings → **Victory** on the winning team's members and
   **Defeat** on the losers, on both instances.
5. **CPU teammates:** confirm a CPU ally attacks the *enemy* base (not yours) and doesn't fight
   your units.
6. Report any desync (with the console `DesyncTick`) or any peer that mis-controls another's units.

## Plan-M4 Inputs (carry-forward) — robustness + polish

- **Desync-halt screen** on `SimRunner.Desynced` (currently logs+pauses), surfacing `DesyncTick`.
- **Anti-cheat / input validation:** reject any command whose `PlayerId` ≠ the submitting peer's
  slot at the receive boundary (`ReceiveFrameRpc` vs `GetRemoteSenderId()`'s assigned slot). The
  M3 mitigations (`localSlot` lock + abort-on-no-slot + Tab gating) are UI-side only.
- **Lobby hardening (review-flagged):** the host's Kind/Remove-slot actions can orphan a remote
  occupant (it falls into the abort path); tighten the host UI / `OnStart` to reject Human slots
  whose `OccupantPeerId` isn't a currently-connected peer, and don't let Remove drop an occupied
  slot.
- **Cross-machine pack sync (review-flagged):** hash each peer's `PackCatalog` in the start
  handshake (or sync the chosen pack bytes) so a pack-content mismatch is reported in the lobby
  instead of desync-halting at tick 0. (Same-machine play is unaffected.)
- **Bound the coordinator `_hashes` buffer** (sliding window) — carried from M1.
- **Disconnect recovery** beyond pause-on-`PeerDropped`; **input-delay tuning** (`NetSession.InputDelay`).
- **Team-color polish (review note):** ally units render in their own player color via shared
  vision (correct), but aren't "always-visible" like your own; optional ally-always-visible/team
  tint for a polished 2v2 (`Minimap.cs`, unit tinting).
