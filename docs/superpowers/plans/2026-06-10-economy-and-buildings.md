# Economy & Buildings Implementation Plan (Plan 2b)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the RTS economy to the deterministic sim — player resources and supply, buildings with passability footprints, construction, production queues, mineral harvesting — plus the plan-2a carry-forward hardening (spec records with weapon cloning, passability hashing, named combat constants).

**Architecture:** Buildings and resource nodes share the unit id space and live in their own stable-ordered lists with the same removal discipline as units. Immutable spec records (`UnitSpec`/`WeaponSpec`/`BuildingSpec`) are the template layer that plan 3's faction packs will deserialize into; runtime state lives on `Unit`/`Building`. New systems slot into `Step`'s fixed order: Apply → Combat → Move → Harvest → Construction → Production → RemoveDead → RemoveDeadBuildings. `FlowField` learns to approach impassable targets (buildings/nodes) by seeding BFS from their passable neighbors. StateHasher v3 folds in players, buildings, nodes, harvest state, and packed map passability — closing the determinism blind spot before mid-run map mutation becomes routine.

**Tech Stack:** .NET 8, xUnit, existing SimCore. PATH note: if `dotnet` is not found, run `$env:Path += ';C:\Program Files\dotnet'`.

**Plan sequence update** (phase 1 is now 7 plans): 1 sim core ✅ → 2a combat & hardening ✅ → **2b economy & buildings (this plan)** → 2c fog of war → 3 faction pack system → 4 Godot presentation → 5 CPU opponent & match flow.

**Golden-hash protocol:** `Trajectory_Hash_Matches_Golden_Constant` folds all 500 per-tick hashes into `GoldenTrajectoryHash` (currently `6441639072325266705UL`). Tasks that change behavior or the hash function re-pin it in the same commit with an explanatory message. Tasks that must NOT move it say so. The two replay tests (`Same_Script_...`, `Replaying_...`) must never fail — a failure there is a real nondeterminism bug: debug honestly or report BLOCKED.

**Plan-2a carry-forwards honored here:** #1 hash passability (Task 9, plus tripwire respected throughout), #2 weapon aliasing (Task 1), #5 spawn-spec record (Task 1), #7 named leash constants (Task 1). #3 fog gates → plan 2c. #4 scale costs and #6 command validation remain deferred with rationale (current scale; commands still in-process). #8 mutable Unit → plan 4. #9 symmetric-exchange in scenario → Task 10 restores it.

---

### Task 1: Spec records, weapon cloning, combat constants

The template layer faction packs will target, plus two carry-forwards: weapon instances must never be shared between units, and the acquisition/leash literals become named constants with their invariant stated.

**Files:**
- Create: `src/SimCore/Sim/Specs.cs`
- Modify: `src/SimCore/Sim/Unit.cs` (add `SupplyCost`)
- Modify: `src/SimCore/Sim/SimWorld.cs` (spec-based `SpawnUnit` overload; old overload clones)
- Modify: `src/SimCore/Sim/SimWorld.Combat.cs` (named constants)
- Test: `tests/SimCore.Tests/SpecTests.cs`

- [ ] **Step 1: Write the failing tests** at `tests/SimCore.Tests/SpecTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class SpecTests
{
    private static SimWorld NewWorld() => new(new MapGrid(16, 16), seed: 1);

    [Fact]
    public void Spec_Spawn_Sets_All_Fields_And_Instantiates_Weapon()
    {
        var w = NewWorld();
        var spec = new UnitSpec(MaxHp: 40, Speed: Fix.FromFraction(1, 2), MineralCost: 50,
            SupplyCost: 2, BuildTimeTicks: 20, Weapon: new WeaponSpec(7, Fix.FromInt(3), 6));
        var id = w.SpawnUnit(0, FixVec.FromInts(2, 2), spec);
        var u = w.GetUnit(id)!;
        Assert.Equal(40, u.Hp);
        Assert.Equal(Fix.FromFraction(1, 2), u.SpeedPerTick);
        Assert.Equal(2, u.SupplyCost);
        Assert.Equal(7, u.Weapon!.Damage);
        Assert.Equal(Fix.FromInt(3), u.Weapon.Range);
        Assert.Equal(6, u.Weapon.CooldownTicks);
        Assert.Equal(0, u.Weapon.CooldownRemaining);
    }

    [Fact]
    public void Same_Spec_Produces_Distinct_Weapon_Instances()
    {
        var w = NewWorld();
        var spec = new UnitSpec(40, Fix.FromFraction(1, 2), 50, 2, 20, new WeaponSpec(7, Fix.FromInt(3), 6));
        var a = w.SpawnUnit(0, FixVec.FromInts(2, 2), spec);
        var b = w.SpawnUnit(0, FixVec.FromInts(3, 3), spec);
        Assert.NotSame(w.GetUnit(a)!.Weapon, w.GetUnit(b)!.Weapon);
    }

    [Fact]
    public void Legacy_Spawn_Clones_Weapon_Instance()
    {
        var w = NewWorld();
        var shared = new Weapon { Damage = 5, Range = Fix.FromInt(2), CooldownTicks = 4 };
        var a = w.SpawnUnit(0, FixVec.FromInts(2, 2), Fix.FromInt(1), 30, shared);
        var b = w.SpawnUnit(0, FixVec.FromInts(3, 3), Fix.FromInt(1), 30, shared);
        Assert.NotSame(w.GetUnit(a)!.Weapon, w.GetUnit(b)!.Weapon);
        Assert.NotSame(shared, w.GetUnit(a)!.Weapon);
        w.GetUnit(a)!.Weapon!.CooldownRemaining = 3;
        Assert.Equal(0, w.GetUnit(b)!.Weapon!.CooldownRemaining); // no shared cooldown state
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter SpecTests`
Expected: compilation FAILS (`UnitSpec` doesn't exist).

- [ ] **Step 3: Implement.**

Create `src/SimCore/Sim/Specs.cs`:

```csharp
using SimCore.Math;

namespace SimCore.Sim;

/// <summary>Immutable templates. Faction packs (plan 3) deserialize into these records;
/// runtime mutable state lives on Unit/Building/Weapon instances created FROM them.</summary>
public sealed record WeaponSpec(int Damage, Fix Range, int CooldownTicks)
{
    /// <summary>Fresh instance per unit — weapon state (cooldown) must never be shared.</summary>
    public Weapon Instantiate() => new() { Damage = Damage, Range = Range, CooldownTicks = CooldownTicks };
}

public sealed record HarvesterSpec(int CarryCapacity, int GatherTicks);

public sealed record UnitSpec(
    int MaxHp, Fix Speed, int MineralCost, int SupplyCost, int BuildTimeTicks,
    WeaponSpec? Weapon = null, HarvesterSpec? Harvester = null);

public sealed record BuildingSpec(
    int MaxHp, int Width, int Height, int MineralCost, int BuildTimeTicks,
    int SupplyProvided = 0, bool IsDepot = false, bool CanTrain = false);
```

In `src/SimCore/Sim/Unit.cs`, add:

```csharp
    public int SupplyCost { get; set; }
```

In `src/SimCore/Sim/SimWorld.cs`, replace `SpawnUnit` with both overloads:

```csharp
    public int SpawnUnit(int ownerId, FixVec pos, Fix speedPerTick, int hp, Weapon? weapon = null)
    {
        // Legacy overload: clones the weapon so callers can never alias cooldown state.
        var clone = weapon is null ? null : new Weapon
        {
            Damage = weapon.Damage, Range = weapon.Range,
            CooldownTicks = weapon.CooldownTicks, CooldownRemaining = weapon.CooldownRemaining
        };
        return Spawn(ownerId, pos, speedPerTick, hp, supplyCost: 0, clone, harvester: null);
    }

    public int SpawnUnit(int ownerId, FixVec pos, UnitSpec spec) =>
        Spawn(ownerId, pos, spec.Speed, spec.MaxHp, spec.SupplyCost, spec.Weapon?.Instantiate(), spec.Harvester);

    private int Spawn(int ownerId, FixVec pos, Fix speedPerTick, int hp, int supplyCost, Weapon? weapon, HarvesterSpec? harvester)
    {
        var u = new Unit
        {
            Id = _nextId++, OwnerId = ownerId, Position = pos, SpeedPerTick = speedPerTick,
            Hp = hp, SupplyCost = supplyCost, Weapon = weapon, Harvester = harvester
        };
        _units.Add(u);
        _byId[u.Id] = u;
        return u.Id;
    }
```

In `src/SimCore/Sim/Unit.cs`, also add (used by the `Spawn` helper now, harvest logic in Task 8):

```csharp
    public HarvesterSpec? Harvester { get; set; }
```

In `src/SimCore/Sim/SimWorld.Combat.cs`, add fields at the top of the class and replace the two literals:

```csharp
    // Hysteresis band: acquisition at Range + AcquireBonus, leash drop at Range + LeashBonus.
    // Invariant: LeashBonus > AcquireBonus, else attack-movers thrash at the boundary.
    private static readonly Fix AcquireBonus = Fix.FromInt(2);
    private static readonly Fix LeashBonus = Fix.FromInt(4);
```

Replace `u.Weapon.Range + Fix.FromInt(2)` (acquisition call) with `u.Weapon.Range + AcquireBonus`, and `u.Weapon.Range + Fix.FromInt(4)` (leash) with `u.Weapon.Range + LeashBonus`. Update the leash comment to reference the named constants.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: 75 passing (72 + 3). Golden trajectory UNCHANGED — cloning identical weapons and renaming constants is behavior-identical. If the golden test fails, you broke behavior; debug, never re-pin here.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/Specs.cs src/SimCore/Sim/Unit.cs src/SimCore/Sim/SimWorld.cs src/SimCore/Sim/SimWorld.Combat.cs tests/SimCore.Tests/SpecTests.cs
git commit -m "feat: spec records for units/weapons/buildings; clone weapons on spawn; named combat constants"
```

---

### Task 2: PlayerState and supply accounting

**Files:**
- Create: `src/SimCore/Sim/PlayerState.cs`
- Modify: `src/SimCore/Sim/SimWorld.cs` (players array, ctor param, supply on spawn/death)
- Test: `tests/SimCore.Tests/PlayerStateTests.cs`

- [ ] **Step 1: Write the failing tests** at `tests/SimCore.Tests/PlayerStateTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class PlayerStateTests
{
    [Fact]
    public void World_Has_Player_States()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, playerCount: 3);
        Assert.Equal(3, w.Players.Count);
        Assert.Equal(0, w.Players[0].Minerals);
    }

    [Fact]
    public void Spawn_And_Death_Track_Supply()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1);
        var spec = new UnitSpec(MaxHp: 10, Speed: Fix.FromInt(1), MineralCost: 0, SupplyCost: 2, BuildTimeTicks: 1);
        var id = w.SpawnUnit(0, FixVec.FromInts(2, 2), spec);
        Assert.Equal(2, w.Players[0].SupplyUsed);

        w.GetUnit(id)!.Hp = 0;
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.Players[0].SupplyUsed);
    }

    [Fact]
    public void Legacy_Spawn_Costs_No_Supply()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1);
        w.SpawnUnit(0, FixVec.FromInts(2, 2), Fix.FromInt(1), 10);
        Assert.Equal(0, w.Players[0].SupplyUsed);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter PlayerStateTests`
Expected: compilation FAILS (`Players`, `playerCount` don't exist).

- [ ] **Step 3: Implement.**

Create `src/SimCore/Sim/PlayerState.cs`:

```csharp
namespace SimCore.Sim;

/// <summary>Per-player economy state. All fields are mutable sim state — hashed (Task 9).</summary>
public sealed class PlayerState
{
    public int Minerals { get; set; }
    public int SupplyUsed { get; set; }
    public int SupplyCap { get; set; }
}
```

In `src/SimCore/Sim/SimWorld.cs`:
- Change the constructor and add the players array:

```csharp
    private readonly PlayerState[] _players;
    public System.Collections.Generic.IReadOnlyList<PlayerState> Players => _players;

    public SimWorld(MapGrid map, ulong seed, int playerCount = 2)
    {
        Map = map;
        Rng = new DeterministicRandom(seed);
        _players = new PlayerState[playerCount];
        for (int i = 0; i < playerCount; i++) _players[i] = new PlayerState();
    }
```

- In the private `Spawn` helper, after `_byId[u.Id] = u;` add:

```csharp
        _players[ownerId].SupplyUsed += supplyCost;
```

- In `RemoveDead`, before `_byId.Remove(...)` add:

```csharp
            _players[_units[i].OwnerId].SupplyUsed -= _units[i].SupplyCost;
```

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: 78 passing. Golden trajectory UNCHANGED (hasher doesn't read players yet; supply fields are 0 throughout the scenario).

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/PlayerState.cs src/SimCore/Sim/SimWorld.cs tests/SimCore.Tests/PlayerStateTests.cs
git commit -m "feat: per-player minerals and supply accounting"
```

---

### Task 3: Building placement and destruction

Buildings share the unit id space, occupy a passability footprint (exercising the version-guarded cache for real), and restore passability on death. `BuildCommand` validates worker proximity, funds, and footprint.

**Files:**
- Create: `src/SimCore/Sim/Building.cs`
- Create: `src/SimCore/Sim/SimWorld.Buildings.cs`
- Modify: `src/SimCore/Sim/Commands.cs` (add `BuildCommand`)
- Modify: `src/SimCore/Sim/SimWorld.cs` (lists, `GetBuilding`, `Apply` case, `Step` calls `RemoveDeadBuildings`)
- Test: `tests/SimCore.Tests/BuildingTests.cs`

- [ ] **Step 1: Write the failing tests** at `tests/SimCore.Tests/BuildingTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class BuildingTests
{
    public static readonly BuildingSpec Depot =
        new(MaxHp: 100, Width: 2, Height: 2, MineralCost: 100, BuildTimeTicks: 10, SupplyProvided: 8, IsDepot: true);

    private static (SimWorld w, int worker) WorldWithWorker()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        w.Players[0].Minerals = 500;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        return (w, worker);
    }

    [Fact]
    public void Build_Deducts_Minerals_And_Blocks_Footprint()
    {
        var (w, worker) = WorldWithWorker();
        w.Step(new Command[] { new BuildCommand(0, worker, Depot, 6, 5) });
        Assert.Equal(400, w.Players[0].Minerals);
        Assert.Single(w.Buildings);
        Assert.False(w.Map.IsPassable(6, 5));
        Assert.False(w.Map.IsPassable(7, 6));
        Assert.True(w.Map.IsPassable(8, 5)); // outside footprint
    }

    [Fact]
    public void Build_Rejected_When_Too_Poor()
    {
        var (w, worker) = WorldWithWorker();
        w.Players[0].Minerals = 50;
        w.Step(new Command[] { new BuildCommand(0, worker, Depot, 6, 5) });
        Assert.Empty(w.Buildings);
        Assert.Equal(50, w.Players[0].Minerals);
    }

    [Fact]
    public void Build_Rejected_When_Worker_Too_Far()
    {
        var (w, worker) = WorldWithWorker();
        w.Step(new Command[] { new BuildCommand(0, worker, Depot, 15, 15) });
        Assert.Empty(w.Buildings);
        Assert.Equal(500, w.Players[0].Minerals);
    }

    [Fact]
    public void Build_Rejected_On_Blocked_Or_Occupied_Footprint()
    {
        var (w, worker) = WorldWithWorker();
        w.Map.SetPassable(7, 5, false); // pre-blocked cell inside footprint
        w.Step(new Command[] { new BuildCommand(0, worker, Depot, 6, 5) });
        Assert.Empty(w.Buildings);

        // unit standing in footprint also blocks (worker itself at (5,5) is outside 6..7 x 5..6)
        w.Map.SetPassable(7, 5, true);
        w.SpawnUnit(1, w.Map.CellCenter(6, 6), Fix.FromInt(1), 10);
        w.Step(new Command[] { new BuildCommand(0, worker, Depot, 6, 5) });
        Assert.Empty(w.Buildings);
    }

    [Fact]
    public void Destroyed_Building_Restores_Passability()
    {
        var (w, worker) = WorldWithWorker();
        w.Step(new Command[] { new BuildCommand(0, worker, Depot, 6, 5) });
        var b = w.Buildings[0];
        b.Hp = 0;
        w.Step(System.Array.Empty<Command>());
        Assert.Empty(w.Buildings);
        Assert.Null(w.GetBuilding(b.Id));
        Assert.True(w.Map.IsPassable(6, 5));
        Assert.True(w.Map.IsPassable(7, 6));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter BuildingTests`
Expected: compilation FAILS (`BuildCommand`, `Buildings` don't exist).

- [ ] **Step 3: Implement.**

Create `src/SimCore/Sim/Building.cs`:

```csharp
namespace SimCore.Sim;

/// <summary>Mutable building state. Shares the entity id space with units.</summary>
public sealed class Building
{
    public int Id { get; init; }
    public int OwnerId { get; init; }
    public int CellX { get; init; }
    public int CellY { get; init; }
    public BuildingSpec Spec { get; init; } = null!;
    public int Hp { get; set; }
    public bool IsComplete { get; set; }
    public int BuildProgress { get; set; }
}
```

In `src/SimCore/Sim/Commands.cs`, add:

```csharp
public sealed record BuildCommand(int PlayerId, int WorkerUnitId, BuildingSpec Spec, int CellX, int CellY) : Command(PlayerId);
```

Create `src/SimCore/Sim/SimWorld.Buildings.cs`:

```csharp
using SimCore.Math;

namespace SimCore.Sim;

public sealed partial class SimWorld
{
    private readonly System.Collections.Generic.List<Building> _buildings = new(); // stable order — determinism
    private readonly System.Collections.Generic.Dictionary<int, Building> _buildingsById = new(); // lookup only

    public System.Collections.Generic.IReadOnlyList<Building> Buildings => _buildings;
    public Building? GetBuilding(int id) => _buildingsById.TryGetValue(id, out var b) ? b : null;

    internal static FixVec FootprintCenter(int cellX, int cellY, int width, int height) =>
        new(Fix.FromInt(cellX) + Fix.FromFraction(width, 2), Fix.FromInt(cellY) + Fix.FromFraction(height, 2));

    internal FixVec CenterOf(Building b) => FootprintCenter(b.CellX, b.CellY, b.Spec.Width, b.Spec.Height);

    private bool FootprintPlaceable(int cellX, int cellY, int width, int height)
    {
        for (int y = cellY; y < cellY + height; y++)
            for (int x = cellX; x < cellX + width; x++)
                if (!Map.IsPassable(x, y)) return false;
        foreach (var u in _units)
        {
            var (ux, uy) = Map.WorldToCell(u.Position);
            if (ux >= cellX && ux < cellX + width && uy >= cellY && uy < cellY + height) return false;
        }
        return true;
    }

    internal int PlaceBuilding(int ownerId, BuildingSpec spec, int cellX, int cellY)
    {
        var b = new Building { Id = _nextId++, OwnerId = ownerId, CellX = cellX, CellY = cellY, Spec = spec, Hp = spec.MaxHp };
        _buildings.Add(b);
        _buildingsById[b.Id] = b;
        for (int y = cellY; y < cellY + spec.Height; y++)
            for (int x = cellX; x < cellX + spec.Width; x++)
                Map.SetPassable(x, y, false);
        return b.Id;
    }

    /// <summary>Reverse-index removal preserves order; restores footprint passability.</summary>
    private void RemoveDeadBuildings()
    {
        for (int i = _buildings.Count - 1; i >= 0; i--)
        {
            var b = _buildings[i];
            if (b.Hp > 0) continue;
            if (b.IsComplete) _players[b.OwnerId].SupplyCap -= b.Spec.SupplyProvided;
            for (int y = b.CellY; y < b.CellY + b.Spec.Height; y++)
                for (int x = b.CellX; x < b.CellX + b.Spec.Width; x++)
                    Map.SetPassable(x, y, true);
            _buildingsById.Remove(b.Id);
            _buildings.RemoveAt(i);
        }
    }
}
```

In `src/SimCore/Sim/SimWorld.cs`:
- In `Step`, add `RemoveDeadBuildings();` immediately after `RemoveDead();`.
- In `Apply`, add the case:

```csharp
            case BuildCommand bc:
                var builder = GetUnit(bc.WorkerUnitId);
                if (builder is null || builder.OwnerId != bc.PlayerId) break;
                if (_players[bc.PlayerId].Minerals < bc.Spec.MineralCost) break;
                var siteCenter = FootprintCenter(bc.CellX, bc.CellY, bc.Spec.Width, bc.Spec.Height);
                if ((builder.Position - siteCenter).LengthSquared() > Fix.FromInt(16)) break; // worker within 4 of site center
                if (!FootprintPlaceable(bc.CellX, bc.CellY, bc.Spec.Width, bc.Spec.Height)) break;
                _players[bc.PlayerId].Minerals -= bc.Spec.MineralCost;
                PlaceBuilding(bc.PlayerId, bc.Spec, bc.CellX, bc.CellY);
                break;
```

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: 83 passing. Golden trajectory UNCHANGED (scenario issues no BuildCommands; hasher unchanged).

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/Building.cs src/SimCore/Sim/SimWorld.Buildings.cs src/SimCore/Sim/Commands.cs src/SimCore/Sim/SimWorld.cs tests/SimCore.Tests/BuildingTests.cs
git commit -m "feat: buildings with passability footprints, build command, destruction"
```

---

### Task 4: Construction progress and supply grant

**Files:**
- Modify: `src/SimCore/Sim/SimWorld.Buildings.cs` (add `UpdateConstruction`)
- Modify: `src/SimCore/Sim/SimWorld.cs` (`Step` order)
- Test: `tests/SimCore.Tests/ConstructionTests.cs`

- [ ] **Step 1: Write the failing tests** at `tests/SimCore.Tests/ConstructionTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class ConstructionTests
{
    private static (SimWorld w, int buildingId) PlacedDepot()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        w.Players[0].Minerals = 500;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(0, worker, BuildingTests.Depot, 6, 5) });
        return (w, w.Buildings[0].Id);
    }

    [Fact]
    public void Building_Completes_After_BuildTime_And_Grants_Supply()
    {
        var (w, id) = PlacedDepot();
        Assert.False(w.GetBuilding(id)!.IsComplete);
        Assert.Equal(0, w.Players[0].SupplyCap);

        for (int i = 0; i < BuildingTests.Depot.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        Assert.True(w.GetBuilding(id)!.IsComplete);
        Assert.Equal(8, w.Players[0].SupplyCap);

        // supply granted exactly once
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(8, w.Players[0].SupplyCap);
    }

    [Fact]
    public void Destroyed_Incomplete_Building_Grants_No_Supply_And_Refunds_Nothing()
    {
        var (w, id) = PlacedDepot();
        w.GetBuilding(id)!.Hp = 0;
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.Players[0].SupplyCap);
        Assert.Equal(400, w.Players[0].Minerals);
    }

    [Fact]
    public void Destroyed_Complete_Building_Revokes_Supply()
    {
        var (w, id) = PlacedDepot();
        for (int i = 0; i < BuildingTests.Depot.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(8, w.Players[0].SupplyCap);
        w.GetBuilding(id)!.Hp = 0;
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.Players[0].SupplyCap);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter ConstructionTests`
Expected: `Building_Completes_After_BuildTime_And_Grants_Supply` and `Destroyed_Complete_Building_Revokes_Supply` FAIL (`IsComplete` never becomes true); the incomplete-destruction test may already pass.

- [ ] **Step 3: Implement.** In `src/SimCore/Sim/SimWorld.Buildings.cs`, add:

```csharp
    private void UpdateConstruction()
    {
        foreach (var b in _buildings)
        {
            if (b.IsComplete) continue;
            b.BuildProgress++;
            if (b.BuildProgress >= b.Spec.BuildTimeTicks)
            {
                b.IsComplete = true;
                _players[b.OwnerId].SupplyCap += b.Spec.SupplyProvided;
            }
        }
    }
```

In `src/SimCore/Sim/SimWorld.cs` `Step`, insert `UpdateConstruction();` between `MoveUnits();` and `RemoveDead();`.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: 86 passing. Golden trajectory UNCHANGED (no buildings in scenario).

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/SimWorld.Buildings.cs src/SimCore/Sim/SimWorld.cs tests/SimCore.Tests/ConstructionTests.cs
git commit -m "feat: construction progress; completed buildings grant supply"
```

---

### Task 5: Production queues and TrainCommand

**Files:**
- Modify: `src/SimCore/Sim/Building.cs` (queue + `TrainingItem`)
- Modify: `src/SimCore/Sim/Commands.cs` (add `TrainCommand`)
- Modify: `src/SimCore/Sim/SimWorld.Buildings.cs` (`UpdateProduction`, `FindSpawnCell`)
- Modify: `src/SimCore/Sim/SimWorld.cs` (`Apply` case, `Step` order)
- Test: `tests/SimCore.Tests/ProductionTests.cs`

- [ ] **Step 1: Write the failing tests** at `tests/SimCore.Tests/ProductionTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class ProductionTests
{
    private static readonly BuildingSpec Barracks =
        new(MaxHp: 150, Width: 2, Height: 2, MineralCost: 150, BuildTimeTicks: 5, CanTrain: true);

    private static readonly UnitSpec Marine =
        new(MaxHp: 40, Speed: Fix.FromFraction(1, 2), MineralCost: 50, SupplyCost: 1, BuildTimeTicks: 8,
            Weapon: new WeaponSpec(6, Fix.FromInt(2), 5));

    private static (SimWorld w, int barracksId) ReadyWorld()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        w.Players[0].Minerals = 1000;
        w.Players[0].SupplyCap = 10;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(0, worker, Barracks, 7, 5) });
        for (int i = 0; i < Barracks.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        return (w, w.Buildings[0].Id);
    }

    [Fact]
    public void Train_Spawns_Unit_With_Spec_After_BuildTime()
    {
        var (w, bid) = ReadyWorld();
        var unitsBefore = w.Units.Count;
        var mineralsBefore = w.Players[0].Minerals;

        w.Step(new Command[] { new TrainCommand(0, bid, Marine) });
        Assert.Equal(mineralsBefore - 50, w.Players[0].Minerals);
        Assert.Equal(1, w.Players[0].SupplyUsed);

        for (int i = 0; i < Marine.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(unitsBefore + 1, w.Units.Count);
        var trained = w.Units[^1];
        Assert.Equal(40, trained.Hp);
        Assert.Equal(6, trained.Weapon!.Damage);
        // spawned adjacent to footprint
        var (cx, cy) = w.Map.WorldToCell(trained.Position);
        Assert.InRange(cx, 6, 9);
        Assert.InRange(cy, 4, 7);
        Assert.True(w.Map.IsPassable(cx, cy));
    }

    [Fact]
    public void Train_Rejected_On_Insufficient_Supply()
    {
        var (w, bid) = ReadyWorld();
        w.Players[0].SupplyCap = w.Players[0].SupplyUsed; // no headroom
        var minerals = w.Players[0].Minerals;
        w.Step(new Command[] { new TrainCommand(0, bid, Marine) });
        Assert.Equal(minerals, w.Players[0].Minerals);
        Assert.Empty(w.GetBuilding(bid)!.Queue);
    }

    [Fact]
    public void Queue_Caps_At_Five()
    {
        var (w, bid) = ReadyWorld();
        var cmds = new Command[7];
        for (int i = 0; i < 7; i++) cmds[i] = new TrainCommand(0, bid, Marine);
        w.Step(cmds);
        Assert.Equal(5, w.GetBuilding(bid)!.Queue.Count);
        Assert.Equal(1000 - 5 * 50, w.Players[0].Minerals); // only 5 paid for
    }

    [Fact]
    public void Incomplete_Or_NonTrainer_Building_Rejects_Train()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        w.Players[0].Minerals = 1000;
        w.Players[0].SupplyCap = 10;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(0, worker, Barracks, 7, 5) });
        var bid = w.Buildings[0].Id; // still under construction
        w.Step(new Command[] { new TrainCommand(0, bid, Marine) });
        Assert.Empty(w.GetBuilding(bid)!.Queue);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter ProductionTests`
Expected: compilation FAILS (`TrainCommand`, `Queue` don't exist).

- [ ] **Step 3: Implement.**

In `src/SimCore/Sim/Building.cs`, add inside `Building`:

```csharp
    public System.Collections.Generic.List<TrainingItem> Queue { get; } = new(); // index 0 is in production
```

and below the class:

```csharp
public sealed class TrainingItem
{
    public UnitSpec Spec { get; init; } = null!;
    public int RemainingTicks { get; set; }
}
```

In `src/SimCore/Sim/Commands.cs`, add:

```csharp
public sealed record TrainCommand(int PlayerId, int BuildingId, UnitSpec Spec) : Command(PlayerId);
```

In `src/SimCore/Sim/SimWorld.cs` `Apply`, add the case:

```csharp
            case TrainCommand tc:
                var trainer = GetBuilding(tc.BuildingId);
                if (trainer is null || trainer.OwnerId != tc.PlayerId || !trainer.IsComplete || !trainer.Spec.CanTrain) break;
                if (trainer.Queue.Count >= 5) break;
                var ps = _players[tc.PlayerId];
                if (ps.Minerals < tc.Spec.MineralCost) break;
                if (ps.SupplyUsed + tc.Spec.SupplyCost > ps.SupplyCap) break;
                ps.Minerals -= tc.Spec.MineralCost;
                ps.SupplyUsed += tc.Spec.SupplyCost; // reserve supply at enqueue
                trainer.Queue.Add(new TrainingItem { Spec = tc.Spec, RemainingTicks = tc.Spec.BuildTimeTicks });
                break;
```

In `src/SimCore/Sim/SimWorld.Buildings.cs`, add:

```csharp
    private void UpdateProduction()
    {
        foreach (var b in _buildings)
        {
            if (!b.IsComplete || b.Queue.Count == 0) continue;
            var item = b.Queue[0];
            item.RemainingTicks--;
            if (item.RemainingTicks > 0) continue;
            var cell = FindSpawnCell(b);
            if (cell is null) continue; // perimeter blocked — retry next tick
            b.Queue.RemoveAt(0);
            // supply was reserved at enqueue; SpawnUnit adds it again, so subtract the duplicate
            SpawnUnit(b.OwnerId, Map.CellCenter(cell.Value.x, cell.Value.y), item.Spec);
            _players[b.OwnerId].SupplyUsed -= item.Spec.SupplyCost;
        }
    }

    /// <summary>First passable perimeter cell in fixed scan order — deterministic.</summary>
    private (int x, int y)? FindSpawnCell(Building b)
    {
        for (int y = b.CellY - 1; y <= b.CellY + b.Spec.Height; y++)
            for (int x = b.CellX - 1; x <= b.CellX + b.Spec.Width; x++)
            {
                var onPerimeter = x == b.CellX - 1 || x == b.CellX + b.Spec.Width
                               || y == b.CellY - 1 || y == b.CellY + b.Spec.Height;
                if (onPerimeter && Map.IsPassable(x, y)) return (x, y);
            }
        return null;
    }
```

In `src/SimCore/Sim/SimWorld.cs` `Step`, insert `UpdateProduction();` immediately after `UpdateConstruction();`.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: 90 passing. Golden trajectory UNCHANGED.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/Building.cs src/SimCore/Sim/Commands.cs src/SimCore/Sim/SimWorld.Buildings.cs src/SimCore/Sim/SimWorld.cs tests/SimCore.Tests/ProductionTests.cs
git commit -m "feat: production queues with supply reservation and adjacent spawn"
```

---

### Task 6: Attackable buildings

`AttackCommand`/acquisition resolve target ids against units first, then buildings (shared id space). Buildings don't move, so combat range uses the footprint center (documented approximation for v1).

**Files:**
- Modify: `src/SimCore/Sim/SimWorld.cs` (`Apply` AttackCommand case accepts building targets)
- Modify: `src/SimCore/Sim/SimWorld.Combat.cs` (target resolution, building damage, acquisition includes buildings)
- Test: `tests/SimCore.Tests/BuildingCombatTests.cs`

- [ ] **Step 1: Write the failing tests** at `tests/SimCore.Tests/BuildingCombatTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class BuildingCombatTests
{
    private static readonly BuildingSpec Hut =
        new(MaxHp: 30, Width: 2, Height: 2, MineralCost: 50, BuildTimeTicks: 1);

    private static Weapon TestWeapon() =>
        new() { Damage = 10, Range = Fix.FromInt(3), CooldownTicks = 2 };

    private static (SimWorld w, int hutId) WorldWithEnemyHut()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        w.Players[1].Minerals = 100;
        var enemyWorker = w.SpawnUnit(1, w.Map.CellCenter(10, 10), Fix.FromFraction(1, 2), 30);
        w.Step(new Command[] { new BuildCommand(1, enemyWorker, Hut, 10, 11) });
        w.Step(System.Array.Empty<Command>()); // completes (BuildTimeTicks 1)
        w.GetUnit(enemyWorker)!.Hp = 0;
        w.Step(System.Array.Empty<Command>()); // remove worker
        return (w, w.Buildings[0].Id);
    }

    [Fact]
    public void Attack_Order_Destroys_Building_And_Restores_Passability()
    {
        var (w, hutId) = WorldWithEnemyHut();
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(8, 11), Fix.FromFraction(1, 2), 50, TestWeapon());
        w.Step(new Command[] { new AttackCommand(0, new[] { attacker }, hutId) });
        for (int i = 0; i < 60 && w.GetBuilding(hutId) is not null; i++)
            w.Step(System.Array.Empty<Command>());
        Assert.Null(w.GetBuilding(hutId));
        Assert.True(w.Map.IsPassable(10, 11));
        Assert.Equal(0, w.GetUnit(attacker)!.AttackTargetId);
    }

    [Fact]
    public void AttackMove_Acquires_Building_When_No_Units_Near()
    {
        var (w, hutId) = WorldWithEnemyHut();
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(9, 11), Fix.FromFraction(1, 2), 50, TestWeapon());
        w.Step(new Command[] { new AttackMoveCommand(0, new[] { attacker }, w.Map.CellCenter(15, 11)) });
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(hutId, w.GetUnit(attacker)!.AttackTargetId);
    }

    [Fact]
    public void Acquisition_Prefers_Units_Over_Buildings()
    {
        var (w, hutId) = WorldWithEnemyHut();
        var enemyUnit = w.SpawnUnit(1, w.Map.CellCenter(9, 10), Fix.FromFraction(1, 2), 100);
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(9, 11), Fix.FromFraction(1, 2), 50, TestWeapon());
        w.Step(new Command[] { new AttackMoveCommand(0, new[] { attacker }, w.Map.CellCenter(15, 11)) });
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(enemyUnit, w.GetUnit(attacker)!.AttackTargetId);
    }

    [Fact]
    public void Cannot_Attack_Own_Building()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        w.Players[0].Minerals = 100;
        var worker = w.SpawnUnit(0, w.Map.CellCenter(10, 10), Fix.FromFraction(1, 2), 30, TestWeapon());
        w.Step(new Command[] { new BuildCommand(0, worker, Hut, 10, 11) });
        var bid = w.Buildings[0].Id;
        w.Step(new Command[] { new AttackCommand(0, new[] { worker }, bid) });
        Assert.Equal(0, w.GetUnit(worker)!.AttackTargetId);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter BuildingCombatTests`
Expected: compilation SUCCEEDS but tests FAIL (AttackCommand rejects building ids — `GetUnit(targetId)` is null).

- [ ] **Step 3: Implement.**

In `src/SimCore/Sim/SimWorld.cs`, replace the `AttackCommand` case's target validation:

```csharp
            case AttackCommand atk:
                foreach (var id in atk.UnitIds)
                {
                    var u = GetUnit(id);
                    if (u is null || u.OwnerId != atk.PlayerId || u.Weapon is null) continue;
                    var tu = GetUnit(atk.TargetId);
                    var tb = tu is null ? GetBuilding(atk.TargetId) : null;
                    if (tu is null && tb is null) continue;
                    if ((tu?.OwnerId ?? tb!.OwnerId) == atk.PlayerId) continue; // no friendly fire
                    u.AttackTargetId = atk.TargetId;
                    u.IsAttackMoving = false; // explicit attack order replaces any attack-move
                    u.HasMoveOrder = false;
                    u.Path = null;
                }
                break;
```

In `src/SimCore/Sim/SimWorld.Combat.cs`:

Replace the stale-target sweep with a shared resolver. Add this helper to the partial class:

```csharp
    /// <summary>Resolves a target id to (position, alive) for unit or building targets.
    /// Building "position" is the footprint center — a v1 approximation for range checks.</summary>
    private bool TryResolveTarget(int targetId, out FixVec position, out Unit? unit, out Building? building)
    {
        unit = GetUnit(targetId);
        if (unit is not null && unit.Hp > 0) { position = unit.Position; building = null; return true; }
        building = GetBuilding(targetId);
        if (building is not null && building.Hp > 0) { position = CenterOf(building); unit = null; return true; }
        position = default;
        unit = null;
        building = null;
        return false;
    }
```

Update the stale-clear sweep at the top of pass 2 to use it:

```csharp
            // Clear dead/missing targets up front so re-acquisition happens this tick, not next.
            if (u.AttackTargetId != 0 && !TryResolveTarget(u.AttackTargetId, out _, out _, out _))
                u.AttackTargetId = 0;
```

Update the fight-or-chase body: replace the `var target = GetUnit(u.AttackTargetId); if (target is null || target.Hp <= 0) ...` lines and all later uses of `target.Position`/`target.Hp` with:

```csharp
            if (u.Weapon is null || u.AttackTargetId == 0) continue;
            if (!TryResolveTarget(u.AttackTargetId, out var targetPos, out var targetUnit, out var targetBuilding))
            {
                u.AttackTargetId = 0;
                continue;
            }

            var delta = targetPos - u.Position;
            // Leash: attack-movers abandon targets that kite beyond acquisition range + 2
            // (explicit AttackCommand orders have no leash — the player asked for that chase).
            if (u.IsAttackMoving)
            {
                var leash = u.Weapon.Range + LeashBonus;
                if (delta.LengthSquared() > leash * leash) { u.AttackTargetId = 0; continue; }
            }
            if (delta.LengthSquared() <= u.Weapon.Range * u.Weapon.Range)
            {
                u.HasMoveOrder = false;
                u.Path = null;
                if (u.Weapon.CooldownRemaining == 0)
                {
                    if (targetUnit is not null) targetUnit.Hp -= u.Weapon.Damage;
                    else targetBuilding!.Hp -= u.Weapon.Damage;
                    u.Weapon.CooldownRemaining = u.Weapon.CooldownTicks;
                }
            }
            else
            {
                // chase: follow a (cached) field toward the target's current cell
                var (tx, ty) = Map.WorldToCell(targetPos);
                u.HasMoveOrder = true;
                u.MoveTarget = targetPos;
                u.Path = GetField(tx, ty);
                u.PathVersion = Map.Version;
            }
```

(Note: chasing a building targets its impassable footprint cell — the flow field returns an empty field until Task 7 adds approach semantics; with weapon range ≥ footprint reach this rarely matters for stationary attacks, and Task 7 fixes it fully. The attack tests in this task place attackers within or near range.)

Extend `AcquireTarget` to consider buildings after units (units win ties by running first):

```csharp
    private int AcquireTarget(Unit u, Fix acquireRange)
    {
        var rangeSq = acquireRange * acquireRange;
        int best = 0;
        Fix bestDist = default;
        foreach (var e in _units)
        {
            if (e.OwnerId == u.OwnerId || e.Hp <= 0) continue;
            var d = (e.Position - u.Position).LengthSquared();
            if (d > rangeSq) continue;
            if (best == 0 || d < bestDist) { best = e.Id; bestDist = d; }
        }
        if (best != 0) return best; // units strictly preferred over buildings
        foreach (var b in _buildings)
        {
            if (b.OwnerId == u.OwnerId || b.Hp <= 0) continue;
            var d = (CenterOf(b) - u.Position).LengthSquared();
            if (d > rangeSq) continue;
            if (best == 0 || d < bestDist) { best = b.Id; bestDist = d; }
        }
        return best;
    }
```

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: 94 passing. Golden trajectory UNCHANGED (no buildings in scenario; combat refactor must be behavior-identical for unit targets — if golden fails, the refactor changed unit-combat behavior: debug, never re-pin here).

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/SimWorld.cs src/SimCore/Sim/SimWorld.Combat.cs tests/SimCore.Tests/BuildingCombatTests.cs
git commit -m "feat: buildings are attackable; acquisition prefers units over buildings"
```

---

### Task 7: FlowField approach semantics for impassable targets

Ordering a unit to an impassable cell (building, resource node) currently yields an empty field (order cancels). New semantics: BFS seeds from the target's passable neighbors, so units walk up adjacent and stop. Reachability give-up semantics are preserved: an impassable target with no reachable neighbors still produces an empty field.

**Files:**
- Modify: `src/SimCore/Sim/FlowField.cs` (`Compute` seeding)
- Test: `tests/SimCore.Tests/FlowFieldApproachTests.cs`

- [ ] **Step 1: Write the failing tests** at `tests/SimCore.Tests/FlowFieldApproachTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class FlowFieldApproachTests
{
    [Fact]
    public void Field_Toward_Impassable_Cell_Leads_To_Its_Neighbors()
    {
        var g = new MapGrid(10, 10);
        g.SetPassable(5, 5, false); // a "building" cell
        var f = FlowField.Compute(g, 5, 5);
        var (dx, dy) = f.DirectionAt(2, 5);
        Assert.Equal(1, dx); // flows east toward the neighbors of (5,5)
        Assert.Equal((0, 0), f.DirectionAt(4, 5)); // adjacent cell is a seed — terminal
    }

    [Fact]
    public void Unit_Ordered_Onto_Building_Cell_Stops_Adjacent()
    {
        var map = new MapGrid(12, 12);
        map.SetPassable(8, 5, false);
        var w = new SimWorld(map, seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, w.Map.CellCenter(8, 5)) });
        for (int i = 0; i < 200 && w.GetUnit(id)!.HasMoveOrder; i++) w.Step(System.Array.Empty<Command>());

        var (cx, cy) = w.Map.WorldToCell(w.GetUnit(id)!.Position);
        Assert.True(System.Math.Abs(cx - 8) <= 1 && System.Math.Abs(cy - 5) <= 1, $"unit at ({cx},{cy}) not adjacent to (8,5)");
        Assert.True(map.IsPassable(cx, cy));
        Assert.False(w.GetUnit(id)!.HasMoveOrder);
    }

    [Fact]
    public void Enclosed_Impassable_Target_Still_Unreachable()
    {
        var g = new MapGrid(10, 10);
        // 3x3 solid block: target at center, all neighbors also impassable
        for (int y = 4; y <= 6; y++)
            for (int x = 4; x <= 6; x++)
                g.SetPassable(x, y, false);
        var f = FlowField.Compute(g, 5, 5);
        Assert.Equal((0, 0), f.DirectionAt(0, 0)); // empty field — give-up semantics preserved
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FlowFieldApproachTests`
Expected: first two FAIL (impassable target → empty field today); third passes already.

- [ ] **Step 3: Implement.** In `src/SimCore/Sim/FlowField.cs`, replace the start of `Compute` (the target check and BFS seeding) with multi-seed logic:

```csharp
    public static FlowField Compute(MapGrid map, int targetX, int targetY)
    {
        var f = new FlowField(map.Width, map.Height, targetX, targetY);
        var queue = new Queue<(int x, int y)>();

        if (map.IsPassable(targetX, targetY))
        {
            f._cost[targetY * map.Width + targetX] = 0;
            queue.Enqueue((targetX, targetY));
        }
        else
        {
            // Approach semantics: impassable target (building/resource) — seed its passable
            // neighbors at cost 0 so units walk up adjacent and stop there. Fixed neighbor
            // order; no corner-cut constraint for seeds (they are start points, not steps).
            foreach (var (dx, dy) in Neighbors)
            {
                int nx = targetX + dx, ny = targetY + dy;
                if (!map.IsPassable(nx, ny)) continue;
                f._cost[ny * map.Width + nx] = 0;
                queue.Enqueue((nx, ny));
            }
        }
        if (queue.Count == 0) return f; // fully enclosed — empty field, give-up semantics

        while (queue.Count > 0)
        {
            // ... existing BFS loop body unchanged ...
        }

        // ... existing direction-field pass unchanged ...
        return f;
    }
```

(Keep the existing BFS loop body and direction-field pass exactly as they are — only the seeding section changes.)

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: 97 passing. Golden trajectory UNCHANGED — every move/attack-move target in the determinism scenario is a passable cell, so the new branch never executes there. If golden fails, the seeding refactor altered single-seed behavior; debug, never re-pin here.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/FlowField.cs tests/SimCore.Tests/FlowFieldApproachTests.cs
git commit -m "feat: flow fields approach impassable targets via neighbor seeding"
```

---

### Task 8: Resource nodes and harvesting

The full economy loop: nodes block passability, harvesters cycle move→gather→return→deposit, depleted nodes vanish and unblock their cell.

**Files:**
- Create: `src/SimCore/Sim/ResourceNode.cs`
- Create: `src/SimCore/Sim/SimWorld.Economy.cs`
- Modify: `src/SimCore/Sim/Unit.cs` (harvest runtime state)
- Modify: `src/SimCore/Sim/Commands.cs` (add `HarvestCommand`)
- Modify: `src/SimCore/Sim/SimWorld.cs` (`Apply` case, `Step` order, node list)
- Test: `tests/SimCore.Tests/HarvestTests.cs`

- [ ] **Step 1: Write the failing tests** at `tests/SimCore.Tests/HarvestTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class HarvestTests
{
    private static readonly UnitSpec WorkerSpec =
        new(MaxHp: 30, Speed: Fix.FromFraction(1, 2), MineralCost: 50, SupplyCost: 1, BuildTimeTicks: 10,
            Harvester: new HarvesterSpec(CarryCapacity: 5, GatherTicks: 4));

    private static (SimWorld w, int worker, int nodeId) EconomyWorld()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        w.Players[0].Minerals = 200;
        w.Players[0].SupplyCap = 10;
        var nodeId = w.AddResourceNode(12, 5, amount: 12);
        var worker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), WorkerSpec);
        // completed depot at (3,4): place + finish construction
        w.Step(new Command[] { new BuildCommand(0, worker, BuildingTests.Depot, 3, 4) });
        for (int i = 0; i < BuildingTests.Depot.BuildTimeTicks; i++) w.Step(System.Array.Empty<Command>());
        return (w, worker, nodeId);
    }

    [Fact]
    public void Node_Blocks_Passability()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        w.AddResourceNode(12, 5, 100);
        Assert.False(w.Map.IsPassable(12, 5));
    }

    [Fact]
    public void Full_Harvest_Cycle_Deposits_Minerals()
    {
        var (w, worker, _) = EconomyWorld();
        var start = w.Players[0].Minerals;
        w.Step(new Command[] { new HarvestCommand(0, new[] { worker }, w.Nodes[0].Id) });
        for (int i = 0; i < 300 && w.Players[0].Minerals == start; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(start + 5, w.Players[0].Minerals); // one full carry deposited
    }

    [Fact]
    public void Depleted_Node_Is_Removed_And_Cell_Unblocked()
    {
        var (w, worker, nodeId) = EconomyWorld();
        w.Step(new Command[] { new HarvestCommand(0, new[] { worker }, nodeId) });
        // 12 minerals at 5/trip = 3 trips
        for (int i = 0; i < 900 && w.GetNode(nodeId) is not null; i++) w.Step(System.Array.Empty<Command>());
        Assert.Null(w.GetNode(nodeId));
        Assert.True(w.Map.IsPassable(12, 5));
        // let the worker finish its last return trip
        for (int i = 0; i < 300 && w.GetUnit(worker)!.HarvestPhase != HarvestPhase.None; i++)
            w.Step(System.Array.Empty<Command>());
        // 200 start - 100 depot + 12 fully harvested = 112
        Assert.Equal(112, w.Players[0].Minerals);
    }

    [Fact]
    public void NonHarvester_Ignores_Harvest_Command()
    {
        var (w, _, nodeId) = EconomyWorld();
        var soldier = w.SpawnUnit(0, w.Map.CellCenter(6, 6), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new HarvestCommand(0, new[] { soldier }, nodeId) });
        Assert.Equal(HarvestPhase.None, w.GetUnit(soldier)!.HarvestPhase);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter HarvestTests`
Expected: compilation FAILS (`AddResourceNode`, `HarvestCommand`, `HarvestPhase` don't exist).

- [ ] **Step 3: Implement.**

Create `src/SimCore/Sim/ResourceNode.cs`:

```csharp
namespace SimCore.Sim;

public sealed class ResourceNode
{
    public int Id { get; init; }
    public int CellX { get; init; }
    public int CellY { get; init; }
    public int Remaining { get; set; }
}
```

In `src/SimCore/Sim/Unit.cs`, add:

```csharp
    public HarvestPhase HarvestPhase { get; set; }
    public int HarvestNodeId { get; set; }
    public int CarriedMinerals { get; set; }
    public int GatherTicksRemaining { get; set; }
```

and at the bottom of the file (outside the class):

```csharp
public enum HarvestPhase : byte { None = 0, MovingToNode = 1, Gathering = 2, Returning = 3 }
```

In `src/SimCore/Sim/Commands.cs`, add:

```csharp
public sealed record HarvestCommand(int PlayerId, int[] UnitIds, int NodeId) : Command(PlayerId);
```

Create `src/SimCore/Sim/SimWorld.Economy.cs`:

```csharp
using SimCore.Math;

namespace SimCore.Sim;

public sealed partial class SimWorld
{
    private readonly System.Collections.Generic.List<ResourceNode> _nodes = new(); // stable order
    private readonly System.Collections.Generic.Dictionary<int, ResourceNode> _nodesById = new(); // lookup only

    public System.Collections.Generic.IReadOnlyList<ResourceNode> Nodes => _nodes;
    public ResourceNode? GetNode(int id) => _nodesById.TryGetValue(id, out var n) ? n : null;

    /// <summary>Setup-time API (map generation). Blocks the cell.</summary>
    public int AddResourceNode(int cellX, int cellY, int amount)
    {
        var n = new ResourceNode { Id = _nextId++, CellX = cellX, CellY = cellY, Remaining = amount };
        _nodes.Add(n);
        _nodesById[n.Id] = n;
        Map.SetPassable(cellX, cellY, false);
        return n.Id;
    }

    private void RemoveNode(ResourceNode n)
    {
        Map.SetPassable(n.CellX, n.CellY, true);
        _nodesById.Remove(n.Id);
        _nodes.Remove(n);
    }

    private bool IsAdjacentToCell(FixVec pos, int cellX, int cellY)
    {
        var (px, py) = Map.WorldToCell(pos);
        return System.Math.Abs(px - cellX) <= 1 && System.Math.Abs(py - cellY) <= 1;
    }

    private bool IsAdjacentToFootprint(FixVec pos, Building b)
    {
        var (px, py) = Map.WorldToCell(pos);
        return px >= b.CellX - 1 && px <= b.CellX + b.Spec.Width
            && py >= b.CellY - 1 && py <= b.CellY + b.Spec.Height;
    }

    /// <summary>Nearest owned completed depot by squared distance; ties → earliest in list (spawn order).</summary>
    private Building? NearestOwnedDepot(Unit u)
    {
        Building? best = null;
        Fix bestDist = default;
        foreach (var b in _buildings)
        {
            if (b.OwnerId != u.OwnerId || !b.IsComplete || !b.Spec.IsDepot || b.Hp <= 0) continue;
            var d = (CenterOf(b) - u.Position).LengthSquared();
            if (best is null || d < bestDist) { best = b; bestDist = d; }
        }
        return best;
    }

    private void IssueApproach(Unit u, FixVec target)
    {
        var (tx, ty) = Map.WorldToCell(target);
        u.HasMoveOrder = true;
        u.MoveTarget = target;
        u.Path = GetField(tx, ty);
        u.PathVersion = Map.Version;
    }

    private void UpdateHarvest()
    {
        foreach (var u in _units)
        {
            if (u.Harvester is null || u.HarvestPhase == HarvestPhase.None) continue;
            var node = GetNode(u.HarvestNodeId);
            switch (u.HarvestPhase)
            {
                case HarvestPhase.MovingToNode:
                    if (node is null || node.Remaining <= 0)
                    {
                        u.HarvestPhase = u.CarriedMinerals > 0 ? HarvestPhase.Returning : HarvestPhase.None;
                        break;
                    }
                    if (IsAdjacentToCell(u.Position, node.CellX, node.CellY))
                    {
                        u.HasMoveOrder = false;
                        u.Path = null;
                        u.HarvestPhase = HarvestPhase.Gathering;
                        u.GatherTicksRemaining = u.Harvester.GatherTicks;
                    }
                    else if (!u.HasMoveOrder)
                    {
                        IssueApproach(u, Map.CellCenter(node.CellX, node.CellY));
                    }
                    break;

                case HarvestPhase.Gathering:
                    if (node is null) { u.HarvestPhase = u.CarriedMinerals > 0 ? HarvestPhase.Returning : HarvestPhase.None; break; }
                    u.GatherTicksRemaining--;
                    if (u.GatherTicksRemaining > 0) break;
                    var take = System.Math.Min(u.Harvester.CarryCapacity, node.Remaining);
                    node.Remaining -= take;
                    u.CarriedMinerals = take;
                    if (node.Remaining <= 0) RemoveNode(node);
                    u.HarvestPhase = HarvestPhase.Returning;
                    break;

                case HarvestPhase.Returning:
                    var depot = NearestOwnedDepot(u);
                    if (depot is null) break; // no depot yet — wait in place
                    if (IsAdjacentToFootprint(u.Position, depot))
                    {
                        u.HasMoveOrder = false;
                        u.Path = null;
                        _players[u.OwnerId].Minerals += u.CarriedMinerals;
                        u.CarriedMinerals = 0;
                        u.HarvestPhase = GetNode(u.HarvestNodeId) is { Remaining: > 0 }
                            ? HarvestPhase.MovingToNode
                            : HarvestPhase.None;
                    }
                    else if (!u.HasMoveOrder)
                    {
                        IssueApproach(u, CenterOf(depot));
                    }
                    break;
            }
        }
    }
}
```

In `src/SimCore/Sim/SimWorld.cs`:
- In `Apply`, add the case:

```csharp
            case HarvestCommand hc:
                if (GetNode(hc.NodeId) is null) break;
                foreach (var id in hc.UnitIds)
                {
                    var u = GetUnit(id);
                    if (u is null || u.OwnerId != hc.PlayerId || u.Harvester is null) continue;
                    u.HarvestPhase = HarvestPhase.MovingToNode;
                    u.HarvestNodeId = hc.NodeId;
                    u.AttackTargetId = 0;
                    u.IsAttackMoving = false;
                    u.HasMoveOrder = false; // UpdateHarvest issues the approach
                    u.Path = null;
                }
                break;
```

- In `Step`, insert `UpdateHarvest();` between `MoveUnits();` and `UpdateConstruction();`.
- Also: `MoveCommand` should cancel harvesting. In its per-unit loop add:

```csharp
                    u.HarvestPhase = HarvestPhase.None;
```

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: 101 passing. Golden trajectory UNCHANGED (no nodes/harvesters in scenario; the MoveCommand cancel line touches a field that's always None there).

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/ResourceNode.cs src/SimCore/Sim/SimWorld.Economy.cs src/SimCore/Sim/Unit.cs src/SimCore/Sim/Commands.cs src/SimCore/Sim/SimWorld.cs tests/SimCore.Tests/HarvestTests.cs
git commit -m "feat: resource nodes and full harvest cycle (gather, return, deposit)"
```

---

### Task 9: StateHasher v3 — players, buildings, nodes, harvest state, map passability

Closes plan-2a carry-forward #1: passability is now mutable mid-run (buildings, node depletion), so it MUST be hashed. Also folds in all new aggregates.

**Files:**
- Modify: `src/SimCore/Sim/StateHasher.cs`
- Test: `tests/SimCore.Tests/StateHasherTests.cs` (append tests)
- Modify: `tests/SimCore.Tests/DeterminismTests.cs` (re-pin golden)

- [ ] **Step 1: Write the failing tests** — append inside `StateHasherTests`:

```csharp
    [Fact]
    public void Minerals_Change_Changes_Hash()
    {
        var a = World();
        var b = World();
        b.Players[0].Minerals = 50;
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Passability_Change_Changes_Hash()
    {
        var a = World();
        var b = World();
        b.Map.SetPassable(3, 3, false);
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Building_Hp_Change_Changes_Hash()
    {
        var a = World();
        var b = World();
        // identical buildings → equal hash; then drift hp
        var specA = new BuildingSpec(100, 2, 2, 100, 10);
        var specB = new BuildingSpec(100, 2, 2, 100, 10);
        a.PlaceBuilding(0, specA, 8, 8);
        b.PlaceBuilding(0, specB, 8, 8);
        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
        b.Buildings[0].Hp = 1;
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Harvest_State_Changes_Hash()
    {
        var a = World();
        var b = World();
        b.GetUnit(1)!.CarriedMinerals = 3;
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }
```

The `Building_Hp_Change_Changes_Hash` test calls the internal `PlaceBuilding` directly — write it as `a.PlaceBuilding(0, specA, 8, 8)` / `b.PlaceBuilding(0, specB, 8, 8)` (NOT `PlaceBuildingForTest`). To grant test access, add `InternalsVisibleTo` in `src/SimCore/SimCore.csproj` inside the existing `<Project>`:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="SimCore.Tests" />
  </ItemGroup>
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter StateHasherTests`
Expected: the 4 new tests FAIL (fields not hashed); prior 8 pass.

- [ ] **Step 3: Implement.** In `src/SimCore/Sim/StateHasher.cs`, extend `Hash`. After the existing per-unit loop's harvest-free fields, add the new unit fields inside the loop (after the weapon block):

```csharp
            h = Mix(h, (ulong)u.SupplyCost);
            h = Mix(h, (ulong)u.HarvestPhase);
            h = Mix(h, (ulong)u.HarvestNodeId);
            h = Mix(h, (ulong)u.CarriedMinerals);
            h = Mix(h, (ulong)u.GatherTicksRemaining);
            h = Mix(h, u.Harvester is null ? 0UL : 1UL);
            if (u.Harvester is { } hv)
            {
                h = Mix(h, (ulong)hv.CarryCapacity);
                h = Mix(h, (ulong)hv.GatherTicks);
            }
```

After the unit loop, add the new aggregates:

```csharp
        foreach (var p in world.Players)
        {
            h = Mix(h, (ulong)p.Minerals);
            h = Mix(h, (ulong)p.SupplyUsed);
            h = Mix(h, (ulong)p.SupplyCap);
        }

        h = Mix(h, (ulong)world.Buildings.Count);
        foreach (var b in world.Buildings)
        {
            h = Mix(h, (ulong)b.Id);
            h = Mix(h, (ulong)b.OwnerId);
            h = Mix(h, (ulong)b.CellX);
            h = Mix(h, (ulong)b.CellY);
            h = Mix(h, (ulong)b.Hp);
            h = Mix(h, b.IsComplete ? 1UL : 0UL);
            h = Mix(h, (ulong)b.BuildProgress);
            h = Mix(h, (ulong)b.Spec.MaxHp);
            h = Mix(h, (ulong)b.Spec.Width);
            h = Mix(h, (ulong)b.Spec.Height);
            h = Mix(h, (ulong)b.Spec.SupplyProvided);
            h = Mix(h, b.Spec.IsDepot ? 1UL : 0UL);
            h = Mix(h, b.Spec.CanTrain ? 1UL : 0UL);
            h = Mix(h, (ulong)b.Queue.Count);
            foreach (var item in b.Queue)
            {
                h = Mix(h, (ulong)item.RemainingTicks);
                h = Mix(h, (ulong)item.Spec.MaxHp);
                h = Mix(h, (ulong)item.Spec.Speed.Raw);
                h = Mix(h, (ulong)item.Spec.SupplyCost);
            }
        }

        h = Mix(h, (ulong)world.Nodes.Count);
        foreach (var n in world.Nodes)
        {
            h = Mix(h, (ulong)n.Id);
            h = Mix(h, (ulong)n.CellX);
            h = Mix(h, (ulong)n.CellY);
            h = Mix(h, (ulong)n.Remaining);
        }

        // Map passability — mutable mid-run since buildings/nodes; packed 64 cells per Mix.
        h = Mix(h, (ulong)world.Map.Version);
        ulong acc = 0;
        int bits = 0;
        for (int y = 0; y < world.Map.Height; y++)
            for (int x = 0; x < world.Map.Width; x++)
            {
                acc = (acc << 1) | (world.Map.IsPassable(x, y) ? 1UL : 0UL);
                if (++bits == 64) { h = Mix(h, acc); acc = 0; bits = 0; }
            }
        if (bits > 0) h = Mix(h, acc);
```

Update the convention doc comment: remove the "MapGrid passability ... MUST be hashed once build/destroy commands can mutate it" exclusion line (it's now hashed) and note `Map.Version` is included.

- [ ] **Step 4: Run the suite and re-pin**

Run: `dotnet test`. The 4 new tests PASS; `Trajectory_Hash_Matches_Golden_Constant` FAILS (hash function changed — expected). Re-pin `GoldenTrajectoryHash` with the actual value, re-run `dotnet test` and `dotnet test --configuration Release` — expect 105 passing in both with the same constant (Debug/Release disagreement = BLOCKED).

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/SimCore.csproj src/SimCore/Sim/StateHasher.cs tests/SimCore.Tests/StateHasherTests.cs tests/SimCore.Tests/DeterminismTests.cs
git commit -m "feat: StateHasher v3 — players, buildings, nodes, harvest state, packed passability (re-pin golden)"
```

---

### Task 10: Determinism scenario v3 — economy under the net

The 500-tick guardrail gains a parallel economy: a worker builds a depot and barracks, harvests a node, and the barracks trains units mid-battle. Verify with temporary instrumentation that all economy paths actually execute, then re-pin.

**Files:**
- Modify: `tests/SimCore.Tests/DeterminismTests.cs` (scenario + re-pin)

- [ ] **Step 1: Replace `Scenario()`** in `tests/SimCore.Tests/DeterminismTests.cs`:

```csharp
    /// <summary>Scenario v3: pitched battle (kiters → leash; explicit attack) PLUS a parallel
    /// economy — build depot + barracks, harvest a node to depletion, train marines mid-run.
    /// Every economy system (placement, construction, supply, production, harvest, node
    /// removal/passability restore) executes inside the hashed trajectory.</summary>
    private static (SimWorld world, Dictionary<int, List<Command>> script) Scenario()
    {
        var map = new MapGrid(40, 40);
        for (int y = 5; y < 35; y++) map.SetPassable(20, y, false);
        var w = new SimWorld(map, seed: 1234);
        w.Players[0].Minerals = 400;
        w.Players[1].Minerals = 400;

        var nodeId = w.AddResourceNode(10, 12, amount: 10);

        var ids = new List<int>();
        for (int i = 0; i < 20; i++)
        {
            var weapon = new Weapon { Damage = 5, Range = Fix.FromInt(2), CooldownTicks = 8 };
            var speed = i % 5 == 0 ? Fix.FromFraction(4, 5) : Fix.FromFraction(2, 5); // some fast kiters
            ids.Add(w.SpawnUnit(i % 2, w.Map.CellCenter(2 + i % 5, 2 + i / 5), speed, 60, weapon));
        }

        var workerSpec = new UnitSpec(30, Fix.FromFraction(1, 2), 50, 1, 10,
            Harvester: new HarvesterSpec(CarryCapacity: 5, GatherTicks: 4));
        var worker = w.SpawnUnit(0, w.Map.CellCenter(8, 10), workerSpec);
        w.Players[0].SupplyCap = 4; // headroom for worker + first marine before depot completes

        var depotSpec = new BuildingSpec(100, 2, 2, 100, BuildTimeTicks: 30, SupplyProvided: 8, IsDepot: true);
        var raxSpec = new BuildingSpec(150, 2, 2, 150, BuildTimeTicks: 40, CanTrain: true);
        var marineSpec = new UnitSpec(40, Fix.FromFraction(1, 2), 50, 1, 25,
            Weapon: new WeaponSpec(6, Fix.FromInt(2), 5));

        int[] Owned(int owner) => ids.FindAll(i => w.GetUnit(i)!.OwnerId == owner).ToArray();

        var script = new Dictionary<int, List<Command>>
        {
            [0] = new()
            {
                new MoveCommand(0, Owned(0), w.Map.CellCenter(35, 35)),
                new BuildCommand(0, worker, depotSpec, 7, 11),       // worker at (8,10) is in range
                new HarvestCommand(0, new[] { worker }, nodeId),     // harvest while depot builds
            },
            [50] = new() { new MoveCommand(1, Owned(1), w.Map.CellCenter(35, 2)) },
            [80] = new() { new BuildCommand(0, worker, raxSpec, 11, 9) }, // ignored if worker out of range — deterministic either way
            [120] = new() { new MoveCommand(0, new[] { ids[0], ids[2] }, w.Map.CellCenter(2, 38)) },
            [200] = new() { new AttackMoveCommand(0, Owned(0), w.Map.CellCenter(35, 2)) },
            [220] = new() { new AttackMoveCommand(1, Owned(1), w.Map.CellCenter(35, 35)) },
            [230] = new() { new MoveCommand(1, new[] { ids[5], ids[15] }, w.Map.CellCenter(38, 20)) },
            [250] = new() { new TrainCommand(0, RaxId, marineSpec), new TrainCommand(0, RaxId, marineSpec) },
            [350] = new() { new AttackCommand(0, Owned(0), ids[11]) },
        };
        return (w, script);
    }
```

Also add this constant near the top of the `DeterminismTests` class (the script is built before the run, but entity ids are deterministic, so the barracks id is knowable statically):

```csharp
    // Deterministic entity id of the barracks: node=1, units 2-21, worker=22, depot=23, rax=24.
    // If spawn order in Scenario() changes, update this and re-pin the golden constant.
    private const int RaxId = 24;
```

- [ ] **Step 2: Run the determinism tests**

Run: `dotnet test --filter DeterminismTests`
Expected: the two replay tests PASS (if not: real nondeterminism in economy code — debug honestly, BLOCKED if unresolved). Golden FAILS → re-pin `GoldenTrajectoryHash`.

- [ ] **Step 3: Verify the economy actually ran (temporary instrumentation)**

Temporarily (in a scratch copy or temporary test) run the scenario 500 ticks and record: final `Players[0].Minerals` (must reflect deposits: economy spent 250 on buildings + 100 on marines from a 400 + 10-harvest budget), `Buildings.Count` for player 0 (≥2 — depot + rax, unless destroyed late), units with marine stats existing (trained), `GetNode` for the node id returning null (depleted) and cell (10,12) passable again. Report all observed values. REMOVE the instrumentation; `git status` must show only DeterminismTests.cs modified.

If the rax id assumption (24) is wrong, the TrainCommands are silently ignored and no marines appear — in that case determine the actual id from instrumentation, fix `RaxId`, re-pin, and re-verify.

- [ ] **Step 4: Full suite, both configs**

Run: `dotnet test` and `dotnet test --configuration Release` — expect 105 passing in both, identical golden constant.

- [ ] **Step 5: Commit**

```bash
git add tests/SimCore.Tests/DeterminismTests.cs
git commit -m "test: determinism scenario v3 — economy (build/train/harvest) under the trajectory net (re-pin golden)"
```

---

## Done Criteria

- `dotnet test --configuration Release` passes (~105 tests) including scenario v3 with verified economy execution.
- Plan-2a carry-forwards: ✅ #1 passability hashed (T9), ✅ #2 weapon cloning (T1), ✅ #5 spec records (T1), ✅ #7 named constants (T1), ✅ #9 deferred-scenario items folded into v3 (T10). #3 fog gates → plan 2c (next). #4/#6 deferred with rationale. #8 → plan 4.
- `grep -ri godot src/SimCore` → no hits; no float/double outside `Fix.ToString()` (note: `System.Math.Abs/Min` on ints is integer math — allowed).

**Next plan:** Fog of war (plan 2c) — per-player vision maps from unit/building sight, visibility gates on attack commands and target acquisition, hasher exclusion documentation, scenario v4.
