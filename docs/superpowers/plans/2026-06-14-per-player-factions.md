# Per-Player Factions Implementation Plan (5b)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give each player their own `FactionDef` so command resolution and the faction mechanic key off the acting/owning player, enabling a human's faction vs the CPU's (5e).

**Architecture:** Replace `SimWorld`'s single `FactionDef? Faction` with a `FactionDef?[] _factions` + `FactionFor(playerId)`. Keep the single-faction constructor (fills every slot) and add a per-player array constructor. Route `Build`/`Train`/`Research` resolution off `cmd.PlayerId` and the shield mechanic off `unit.OwnerId`. Behavior-preserving under a shared faction → determinism golden unchanged.

**Tech Stack:** C# / .NET 8, xUnit. SimCore (deterministic core). No floats; factions are immutable config (not hashed).

**Source spec:** `docs/superpowers/specs/2026-06-14-per-player-factions-design.md`

---

## Conventions for every task

- Run from repo root `C:\Users\lssha\llm-rts`. If `dotnet` missing: bash `export PATH="$PATH:/c/Program Files/dotnet"`.
- Run SimCore tests: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
- **Baseline:** 293 SimCore tests pass; golden trajectory hash = `9352778236967924871UL` (must stay unchanged — no re-pin in 5b).
- After each commit, confirm `git log --oneline -1`. End commit messages with `Co-Authored-By: RuFlo <ruv@ruv.net>`.

## Engine facts (verified against source)

- `SimWorld.cs`: `public FactionDef? Faction { get; }` (line 11); ctor (lines 23-30) sets `Faction = faction;` and inits `_players`. `private readonly PlayerState[] _players;` (line 20).
- `Apply` resolution sites in `SimWorld.cs`: `BuildCommand` `var bdef = Faction?.GetBuilding(bc.BuildingDefId);` (line 172); `TrainCommand` `var udef = Faction?.GetUnit(tc.UnitDefId);` (line 185); `ResearchCommand` `var rup = Faction?.GetUpgrade(rc.UpgradeDefId);` (line 291). Each command record has a `PlayerId`.
- `SimWorld.Mechanics.cs`: `MechanicFor(Unit u) => Faction?.Mechanic;` (line 8); `InitialShield()` (lines 13-14) `=> Faction?.Mechanic is {RegeneratingShields} m ? m.MaxShield : 0;`; `UpdateShields()` (lines 34-47) has a global guard `if (Faction?.Mechanic is not {RegeneratingShields} m) return;` then iterates `_units` applying `m`. `Spawn(...)` in `SimWorld.cs:59` sets `ShieldHp = InitialShield()` (ownerId is a Spawn parameter).
- Godot is unaffected: `TestMap` uses `new SimWorld(map, seed, faction: ReferenceFaction.Def)` (the kept ctor) and the HUD reads `World.Faction` (kept as the player-0 alias).
- `FactionDef` ctor: `(string id, string name, IEnumerable<UnitDef> units, IEnumerable<BuildingDef> buildings, IEnumerable<UpgradeDef> upgrades, MechanicDef? mechanic = null)` and `(string id, string name, IEnumerable<UnitDef> units, IEnumerable<BuildingDef> buildings)`. `MechanicDef(MechanicKind Kind, int MaxShield, int RegenPerTick, int RegenDelayTicks)`. `Unit.ShieldHp` / `Unit.TicksSinceDamaged` are public settable (SimCore.Tests has InternalsVisibleTo).

## File Structure

- `src/SimCore/Sim/SimWorld.cs` — MODIFY: storage, `FactionFor`, `Faction` alias, second ctor; route 3 Apply sites; `InitialShield(ownerId)` call site.
- `src/SimCore/Sim/SimWorld.Mechanics.cs` — MODIFY (Task 2): `MechanicFor`, `InitialShield(ownerId)`, `UpdateShields` per-owner.
- `tests/SimCore.Tests/PerPlayerFactionTests.cs` — NEW: across both tasks.

---

## Task 1: Per-player faction storage + constructors + command resolution

**Files:**
- Modify: `src/SimCore/Sim/SimWorld.cs`
- Test: `tests/SimCore.Tests/PerPlayerFactionTests.cs`

Mechanics (`MechanicFor`/`InitialShield`/`UpdateShields`) are left referencing the `Faction` alias in this task — under a shared faction that equals `FactionFor(0)`, so they keep compiling and the golden stays green. Task 2 migrates them to per-owner.

- [ ] **Step 1: Write the failing tests**

Create `tests/SimCore.Tests/PerPlayerFactionTests.cs`:

```csharp
using System.Collections.Generic;
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class PerPlayerFactionTests
{
    private static readonly string[] None = System.Array.Empty<string>();
    private static readonly List<Command> Empty = new();

    // Minimal faction: one depot-style building "base_<id>" + one unit "u_<id>", optional mechanic.
    private static FactionDef OneUnitFaction(string id, MechanicDef? mechanic = null)
    {
        var depot = new BuildingSpec(100, 2, 2, 50, 5, IsDepot: true);
        var unitSpec = new UnitSpec(30, Fix.One, 50, 1, 5);
        return new FactionDef(id, id,
            units: new[] { new UnitDef("u_" + id, 1, "base_" + id, None, unitSpec) },
            buildings: new[] { new BuildingDef("base_" + id, 1, None, depot) },
            upgrades: System.Array.Empty<UpgradeDef>(),
            mechanic: mechanic);
    }

    [Fact]
    public void FactionFor_Returns_Each_Players_Faction()
    {
        var a = OneUnitFaction("A");
        var b = OneUnitFaction("B");
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, new FactionDef?[] { a, b });
        Assert.Same(a, w.FactionFor(0));
        Assert.Same(b, w.FactionFor(1));
        Assert.Same(a, w.Faction); // alias = player 0
    }

    [Fact]
    public void Shared_Faction_Ctor_Applies_To_All_Players()
    {
        var a = OneUnitFaction("A");
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: a); // playerCount default 2
        Assert.Same(a, w.FactionFor(0));
        Assert.Same(a, w.FactionFor(1));
    }

    [Fact]
    public void Build_Resolves_Against_Acting_Players_Faction()
    {
        var a = OneUnitFaction("A");
        var b = OneUnitFaction("B");
        var w = new SimWorld(new MapGrid(30, 30), seed: 1, new FactionDef?[] { a, b });
        w.Players[0].Minerals = 1000;
        w.Players[1].Minerals = 1000;
        int w0 = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.One, 30);
        int w1 = w.SpawnUnit(1, w.Map.CellCenter(20, 20), Fix.One, 30);

        // Player 0 builds A's building (in range of worker at (5,5)).
        w.Step(new List<Command> { new BuildCommand(0, w0, "base_A", 6, 5) });
        Assert.Contains(w.Buildings, x => x.OwnerId == 0 && x.DefId == "base_A");

        // Player 1 builds B's building (FactionFor(1) == B).
        w.Step(new List<Command> { new BuildCommand(1, w1, "base_B", 21, 20) });
        Assert.Contains(w.Buildings, x => x.OwnerId == 1 && x.DefId == "base_B");

        // Player 0 tries B's def id (base_B) IN RANGE of its worker — rejected: A's catalog lacks it.
        int before = w.Buildings.Count;
        w.Step(new List<Command> { new BuildCommand(0, w0, "base_B", 6, 7) });
        Assert.Equal(before, w.Buildings.Count);
    }
}
```

- [ ] **Step 2: Run the tests, expect failure**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
Expected: compile FAILS — the `SimWorld(MapGrid, ulong, FactionDef?[])` ctor and `FactionFor` do not exist yet.

- [ ] **Step 3: Replace the `Faction` property with per-player storage + accessors**

In `src/SimCore/Sim/SimWorld.cs`, replace this line (line 11):

```csharp
    public FactionDef? Faction { get; }
```

with:

```csharp
    private readonly FactionDef?[] _factions;

    /// <summary>The faction for a given player (null if that slot has none).</summary>
    public FactionDef? FactionFor(int playerId) => _factions[playerId];

    /// <summary>Player 0's faction. Back-compat alias for callers (e.g. the Godot HUD building
    /// the human's catalog menus) that predate per-player factions; the human is player 0.</summary>
    public FactionDef? Faction => _factions.Length > 0 ? _factions[0] : null;
```

- [ ] **Step 4: Update the existing ctor and add the per-player ctor**

In `src/SimCore/Sim/SimWorld.cs`, replace the existing constructor (the `SimWorld(MapGrid map, ulong seed, int playerCount = 2, FactionDef? faction = null)` body):

```csharp
    public SimWorld(MapGrid map, ulong seed, int playerCount = 2, FactionDef? faction = null)
    {
        Map = map;
        Rng = new DeterministicRandom(seed);
        Faction = faction;
        _players = new PlayerState[playerCount];
        for (int i = 0; i < playerCount; i++) _players[i] = new PlayerState();
    }
```

with:

```csharp
    public SimWorld(MapGrid map, ulong seed, int playerCount = 2, FactionDef? faction = null)
    {
        Map = map;
        Rng = new DeterministicRandom(seed);
        _players = new PlayerState[playerCount];
        _factions = new FactionDef?[playerCount];
        for (int i = 0; i < playerCount; i++) { _players[i] = new PlayerState(); _factions[i] = faction; }
    }

    /// <summary>Per-player factions; playerCount = factions.Length. The array is copied.</summary>
    public SimWorld(MapGrid map, ulong seed, FactionDef?[] factions)
    {
        Map = map;
        Rng = new DeterministicRandom(seed);
        int playerCount = factions.Length;
        _players = new PlayerState[playerCount];
        for (int i = 0; i < playerCount; i++) _players[i] = new PlayerState();
        _factions = (FactionDef?[])factions.Clone();
    }
```

- [ ] **Step 5: Route the three Apply resolution sites to the acting player's faction**

In `src/SimCore/Sim/SimWorld.cs`, make these three one-line changes:

- `BuildCommand` (line ~172): `var bdef = Faction?.GetBuilding(bc.BuildingDefId);` → `var bdef = FactionFor(bc.PlayerId)?.GetBuilding(bc.BuildingDefId);`
- `TrainCommand` (line ~185): `var udef = Faction?.GetUnit(tc.UnitDefId);` → `var udef = FactionFor(tc.PlayerId)?.GetUnit(tc.UnitDefId);`
- `ResearchCommand` (line ~291): `var rup = Faction?.GetUpgrade(rc.UpgradeDefId);` → `var rup = FactionFor(rc.PlayerId)?.GetUpgrade(rc.UpgradeDefId);`

- [ ] **Step 6: Run the tests, expect pass**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
Expected: PASS, 293 + 3 = 296. The 3 determinism tests STILL pass with the golden unchanged (the scenario uses a shared faction, so `FactionFor(p)` == the old `Faction` for every `p`; Mechanics still read the `Faction` alias = `FactionFor(0)` = the shared faction).

- [ ] **Step 7: Commit**

```bash
git add src/SimCore/Sim/SimWorld.cs tests/SimCore.Tests/PerPlayerFactionTests.cs
git commit -m "feat(sim): per-player faction storage + FactionFor; route command resolution

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 2: Per-owner faction mechanic (shields) + determinism gate

**Files:**
- Modify: `src/SimCore/Sim/SimWorld.Mechanics.cs`
- Modify: `src/SimCore/Sim/SimWorld.cs` (the `InitialShield()` call site in `Spawn`)
- Test: `tests/SimCore.Tests/PerPlayerFactionTests.cs` (add)

- [ ] **Step 1: Write the failing test**

Add to `PerPlayerFactionTests.cs` (inside the class):

```csharp
    [Fact]
    public void Shields_Are_Per_Owner_Faction()
    {
        var shielded = new MechanicDef(MechanicKind.RegeneratingShields, MaxShield: 10, RegenPerTick: 5, RegenDelayTicks: 2);
        var a = OneUnitFaction("A", shielded); // player 0: regenerating shields
        var b = OneUnitFaction("B", null);     // player 1: no mechanic
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, new FactionDef?[] { a, b });

        int u0 = w.SpawnUnit(0, w.Map.CellCenter(3, 3), Fix.One, 100);
        int u1 = w.SpawnUnit(1, w.Map.CellCenter(15, 15), Fix.One, 100);

        // Initial shields come from each owner's faction.
        Assert.Equal(10, w.GetUnit(u0)!.ShieldHp);
        Assert.Equal(0, w.GetUnit(u1)!.ShieldHp);

        // Drain both shields, then step: only the shields-faction unit regenerates.
        w.GetUnit(u0)!.ShieldHp = 0; w.GetUnit(u0)!.TicksSinceDamaged = 0;
        w.GetUnit(u1)!.ShieldHp = 0; w.GetUnit(u1)!.TicksSinceDamaged = 0;
        for (int i = 0; i < 5; i++) w.Step(Empty);

        Assert.Equal(10, w.GetUnit(u0)!.ShieldHp); // regenerated to MaxShield
        Assert.Equal(0, w.GetUnit(u1)!.ShieldHp);  // owner has no mechanic → never regenerates
    }
```

- [ ] **Step 2: Run the test, expect failure**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q --filter "FullyQualifiedName~Shields_Are_Per_Owner_Faction"`
Expected: FAIL — currently `MechanicFor`/`InitialShield`/`UpdateShields` key off the shared `Faction` (== `FactionFor(0)` == faction A), so the player-1 unit (`u1`) would wrongly get faction A's shields (spawn `ShieldHp == 10`, not 0). The first assertion `Assert.Equal(0, u1.ShieldHp)` fails.

- [ ] **Step 3: Migrate the mechanic accessors to per-owner**

In `src/SimCore/Sim/SimWorld.Mechanics.cs`, replace the three members.

`MechanicFor` (line 8):
```csharp
    /// <summary>The mechanic governing this unit: its OWNER's faction mechanic. Null when none.</summary>
    private MechanicDef? MechanicFor(Unit u) => FactionFor(u.OwnerId)?.Mechanic;
```

`InitialShield()` (lines 13-14) → take an owner id:
```csharp
    /// <summary>Initial shield pool for a unit spawned under its owner's faction.</summary>
    private int InitialShield(int ownerId) =>
        FactionFor(ownerId)?.Mechanic is { Kind: MechanicKind.RegeneratingShields } m ? m.MaxShield : 0;
```

`UpdateShields()` (lines 34-47) → per-unit owner mechanic (drop the single global guard/`m`):
```csharp
    /// <summary>Per-tick faction-mechanic update (regenerating shields), keyed per unit off
    /// its owner's faction. Units whose owner has no shield mechanic are skipped (zero churn).
    /// See the TicksSinceDamaged note: ApplyDamage resets to 0 (UpdateCombat, before this),
    /// UpdateShields transitions 0->1 then increments; regen delay accounts for the +1.</summary>
    private void UpdateShields()
    {
        foreach (var u in _units)
        {
            if (MechanicFor(u) is not { Kind: MechanicKind.RegeneratingShields } m) continue;
            if (u.TicksSinceDamaged == 0)
                u.TicksSinceDamaged = 1;
            else
                u.TicksSinceDamaged++;
            if (u.TicksSinceDamaged >= m.RegenDelayTicks && u.ShieldHp < m.MaxShield)
                u.ShieldHp = System.Math.Min(m.MaxShield, u.ShieldHp + m.RegenPerTick);
        }
    }
```

(`HasShields(u)` already calls `MechanicFor(u)`, so it becomes per-owner automatically — leave it as is.)

- [ ] **Step 4: Update the `InitialShield()` call site in `Spawn`**

In `src/SimCore/Sim/SimWorld.cs`, in the `Spawn(...)` method, change `ShieldHp = InitialShield()` to pass the owner:

```csharp
                SightRange = sightRange, ShieldHp = InitialShield(ownerId)
```

(`ownerId` is already a parameter of `Spawn`.)

- [ ] **Step 5: Run the test, expect pass**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
Expected: PASS, 296 + 1 = 297.

- [ ] **Step 6: Determinism gate — full suite Release + Debug, golden UNCHANGED**

The `UpdateShields` rewrite must preserve the shared-faction scenario (which has a shields faction shared by both players) byte-for-byte.

Run: `dotnet test --configuration Release --nologo -v q`
Expected: PASS — SimCore.Tests 297, SpriteSlicer.Tests 6, 0 failures. The 3 determinism tests pass with `GoldenTrajectoryHash` STILL `9352778236967924871UL` (NO re-pin). If the golden test fails, the `UpdateShields` rewrite changed behavior — STOP and reconcile (it must be exactly equivalent for a shared faction); do NOT re-pin the golden to paper over it.

Run: `dotnet test --configuration Debug --nologo -v q`
Expected: PASS (Debug == Release).

- [ ] **Step 7: Commit**

```bash
git add src/SimCore/Sim/SimWorld.Mechanics.cs src/SimCore/Sim/SimWorld.cs tests/SimCore.Tests/PerPlayerFactionTests.cs
git commit -m "feat(sim): per-owner faction mechanic (shields key off unit owner)

UpdateShields/MechanicFor/InitialShield resolve the unit owner's faction. Behavior-
preserving under a shared faction (determinism golden unchanged, no re-pin).

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Self-Review (author checklist — completed)

- **Spec coverage:** storage + `FactionFor` + alias + two ctors (Task 1 steps 3-4); Build/Train/Research resolution per acting player (Task 1 step 5); mechanic per owner — `MechanicFor`/`InitialShield`/`UpdateShields` + Spawn call site (Task 2); tests for FactionFor/shared-ctor/build-resolution/shields-per-owner (both tasks); determinism golden unchanged, no re-pin (Task 2 step 6). Every spec "In scope" item maps to a step.
- **Placeholder scan:** none — all edits show exact old→new code; no TBDs.
- **Type consistency:** `FactionFor(int)`, `_factions` (`FactionDef?[]`), `InitialShield(int ownerId)`, `MechanicFor(Unit)` used consistently; the new array ctor signature `SimWorld(MapGrid, ulong, FactionDef?[])` matches the test call `new SimWorld(map, seed, new FactionDef?[]{a,b})`; `Faction` alias preserved for Godot. The golden constant `9352778236967924871UL` is referenced as the unchanged value (no re-pin), consistent with 5a's result.
