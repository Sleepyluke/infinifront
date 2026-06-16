# Base-Building Expansion — Supply, Defense Tower, Textured Ground — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement the CODE tasks. Steps use checkbox (`- [ ]`) syntax. ART tasks (marked **[ART — Claude runs]**) are done by the lead via the nano-banana skill, not by an implementer subagent.

**Goal:** Add a buildable supply structure, a worker-built defense tower that auto-fires (new building-combat sim mechanic), and a tiled ground texture — all golden-safe (no re-pin).

**Architecture:** SimCore content (new `BuildingSpec`s + `ReferenceFaction` defs) + a new building-combat pass (`BuildingSpec.Weapon` → `Building.Weapon` → `UpdateBuildingCombat`, reusing an extracted `AcquireTargetAt` + `ApplyDamage`). The hash folds the building weapon **only when present**, so weaponless worlds (incl. the golden scenario) are byte-identical. Godot `BuildingView` already loads `res://assets/buildings/<defId>.png`, so new building sprites need no code; `MapView` gets a tiled ground texture.

**Tech Stack:** C# (.NET 8), SimCore, xUnit; Godot 4.6 render layer; nano-banana (`gemini-3-pro-image`) + `scripts/keysprite.py`.

**Spec:** `docs/superpowers/specs/2026-06-15-base-building-expansion-design.md`.

**DETERMINISM KEYSTONE:** golden trajectory hash `1571756151672809223UL` must stay UNCHANGED across all tasks. Building combat is a **no-op for weaponless buildings** and the weapon is hashed **only when present** — so the towerless golden scenario folds nothing new. The Debug+Release gate proves it; if the golden moves, the no-op/fold wasn't clean — FIX it, do not re-pin.

**Landmine:** `src/SimCore*` files `using SimCore.Math;` shadows `System.Math` — qualify `System.Math` if needed.

---

## Confirmed source facts (from exploration)

- `WeaponSpec(int Damage, Fix Range, int CooldownTicks)` with `.Instantiate() → Weapon` (`Specs.cs:7`). Runtime `Weapon` class has `Damage/Range/CooldownTicks` (init) + `CooldownRemaining` (set) (`Weapon.cs`).
- `BuildingSpec(MaxHp, Width, Height, MineralCost, BuildTimeTicks, SupplyProvided=0, IsDepot=false, CanTrain=false, SightRange=8)` (`Specs.cs:19`) — NO weapon yet.
- `Building` (`Building.cs`): class, init props + mutable; NO weapon yet.
- `PlaceBuilding` (`SimWorld.Buildings.cs:31`) is the SINGLE building-creation point (both `AddCompletedBuilding` and construction route through it).
- `Step` pipeline (`SimWorld.cs`): `…UpdateCombat()` (line 118) → `MoveUnits()` (119) → … `RemoveDeadBuildings()`.
- `AcquireTarget(Unit u, Fix range)` (`SimWorld.Combat.cs:~237`): nearest enemy (units then buildings), `SameTeam`-filtered, `IsVisibleTo(u.OwnerId)`-gated, range-filtered. Uses `u.Position`/`u.OwnerId`.
- `ApplyDamage(Unit, int)` shield-aware; building damage is `building.Hp -= dmg`. `TryResolveTarget(id, out pos, out unit, out building)`. `CenterOf(building)`.
- `StateHasher` building fold: `SimWorld.Sim/StateHasher.cs:106-135` (after `CanTrain`, before `Queue`).
- AI `SupplyDef(int p)` (`SimWorld.Ai.cs:73`): returns first `BuildingList` building with `SupplyProvided>0`.
- `ReferenceSpecs` (`ReferenceSpecs.cs`): Depot/Barracks. `ReferenceFaction` (`ReferenceFaction.cs:21-22`): `new BuildingDef("depot", 1, None, ReferenceSpecs.Depot)` etc. — confirm what `None` is (an empty requires) and the `BuildingDef(id, tier, requires, spec)` signature before editing.

---

# PHASE W1 — Supply building ("Supply Silo")

## Task W1-1: SupplySilo spec + faction def + CPU supply preference (SimCore, TDD)

**Files:** Modify `src/SimCore/Sim/ReferenceSpecs.cs`, `src/SimCore/Sim/ReferenceFaction.cs`, `src/SimCore/Sim/SimWorld.Ai.cs`; Test `tests/SimCore.Tests/SupplyBuildingTests.cs`.

- [ ] **Step 1: Write the failing test**

Create `tests/SimCore.Tests/SupplyBuildingTests.cs`:

```csharp
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class SupplyBuildingTests
{
    [Fact]
    public void SupplySilo_Provides_Supply_And_Does_Not_Train()
    {
        Assert.True(ReferenceSpecs.SupplySilo.SupplyProvided > 0);
        Assert.False(ReferenceSpecs.SupplySilo.IsDepot);
        Assert.False(ReferenceSpecs.SupplySilo.CanTrain);
    }

    [Fact]
    public void Reference_Has_A_Supply_Building_Def()
    {
        Assert.Contains(ReferenceFaction.Def.BuildingList, b => b.Id == "supply");
    }

    [Fact]
    public void Completing_A_Silo_Raises_Supply_Cap()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, playerCount: 2,
            faction: ReferenceFaction.Def);
        int before = w.Players[0].SupplyCap;
        w.AddCompletedBuilding(0, ReferenceSpecs.SupplySilo, 2, 2, "supply");
        Assert.Equal(before + ReferenceSpecs.SupplySilo.SupplyProvided, w.Players[0].SupplyCap);
    }
}
```

**Before running:** confirm `new SimWorld(MapGrid, seed, int playerCount, FactionDef? faction)` ctor signature (used widely; it exists) and that `ReferenceFaction.Def.BuildingList` is the building accessor. Adjust if the accessor differs.

- [ ] **Step 2: Run, expect failure** — `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q` (prepend `export PATH="$PATH:/c/Program Files/dotnet"` in bash if needed) → compile FAIL (`SupplySilo` missing).

- [ ] **Step 3: Implement**

`ReferenceSpecs.cs` — add after `Barracks`:

```csharp
    public static readonly BuildingSpec SupplySilo = new(
        MaxHp: 200, Width: 2, Height: 2, MineralCost: 100, BuildTimeTicks: 120,
        SupplyProvided: 8, IsDepot: false, CanTrain: false, SightRange: 5);
```

`ReferenceFaction.cs` — add to the building list (match the existing `BuildingDef(id, tier, requires, spec)` shape; `None` = same empty-requires used by depot/barracks):

```csharp
            new BuildingDef("supply", 1, None, ReferenceSpecs.SupplySilo),
```

`SimWorld.Ai.cs` `SupplyDef` — prefer a dedicated (non-depot, non-train) supply building, else fall back to any:

```csharp
    private BuildingDef? SupplyDef(int p)
    {
        var f = FactionFor(p);
        if (f is null) return null;
        BuildingDef? fallback = null;
        foreach (var b in f.BuildingList)
            if (b.Spec.SupplyProvided > 0)
            {
                if (!b.Spec.IsDepot && !b.Spec.CanTrain) return b;  // prefer the cheap supply-only building
                fallback ??= b;
            }
        return fallback;
    }
```

- [ ] **Step 4: Run, expect pass.** Also confirm the golden determinism test still passes (it must — ReferenceFaction isn't in the golden scenario, and the AI change is behavior-preserving when no silo exists).

- [ ] **Step 5: Regenerate the reference pack + fix count asserts**

The pack drift-guard test exports `packs/reference/faction.json` from `ReferenceFaction.Def`; regenerate it:
```bash
UPDATE_PACKS=1 dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q
```
Then run the suite normally; if any test asserts a building COUNT (grep `BuildingList.Count` / `Buildings.Count` in tests), bump it. Re-run green.

- [ ] **Step 6: Commit**

```bash
git status -s   # only the intended files
git add src/SimCore/Sim/ReferenceSpecs.cs src/SimCore/Sim/ReferenceFaction.cs src/SimCore/Sim/SimWorld.Ai.cs tests/SimCore.Tests/SupplyBuildingTests.cs packs/reference/faction.json
git commit -m "feat(sim): Supply Silo building (+8 supply, worker-built); CPU prefers it for supply

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

## Task W1-ART **[ART — Claude runs]**

Generate `supply` sprite via nano-banana (`gemini-3-pro-image`, `--ref` depot-pro.png for consistency): an industrial supply silo/generator, smaller/lesser than the Depot, gunmetal+yellow, red faction trim, magenta bg. Review (Read the PNG), iterate; then `keysprite.py … --out godot/assets/buildings/supply.png --size 256`; commit the raw + processed PNGs. `BuildingView` loads it by def-id automatically.

---

# PHASE W2 — Textured ground

## Task W2-ART **[ART — Claude runs]**

Generate a **seamless tileable** ground texture via nano-banana (prompt: "seamless tileable texture, edges wrap perfectly, top-down sci-fi cracked rockcrete/dirt ground, subtle, no distinct focal object, 512x512"). NOT magenta-keyed (opaque). Save to `godot/assets/world/ground.png` (downscale to 256×256 power-of-two via Pillow, no chroma-key). Review for tiling seams; iterate the prompt.

## Task W2-1: Tile the ground in MapView (Godot, compile + playtest)

**Files:** Modify `godot/scripts/MapView.cs`.

- [ ] **Step 1: Load + tile the ground texture**

In `MapView`, load `res://assets/world/ground.png` if it exists; in `_Draw`, replace the flat tan fill with the tiled texture (keep the rock-square loop, grid lines, and the building/node cell-skip from the last fix):

```csharp
    private Texture2D? _ground;
    // in Init: const string g = "res://assets/world/ground.png"; if (ResourceLoader.Exists(g)) _ground = ResourceLoader.Load<Texture2D>(g);
```

In `_Draw`, replace `DrawRect(new Rect2(0,0,map.Width*px, map.Height*px), new Color(0.42f,0.38f,0.32f));` with:

```csharp
        var full = new Rect2(0, 0, map.Width * px, map.Height * px);
        if (_ground is not null)
            DrawTextureRectRegion(_ground, full, new Rect2(0, 0, map.Width * px, map.Height * px), tile: true);
        else
            DrawRect(full, new Color(0.42f, 0.38f, 0.32f));
```

> Tiling in Godot 4: set the texture to repeat. If `DrawTextureRectRegion(..., tile:true)` doesn't repeat, set `TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled` on the MapView node (in `_Ready` or Init) and use `DrawTextureRect(_ground, full, tile: true)`. Confirm the API in this Godot version; the goal is the ground texture tiled across the whole map with no stretching. Report which call worked.

- [ ] **Step 2: Compile-check** — `dotnet build godot/LlmRts.Godot.csproj --nologo` clean. (Visual seams verified by playtest.)

- [ ] **Step 3: Commit** — `MapView.cs` + the ground asset (+ .import after a headless import).

---

# PHASE W3 — Defense tower ("Sentry Turret") — building-combat feature

## Task W3-1: Building weapons — BuildingSpec.Weapon + Building.Weapon + instantiate (SimCore, TDD)

**Files:** Modify `src/SimCore/Sim/Specs.cs`, `src/SimCore/Sim/Building.cs`, `src/SimCore/Sim/SimWorld.Buildings.cs`; Test `tests/SimCore.Tests/BuildingWeaponTests.cs`.

- [ ] **Step 1: Failing test**

Create `tests/SimCore.Tests/BuildingWeaponTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class BuildingWeaponTests
{
    private static BuildingSpec Armed() => new(
        MaxHp: 250, Width: 2, Height: 2, MineralCost: 150, BuildTimeTicks: 1, SightRange: 9,
        Weapon: new WeaponSpec(Damage: 12, Range: Fix.FromInt(6), CooldownTicks: 8));

    [Fact]
    public void Weaponless_Building_Has_Null_Weapon()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, playerCount: 2, faction: null);
        int id = w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 2, 2, "depot");
        Assert.Null(w.GetBuilding(id)!.Weapon);
    }

    [Fact]
    public void Armed_Building_Gets_A_Cloned_Weapon()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, playerCount: 2, faction: null);
        int a = w.AddCompletedBuilding(0, Armed(), 2, 2, "tower");
        int b = w.AddCompletedBuilding(0, Armed(), 6, 6, "tower");
        var wa = w.GetBuilding(a)!.Weapon;
        var wb = w.GetBuilding(b)!.Weapon;
        Assert.NotNull(wa);
        Assert.NotNull(wb);
        Assert.NotSame(wa, wb);              // no aliasing — cooldown state is per-building
        Assert.Equal(12, wa!.Damage);
    }
}
```

**Before running:** confirm `BuildingSpec` is a positional record so adding `WeaponSpec? Weapon = null` as a new trailing optional param is source-compatible with all existing call sites (they use named or positional-without-weapon args — `SightRange` is already optional, so a new trailing optional is fine). Confirm `SimWorld.GetBuilding(int)` exists (it does, `SimWorld.Buildings.cs:11`).

- [ ] **Step 2: Run, expect failure** (compile — `Weapon` not on BuildingSpec/Building).

- [ ] **Step 3: Implement**

`Specs.cs` — add `WeaponSpec? Weapon` as the new trailing optional param of `BuildingSpec`:

```csharp
public sealed record BuildingSpec(
    int MaxHp, int Width, int Height, int MineralCost, int BuildTimeTicks,
    int SupplyProvided = 0, bool IsDepot = false, bool CanTrain = false, int SightRange = 8,
    WeaponSpec? Weapon = null);
```

`Building.cs` — add the runtime weapon:

```csharp
    public Weapon? Weapon { get; set; }   // non-null only for buildings with a WeaponSpec (e.g. towers)
```

`SimWorld.Buildings.cs` `PlaceBuilding` — instantiate it in the `new Building { … }` initializer:

```csharp
        var b = new Building { Id = _nextId++, OwnerId = ownerId, CellX = cellX, CellY = cellY, DefId = defId, Spec = spec, Hp = spec.MaxHp, Weapon = spec.Weapon?.Instantiate() };
```

- [ ] **Step 4: Run, expect pass.** Golden unchanged (no building has a weapon yet; the new field is null everywhere existing).

- [ ] **Step 5: Commit** (`git status -s` first):

```bash
git add src/SimCore/Sim/Specs.cs src/SimCore/Sim/Building.cs src/SimCore/Sim/SimWorld.Buildings.cs tests/SimCore.Tests/BuildingWeaponTests.cs
git commit -m "feat(sim): BuildingSpec.Weapon + per-building cloned Weapon (no aliasing)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

## Task W3-2: Building-combat pass (SimCore, TDD)

**Files:** Modify `src/SimCore/Sim/SimWorld.Combat.cs`, `src/SimCore/Sim/SimWorld.cs`; Test `tests/SimCore.Tests/BuildingCombatTests.cs`.

- [ ] **Step 1: Failing test**

Create `tests/SimCore.Tests/BuildingCombatTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class BuildingCombatTests
{
    private static BuildingSpec Turret() => new(
        MaxHp: 250, Width: 2, Height: 2, MineralCost: 150, BuildTimeTicks: 1, SightRange: 9,
        Weapon: new WeaponSpec(Damage: 12, Range: Fix.FromInt(6), CooldownTicks: 8));

    private static SimWorld World()
    {
        var w = new SimWorld(new MapGrid(24, 24), seed: 1, playerCount: 4, faction: null);
        w.FogEnabled = false;   // isolate combat from vision
        return w;
    }

    [Fact]
    public void Tower_Damages_An_Enemy_In_Range()
    {
        var w = World();
        w.AddCompletedBuilding(0, Turret(), 4, 4, "tower");
        int e = w.SpawnUnit(1, w.Map.CellCenter(6, 6), Fix.FromInt(1), hp: 50);  // ~2-3 cells away, player 1
        for (int i = 0; i < 20; i++) w.Step(System.Array.Empty<Command>());
        Assert.True(w.GetUnit(e)!.Hp < 50, "an enemy in range should be shot by the tower");
    }

    [Fact]
    public void Tower_Ignores_Allies()
    {
        var w = World();
        w.SetTeam(0, 7); w.SetTeam(1, 7);                 // tower owner 0 and unit owner 1 same team
        w.AddCompletedBuilding(0, Turret(), 4, 4, "tower");
        int ally = w.SpawnUnit(1, w.Map.CellCenter(6, 6), Fix.FromInt(1), hp: 50);
        for (int i = 0; i < 20; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(50, w.GetUnit(ally)!.Hp);
    }

    [Fact]
    public void Tower_Ignores_Enemy_Out_Of_Range()
    {
        var w = World();
        w.AddCompletedBuilding(0, Turret(), 2, 2, "tower");          // range 6
        int far = w.SpawnUnit(1, w.Map.CellCenter(20, 20), Fix.FromInt(1), hp: 50);  // far away
        for (int i = 0; i < 20; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(50, w.GetUnit(far)!.Hp);
    }

    [Fact]
    public void Incomplete_Tower_Does_Not_Fire()
    {
        var w = World();
        // place via the construction path so it starts incomplete (BuildTimeTicks high)
        var spec = new BuildingSpec(250, 2, 2, 150, BuildTimeTicks: 10_000, SightRange: 9,
            Weapon: new WeaponSpec(12, Fix.FromInt(6), 8));
        w.PlaceBuildingForTest(0, spec, 4, 4, "tower");   // helper that places INCOMPLETE; see note
        int e = w.SpawnUnit(1, w.Map.CellCenter(6, 6), Fix.FromInt(1), hp: 50);
        for (int i = 0; i < 20; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(50, w.GetUnit(e)!.Hp);
    }
}
```

> The `Incomplete_Tower_Does_Not_Fire` test needs an INCOMPLETE building. `PlaceBuilding` is `internal` (`InternalsVisibleTo SimCore.Tests` is set) — call `w.PlaceBuilding(...)` directly instead of a fictional helper (it places incomplete: `IsComplete=false`). Rewrite that line to `int id = w.PlaceBuilding(0, spec, 4, 4, "tower");` and confirm `PlaceBuilding` is reachable from tests (internal + InternalsVisibleTo). If not reachable, drop this test (the `!b.IsComplete` guard is still implemented + covered by reasoning).

- [ ] **Step 2: Run, expect failure** (towers don't fire yet).

- [ ] **Step 3: Implement**

In `SimWorld.Combat.cs`, refactor `AcquireTarget(Unit u, …)` to delegate to a position+owner core, then add `UpdateBuildingCombat`:

```csharp
    private int AcquireTarget(Unit u, Fix acquireRange) => AcquireTargetAt(u.Position, u.OwnerId, acquireRange);

    /// <summary>Nearest living enemy (units preferred, then buildings) within range of `from`,
    /// owned by a different TEAM than `ownerId`, visible to that owner. Deterministic (stable
    /// list iteration, strict-less-than tie-break). Shared by unit and building acquisition.</summary>
    private int AcquireTargetAt(FixVec from, int ownerId, Fix acquireRange)
    {
        var rangeSq = acquireRange * acquireRange;
        int best = 0;
        Fix bestDist = default;
        foreach (var e in _units)
        {
            if (SameTeam(e.OwnerId, ownerId) || e.Hp <= 0) continue;
            var (ecx, ecy) = Map.WorldToCell(e.Position);
            if (!IsVisibleTo(ownerId, ecx, ecy)) continue;
            var d = (e.Position - from).LengthSquared();
            if (d > rangeSq) continue;
            if (best == 0 || d < bestDist) { best = e.Id; bestDist = d; }
        }
        if (best != 0) return best;
        foreach (var b in _buildings)
        {
            if (SameTeam(b.OwnerId, ownerId) || b.Hp <= 0) continue;
            var bc = Map.WorldToCell(CenterOf(b));
            if (!IsVisibleTo(ownerId, bc.Item1, bc.Item2)) continue;
            var d = (CenterOf(b) - from).LengthSquared();
            if (d > rangeSq) continue;
            if (best == 0 || d < bestDist) { best = b.Id; bestDist = d; }
        }
        return best;
    }

    /// <summary>Static building defense: each complete, alive, WEAPONED building fires at the
    /// nearest enemy in range on cooldown. No-op for weaponless buildings (all but towers), so the
    /// towerless golden scenario is byte-identical. Deterministic (stable _buildings order).</summary>
    private void UpdateBuildingCombat()
    {
        foreach (var b in _buildings)
        {
            if (b.Weapon is not { } w || !b.IsComplete || b.Hp <= 0) continue;
            if (w.CooldownRemaining > 0) { w.CooldownRemaining--; continue; }
            int targetId = AcquireTargetAt(CenterOf(b), b.OwnerId, w.Range);
            if (targetId == 0) continue;
            if (!TryResolveTarget(targetId, out _, out var tu, out var tb)) continue;
            if (tu is not null) ApplyDamage(tu, w.Damage);
            else tb!.Hp -= w.Damage;
            w.CooldownRemaining = w.CooldownTicks;
        }
    }
```

> The refactor of `AcquireTarget`→`AcquireTargetAt` MUST be behavior-preserving for units (same iteration, same filters, just `from`/`ownerId` params) — the golden depends on it.

In `SimWorld.cs`, call it right after unit combat (line ~118):

```csharp
        UpdateCombat();
        UpdateBuildingCombat();
        MoveUnits();
```

- [ ] **Step 4: Run, expect pass.** Golden UNCHANGED: `UpdateBuildingCombat` is a no-op when no building has a weapon (golden scenario), and the `AcquireTarget` refactor is behavior-preserving.

- [ ] **Step 5: Commit** (`git status -s` first):

```bash
git add src/SimCore/Sim/SimWorld.Combat.cs src/SimCore/Sim/SimWorld.cs tests/SimCore.Tests/BuildingCombatTests.cs
git commit -m "feat(sim): building combat — weaponed buildings auto-fire (AcquireTargetAt refactor; no-op for weaponless)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

## Task W3-3: Hash the building weapon + Sentry Turret content (SimCore, TDD)

**Files:** Modify `src/SimCore/Sim/StateHasher.cs`, `src/SimCore/Sim/ReferenceSpecs.cs`, `src/SimCore/Sim/ReferenceFaction.cs`; Test `tests/SimCore.Tests/TowerDeterminismTests.cs`.

- [ ] **Step 1: Failing test**

Create `tests/SimCore.Tests/TowerDeterminismTests.cs`:

```csharp
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class TowerDeterminismTests
{
    [Fact]
    public void Reference_Has_A_Tower_Def_That_Requires_Depot()
    {
        var t = System.Linq.Enumerable.FirstOrDefault(ReferenceFaction.Def.BuildingList, b => b.Id == "tower");
        Assert.NotNull(t);
        Assert.NotNull(t!.Spec.Weapon);
    }

    [Fact]
    public void Hash_Reflects_Tower_Cooldown()
    {
        // Two worlds: one with a tower mid-cooldown, one freshly placed → different hashes
        // (weapon cooldown IS folded when present).
        var a = TowerWorld(); var b = TowerWorld();
        a.Step(System.Array.Empty<Command>());           // 'a' ticks the tower's cooldown / fires
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Tower_World_Replays_Identically()
    {
        var a = TowerWorld(); var b = TowerWorld();
        for (int i = 0; i < 60; i++) { a.Step(System.Array.Empty<Command>()); b.Step(System.Array.Empty<Command>()); }
        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    private static SimWorld TowerWorld()
    {
        var w = new SimWorld(new MapGrid(24, 24), seed: 5, playerCount: 4, faction: null);
        w.FogEnabled = false;
        w.AddCompletedBuilding(0, ReferenceSpecs.SentryTurret, 4, 4, "tower");
        w.SpawnUnit(1, w.Map.CellCenter(6, 6), SimCore.Math.Fix.FromInt(1), hp: 80);
        return w;
    }
}
```

- [ ] **Step 2: Run, expect failure** (`SentryTurret` missing; cooldown not hashed).

- [ ] **Step 3: Implement**

`ReferenceSpecs.cs` — add:

```csharp
    public static readonly BuildingSpec SentryTurret = new(
        MaxHp: 250, Width: 2, Height: 2, MineralCost: 150, BuildTimeTicks: 180,
        SupplyProvided: 0, IsDepot: false, CanTrain: false, SightRange: 9,
        Weapon: new WeaponSpec(Damage: 12, Range: Fix.FromInt(6), CooldownTicks: 8));
```

`ReferenceFaction.cs` — add the tower def, requiring the depot (match the `requires` shape used by `UnitDef` tank `new[] { "depot" }`):

```csharp
            new BuildingDef("tower", 1, new[] { "depot" }, ReferenceSpecs.SentryTurret),
```

`StateHasher.cs` — in the building loop (after the `CanTrain` fold, line ~120), fold the weapon **ONLY when present** (golden-safe — weaponless buildings fold nothing new):

```csharp
            if (b.Weapon is { } bw)
            {
                h = Mix(h, 1UL);
                h = Mix(h, (ulong)bw.Damage);
                h = Mix(h, (ulong)bw.Range.Raw);
                h = Mix(h, (ulong)bw.CooldownTicks);
                h = Mix(h, (ulong)bw.CooldownRemaining);
            }
```

Also extend the `Hash` doc-comment: "Building.Weapon (Damage/Range/CooldownTicks/CooldownRemaining) IS hashed — folded ONLY when present (weaponless buildings fold nothing, keeping pre-tower worlds byte-identical)."

- [ ] **Step 4: Run, expect pass.** Then **verify the golden is UNCHANGED** (`grep GoldenTrajectoryHash` still `1571756151672809223UL`, and the determinism replay test passes). The fold-only-when-present + no-op combat keep the towerless golden identical. If the golden test FAILS, the conditional fold leaked into weaponless buildings — fix it, do NOT re-pin.

- [ ] **Step 5: Regenerate pack + count asserts** (a second building added) — `UPDATE_PACKS=1 dotnet test …`, fix any building-count assertions, re-run green.

- [ ] **Step 6: Commit**:

```bash
git add src/SimCore/Sim/StateHasher.cs src/SimCore/Sim/ReferenceSpecs.cs src/SimCore/Sim/ReferenceFaction.cs tests/SimCore.Tests/TowerDeterminismTests.cs packs/reference/faction.json
git commit -m "feat(sim): Sentry Turret tower + hash building weapon (golden-safe: folded only when present)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

## Task W3-ART **[ART — Claude runs]**

Generate `tower` sprite via nano-banana (`--ref` depot-pro.png): an armored sci-fi sentry turret with a visible barrel/cannon, gunmetal + red faction trim, on a 2×2 base, magenta bg. Review + iterate; `keysprite.py … --out godot/assets/buildings/tower.png --size 256`; commit raw + processed. Loads via `BuildingView` by def-id.

---

# Task W-GATE: Full gate + playtest checklist + commit

- [ ] **Step 1:** `git status -s` (no phantom changes). Headless image-import for new assets (`Godot --headless --import`).
- [ ] **Step 2: Determinism + full suite (Release + Debug)**
```bash
dotnet test --configuration Release --nologo -v q
dotnet test --configuration Debug --nologo -v q
grep -n "GoldenTrajectoryHash =" tests/SimCore.Tests/DeterminismTests.cs   # MUST still be 1571756151672809223UL
git diff --stat master -- tests/SimCore.Tests/DeterminismTests.cs          # golden line unchanged
```
Both green; golden unchanged.
- [ ] **Step 3: Godot build** — `dotnet build godot/LlmRts.Godot.csproj --nologo` clean.
- [ ] **Step 4:** Append a playtest checklist (build a supply silo → cap rises; build a tower → it shoots an attacking enemy; ground is textured) to this plan; commit.

---

## Self-Review (author checklist — completed)

- **Spec coverage:** supply silo + AI pref (W1-1) + art (W1-ART); ground tile (W2-ART) + MapView tiling (W2-1); building weapons (W3-1), building-combat pass (W3-2), hash + tower content (W3-3) + art (W3-ART); gate (W-GATE). Every spec item maps to a task.
- **Determinism keystone:** W1/W2 don't touch hashed behavior; W3 building-combat is a no-op for weaponless buildings and the weapon is hashed only-when-present, so the golden `1571756151672809223UL` is unchanged — each SimCore task re-verifies it; explicit "do not re-pin, fix instead" guard in W3-2/W3-3.
- **Placeholder scan:** complete code in every code step. The "confirm signature" notes (BuildingDef `requires`/`None`, `PlaceBuilding` test-reachability, Godot tile API) are explicit verification steps with fallbacks, not deferred work.
- **Type consistency:** `BuildingSpec(… , WeaponSpec? Weapon = null)`, `Building.Weapon` (runtime `Weapon`), `WeaponSpec.Instantiate()`, `AcquireTargetAt(FixVec, int, Fix)`, `UpdateBuildingCombat`, `ApplyDamage`/`TryResolveTarget`/`CenterOf`, `SupplyDef`, `ReferenceSpecs.SupplySilo`/`SentryTurret`, `ReferenceFaction` "supply"/"tower" defs — used identically across tasks. Weapon hashed only-when-present (deliberate deviation from the always-marker convention, for golden-safety; documented in the hasher comment).
