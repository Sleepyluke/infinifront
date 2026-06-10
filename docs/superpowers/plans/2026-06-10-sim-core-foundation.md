# Simulation Core Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the deterministic, Godot-free C# simulation core — fixed-point math, tick loop, command queue, map grid, flow-field pathfinding, unit movement, and state hashing with a CI-grade determinism test.

**Architecture:** A standalone .NET class library (`SimCore`) with zero Godot dependencies, exercised by an xUnit test project. All game math uses Q48.16 fixed-point (`Fix`), all mutation flows through per-tick `Command` objects, and a FNV-1a state hash proves replay determinism. Godot consumes this library in a later plan.

**Tech Stack:** .NET 8 SDK, xUnit, (Godot 4.x .NET arrives in the presentation plan — not needed here).

**Phase 1 plan sequence** (this is plan 1 of 5):
1. **Simulation core foundation** ← this plan
2. Combat, economy & buildings (components, damage, resources, supply, production, fog of war)
3. Faction pack system (schema, validator, point budget, loader, reference faction JSON)
4. Godot presentation layer (rendering, input, UI, sprite import, silhouette fallback)
5. CPU opponent & match flow (pack-driven AI, victory, faction select, import screen)

**Spec:** `docs/superpowers/specs/2026-06-10-llm-faction-rts-design.md`

---

### Task 1: Solution scaffolding

**Files:**
- Create: `src/SimCore/SimCore.csproj`
- Create: `tests/SimCore.Tests/SimCore.Tests.csproj`
- Create: `LlmRts.sln`
- Create: `.gitignore`

- [ ] **Step 1: Verify .NET 8 SDK is available**

Run: `dotnet --version`
Expected: `8.x.x` (any 8.0+). If missing, install from https://dotnet.microsoft.com/download/dotnet/8.0 before continuing.

- [ ] **Step 2: Create solution and projects**

Run from repo root (`C:\Users\lssha\llm-rts`):

```bash
dotnet new sln -n LlmRts
dotnet new classlib -o src/SimCore -n SimCore -f net8.0
dotnet new xunit -o tests/SimCore.Tests -n SimCore.Tests -f net8.0
dotnet sln add src/SimCore tests/SimCore.Tests
dotnet add tests/SimCore.Tests reference src/SimCore
```

Delete the template files `src/SimCore/Class1.cs` and `tests/SimCore.Tests/UnitTest1.cs`.

- [ ] **Step 3: Add .gitignore**

```gitignore
bin/
obj/
.godot/
*.user
TestResults/
```

- [ ] **Step 4: Verify the empty solution builds and tests run**

Run: `dotnet test`
Expected: build succeeds, `0 tests` discovered (template test was deleted), exit code 0.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore: scaffold SimCore solution with xUnit test project"
```

---

### Task 2: Fixed-point number type (`Fix`)

Determinism rule from the spec: **no floats in game logic**. `Fix` is a Q48.16 fixed-point value stored in a `long`.

**Files:**
- Create: `src/SimCore/Math/Fix.cs`
- Test: `tests/SimCore.Tests/FixTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SimCore.Tests/FixTests.cs
using SimCore.Math;
using Xunit;

public class FixTests
{
    [Fact]
    public void FromInt_RoundTrips() => Assert.Equal(5, Fix.FromInt(5).ToInt());

    [Fact]
    public void Add_And_Sub_Work()
    {
        var a = Fix.FromInt(3);
        var b = Fix.FromInt(2);
        Assert.Equal(5, (a + b).ToInt());
        Assert.Equal(1, (a - b).ToInt());
    }

    [Fact]
    public void Mul_Works_With_Fractions()
    {
        var half = Fix.FromFraction(1, 2);
        Assert.Equal(3, (Fix.FromInt(6) * half).ToInt());
    }

    [Fact]
    public void Div_Works() => Assert.Equal(4, (Fix.FromInt(12) / Fix.FromInt(3)).ToInt());

    [Fact]
    public void Sqrt_Of_PerfectSquare() => Assert.Equal(9, Fix.Sqrt(Fix.FromInt(81)).ToInt());

    [Fact]
    public void Sqrt_Of_Two_Is_Close()
    {
        var r = Fix.Sqrt(Fix.FromInt(2));
        // 1.41421 in Q48.16 ≈ raw 92681; accept ±2 raw units
        Assert.InRange(r.Raw, 92679, 92683);
    }

    [Fact]
    public void Comparisons_Work()
    {
        Assert.True(Fix.FromInt(1) < Fix.FromInt(2));
        Assert.True(Fix.FromInt(2) >= Fix.FromInt(2));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FixTests`
Expected: compilation FAILS — `Fix` does not exist.

- [ ] **Step 3: Implement `Fix`**

```csharp
// src/SimCore/Math/Fix.cs
namespace SimCore.Math;

/// <summary>Q48.16 fixed-point number. The only numeric type allowed in sim logic.</summary>
public readonly struct Fix : System.IComparable<Fix>, System.IEquatable<Fix>
{
    public const int FractionalBits = 16;
    public readonly long Raw;

    public static readonly Fix Zero = new(0);
    public static readonly Fix One = new(1L << FractionalBits);

    public Fix(long raw) => Raw = raw;

    public static Fix FromInt(int v) => new((long)v << FractionalBits);
    public static Fix FromFraction(int numerator, int denominator) =>
        new(((long)numerator << FractionalBits) / denominator);

    public int ToInt() => (int)(Raw >> FractionalBits); // floor

    public static Fix operator +(Fix a, Fix b) => new(a.Raw + b.Raw);
    public static Fix operator -(Fix a, Fix b) => new(a.Raw - b.Raw);
    public static Fix operator -(Fix a) => new(-a.Raw);
    public static Fix operator *(Fix a, Fix b) => new((a.Raw * b.Raw) >> FractionalBits);
    public static Fix operator /(Fix a, Fix b) => new((a.Raw << FractionalBits) / b.Raw);

    public static bool operator <(Fix a, Fix b) => a.Raw < b.Raw;
    public static bool operator >(Fix a, Fix b) => a.Raw > b.Raw;
    public static bool operator <=(Fix a, Fix b) => a.Raw <= b.Raw;
    public static bool operator >=(Fix a, Fix b) => a.Raw >= b.Raw;
    public static bool operator ==(Fix a, Fix b) => a.Raw == b.Raw;
    public static bool operator !=(Fix a, Fix b) => a.Raw != b.Raw;

    /// <summary>Integer Newton's method on the raw value; deterministic.</summary>
    public static Fix Sqrt(Fix v)
    {
        if (v.Raw <= 0) return Zero;
        // sqrt(raw * 2^16) gives the Q48.16 root of the Q48.16 input
        var n = (System.UInt128)(ulong)v.Raw << FractionalBits;
        System.UInt128 x = n, y = (x + 1) / 2;
        while (y < x) { x = y; y = (x + n / x) / 2; }
        return new Fix((long)(ulong)x);
    }

    public int CompareTo(Fix other) => Raw.CompareTo(other.Raw);
    public bool Equals(Fix other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fix f && Equals(f);
    public override int GetHashCode() => Raw.GetHashCode();
    public override string ToString() => ((double)Raw / (1 << FractionalBits)).ToString("0.####");
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FixTests`
Expected: 7 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Math/Fix.cs tests/SimCore.Tests/FixTests.cs
git commit -m "feat: Q48.16 fixed-point Fix type with deterministic sqrt"
```

---

### Task 3: Fixed-point 2D vector (`FixVec`)

**Files:**
- Create: `src/SimCore/Math/FixVec.cs`
- Test: `tests/SimCore.Tests/FixVecTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SimCore.Tests/FixVecTests.cs
using SimCore.Math;
using Xunit;

public class FixVecTests
{
    [Fact]
    public void Add_Works()
    {
        var v = new FixVec(Fix.FromInt(1), Fix.FromInt(2)) + new FixVec(Fix.FromInt(3), Fix.FromInt(4));
        Assert.Equal(4, v.X.ToInt());
        Assert.Equal(6, v.Y.ToInt());
    }

    [Fact]
    public void Length_Of_3_4_Is_5()
    {
        var v = new FixVec(Fix.FromInt(3), Fix.FromInt(4));
        Assert.Equal(Fix.FromInt(5), v.Length());
    }

    [Fact]
    public void Normalized_Times_Length_Restores_Vector_Approximately()
    {
        var v = new FixVec(Fix.FromInt(10), Fix.FromInt(0));
        var n = v.Normalized();
        Assert.Equal(Fix.One, n.X);
        Assert.Equal(Fix.Zero, n.Y);
    }

    [Fact]
    public void Normalized_Zero_Is_Zero()
    {
        var n = FixVec.Zero.Normalized();
        Assert.Equal(Fix.Zero, n.X);
        Assert.Equal(Fix.Zero, n.Y);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FixVecTests`
Expected: compilation FAILS — `FixVec` does not exist.

- [ ] **Step 3: Implement `FixVec`**

```csharp
// src/SimCore/Math/FixVec.cs
namespace SimCore.Math;

public readonly struct FixVec : System.IEquatable<FixVec>
{
    public readonly Fix X;
    public readonly Fix Y;

    public static readonly FixVec Zero = new(Fix.Zero, Fix.Zero);

    public FixVec(Fix x, Fix y) { X = x; Y = y; }
    public static FixVec FromInts(int x, int y) => new(Fix.FromInt(x), Fix.FromInt(y));

    public static FixVec operator +(FixVec a, FixVec b) => new(a.X + b.X, a.Y + b.Y);
    public static FixVec operator -(FixVec a, FixVec b) => new(a.X - b.X, a.Y - b.Y);
    public static FixVec operator *(FixVec a, Fix s) => new(a.X * s, a.Y * s);

    public Fix LengthSquared() => X * X + Y * Y;
    public Fix Length() => Fix.Sqrt(LengthSquared());

    public FixVec Normalized()
    {
        var len = Length();
        return len == Fix.Zero ? Zero : new FixVec(X / len, Y / len);
    }

    public bool Equals(FixVec other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is FixVec v && Equals(v);
    public override int GetHashCode() => System.HashCode.Combine(X, Y);
    public override string ToString() => $"({X}, {Y})";
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FixVecTests`
Expected: 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Math/FixVec.cs tests/SimCore.Tests/FixVecTests.cs
git commit -m "feat: FixVec fixed-point 2D vector"
```

---

### Task 4: Deterministic RNG

**Files:**
- Create: `src/SimCore/DeterministicRandom.cs`
- Test: `tests/SimCore.Tests/DeterministicRandomTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SimCore.Tests/DeterministicRandomTests.cs
using SimCore;
using Xunit;

public class DeterministicRandomTests
{
    [Fact]
    public void Same_Seed_Same_Sequence()
    {
        var a = new DeterministicRandom(42);
        var b = new DeterministicRandom(42);
        for (int i = 0; i < 100; i++)
            Assert.Equal(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void Different_Seeds_Differ()
    {
        var a = new DeterministicRandom(1);
        var b = new DeterministicRandom(2);
        Assert.NotEqual(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void NextInt_Respects_Bounds()
    {
        var r = new DeterministicRandom(7);
        for (int i = 0; i < 1000; i++)
            Assert.InRange(r.NextInt(5, 10), 5, 9); // max exclusive
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter DeterministicRandomTests`
Expected: compilation FAILS.

- [ ] **Step 3: Implement xorshift64* RNG**

```csharp
// src/SimCore/DeterministicRandom.cs
namespace SimCore;

/// <summary>xorshift64* — deterministic across platforms. Never use System.Random in sim code.</summary>
public sealed class DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(ulong seed) => _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    public uint NextUInt()
    {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return (uint)((_state * 0x2545F4914F6CDD1DUL) >> 32);
    }

    /// <summary>Returns value in [minInclusive, maxExclusive).</summary>
    public int NextInt(int minInclusive, int maxExclusive) =>
        minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter DeterministicRandomTests`
Expected: 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/DeterministicRandom.cs tests/SimCore.Tests/DeterministicRandomTests.cs
git commit -m "feat: deterministic xorshift64* RNG"
```

---

### Task 5: Map grid

A tile grid with passability. World coordinates are `Fix` units where **1 unit = 1 tile**; tile (cx, cy) spans world x ∈ [cx, cx+1).

**Files:**
- Create: `src/SimCore/Sim/MapGrid.cs`
- Test: `tests/SimCore.Tests/MapGridTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SimCore.Tests/MapGridTests.cs
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class MapGridTests
{
    [Fact]
    public void New_Grid_Is_All_Passable()
    {
        var g = new MapGrid(8, 8);
        Assert.True(g.IsPassable(0, 0));
        Assert.True(g.IsPassable(7, 7));
    }

    [Fact]
    public void Blocked_Cell_Is_Impassable()
    {
        var g = new MapGrid(8, 8);
        g.SetPassable(3, 4, false);
        Assert.False(g.IsPassable(3, 4));
    }

    [Fact]
    public void Out_Of_Bounds_Is_Impassable()
    {
        var g = new MapGrid(8, 8);
        Assert.False(g.IsPassable(-1, 0));
        Assert.False(g.IsPassable(8, 0));
    }

    [Fact]
    public void WorldToCell_Floors()
    {
        var g = new MapGrid(8, 8);
        var (cx, cy) = g.WorldToCell(new FixVec(Fix.FromFraction(5, 2), Fix.FromFraction(7, 2)));
        Assert.Equal(2, cx);
        Assert.Equal(3, cy);
    }

    [Fact]
    public void CellCenter_Is_Cell_Plus_Half()
    {
        var g = new MapGrid(8, 8);
        var c = g.CellCenter(2, 3);
        Assert.Equal(Fix.FromFraction(5, 2), c.X);
        Assert.Equal(Fix.FromFraction(7, 2), c.Y);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter MapGridTests`
Expected: compilation FAILS.

- [ ] **Step 3: Implement `MapGrid`**

```csharp
// src/SimCore/Sim/MapGrid.cs
using SimCore.Math;

namespace SimCore.Sim;

public sealed class MapGrid
{
    public int Width { get; }
    public int Height { get; }
    private readonly bool[] _passable; // index = y * Width + x

    public MapGrid(int width, int height)
    {
        Width = width;
        Height = height;
        _passable = new bool[width * height];
        System.Array.Fill(_passable, true);
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public bool IsPassable(int x, int y) => InBounds(x, y) && _passable[y * Width + x];

    public void SetPassable(int x, int y, bool value)
    {
        if (InBounds(x, y)) _passable[y * Width + x] = value;
    }

    public (int cx, int cy) WorldToCell(FixVec pos) => (pos.X.ToInt(), pos.Y.ToInt());

    public FixVec CellCenter(int cx, int cy) =>
        new(Fix.FromInt(cx) + Fix.FromFraction(1, 2), Fix.FromInt(cy) + Fix.FromFraction(1, 2));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter MapGridTests`
Expected: 5 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/MapGrid.cs tests/SimCore.Tests/MapGridTests.cs
git commit -m "feat: MapGrid with passability and world/cell conversion"
```

---

### Task 6: Units, commands, and the SimWorld tick loop

The world owns units in a `List` (stable iteration order = determinism), accepts commands tagged with a tick, and advances one tick at a time. Movement here is straight-line only; flow-field pathing replaces the movement step in Task 8.

**Files:**
- Create: `src/SimCore/Sim/Unit.cs`
- Create: `src/SimCore/Sim/Commands.cs`
- Create: `src/SimCore/Sim/SimWorld.cs`
- Test: `tests/SimCore.Tests/SimWorldTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SimCore.Tests/SimWorldTests.cs
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class SimWorldTests
{
    private static SimWorld NewWorld() => new(new MapGrid(32, 32), seed: 1);

    [Fact]
    public void Spawn_Assigns_Sequential_Ids()
    {
        var w = NewWorld();
        var a = w.SpawnUnit(ownerId: 0, pos: FixVec.FromInts(1, 1), speedPerTick: Fix.FromFraction(1, 10), hp: 50);
        var b = w.SpawnUnit(ownerId: 1, pos: FixVec.FromInts(2, 2), speedPerTick: Fix.FromFraction(1, 10), hp: 50);
        Assert.Equal(1, a);
        Assert.Equal(2, b);
    }

    [Fact]
    public void Tick_Advances()
    {
        var w = NewWorld();
        w.Step(System.Array.Empty<Command>());
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(2, w.Tick);
    }

    [Fact]
    public void MoveCommand_Moves_Unit_Toward_Target()
    {
        var w = NewWorld();
        var id = w.SpawnUnit(0, FixVec.FromInts(0, 0), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new MoveCommand(playerId: 0, unitIds: new[] { id }, target: FixVec.FromInts(10, 0)) });
        // moved 0.5 along +X
        Assert.Equal(Fix.FromFraction(1, 2), w.GetUnit(id)!.Position.X);
        Assert.Equal(Fix.Zero, w.GetUnit(id)!.Position.Y);
    }

    [Fact]
    public void Unit_Stops_At_Target()
    {
        var w = NewWorld();
        var id = w.SpawnUnit(0, FixVec.FromInts(0, 0), Fix.FromInt(1), 50);
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, FixVec.FromInts(3, 0)) });
        for (int i = 0; i < 10; i++) w.Step(System.Array.Empty<Command>());
        var u = w.GetUnit(id)!;
        Assert.Equal(Fix.FromInt(3), u.Position.X);
        Assert.False(u.HasMoveOrder);
    }

    [Fact]
    public void Move_Ignores_Units_Not_Owned_By_Player()
    {
        var w = NewWorld();
        var id = w.SpawnUnit(ownerId: 1, FixVec.FromInts(0, 0), Fix.FromInt(1), 50);
        w.Step(new Command[] { new MoveCommand(playerId: 0, new[] { id }, FixVec.FromInts(5, 0)) });
        Assert.Equal(Fix.Zero, w.GetUnit(id)!.Position.X);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter SimWorldTests`
Expected: compilation FAILS.

- [ ] **Step 3: Implement `Unit`, `Commands`, `SimWorld`**

```csharp
// src/SimCore/Sim/Unit.cs
using SimCore.Math;

namespace SimCore.Sim;

public sealed class Unit
{
    public int Id { get; init; }
    public int OwnerId { get; init; }
    public FixVec Position { get; set; }
    public Fix SpeedPerTick { get; set; }
    public int Hp { get; set; }

    public bool HasMoveOrder { get; set; }
    public FixVec MoveTarget { get; set; }
    public FlowField? Path { get; set; } // null until Task 8 wires pathfinding in
}
```

```csharp
// src/SimCore/Sim/Commands.cs
using SimCore.Math;

namespace SimCore.Sim;

/// <summary>Commands are the ONLY way to mutate the sim from outside (spec rule).</summary>
public abstract record Command(int PlayerId);

public sealed record MoveCommand(int PlayerId, int[] UnitIds, FixVec Target) : Command(PlayerId);
```

```csharp
// src/SimCore/Sim/SimWorld.cs
using System.Collections.Generic;
using SimCore.Math;

namespace SimCore.Sim;

public sealed class SimWorld
{
    public MapGrid Map { get; }
    public int Tick { get; private set; }
    public DeterministicRandom Rng { get; }

    private readonly List<Unit> _units = new(); // stable order — required for determinism
    private readonly Dictionary<int, Unit> _byId = new();
    private int _nextId = 1;

    public SimWorld(MapGrid map, ulong seed)
    {
        Map = map;
        Rng = new DeterministicRandom(seed);
    }

    public IReadOnlyList<Unit> Units => _units;
    public Unit? GetUnit(int id) => _byId.TryGetValue(id, out var u) ? u : null;

    public int SpawnUnit(int ownerId, FixVec pos, Fix speedPerTick, int hp)
    {
        var u = new Unit { Id = _nextId++, OwnerId = ownerId, Position = pos, SpeedPerTick = speedPerTick, Hp = hp };
        _units.Add(u);
        _byId[u.Id] = u;
        return u.Id;
    }

    /// <summary>Advance one tick. Commands are applied first, then systems run in fixed order.</summary>
    public void Step(IReadOnlyList<Command> commands)
    {
        foreach (var cmd in commands) Apply(cmd);
        MoveUnits();
        Tick++;
    }

    private void Apply(Command cmd)
    {
        switch (cmd)
        {
            case MoveCommand mv:
                foreach (var id in mv.UnitIds)
                {
                    var u = GetUnit(id);
                    if (u is null || u.OwnerId != mv.PlayerId) continue;
                    u.HasMoveOrder = true;
                    u.MoveTarget = mv.Target;
                    u.Path = null; // recomputed by pathfinding (Task 8)
                }
                break;
        }
    }

    private void MoveUnits()
    {
        foreach (var u in _units)
        {
            if (!u.HasMoveOrder) continue;
            var delta = u.MoveTarget - u.Position;
            var dist = delta.Length();
            if (dist <= u.SpeedPerTick)
            {
                u.Position = u.MoveTarget;
                u.HasMoveOrder = false;
            }
            else
            {
                u.Position += delta.Normalized() * u.SpeedPerTick;
            }
        }
    }
}
```

Note: `FlowField` doesn't exist yet — add a temporary stub so this compiles, replaced in Task 7:

```csharp
// src/SimCore/Sim/FlowField.cs  (stub — fully implemented in Task 7)
namespace SimCore.Sim;

public sealed class FlowField { }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter SimWorldTests`
Expected: 5 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim tests/SimCore.Tests/SimWorldTests.cs
git commit -m "feat: SimWorld tick loop with command queue and straight-line movement"
```

---

### Task 7: Flow-field pathfinding

A `FlowField` is computed for a target cell: BFS (uniform cost, 8-neighbor with no corner cutting) produces an integration cost per cell; each cell then stores the direction toward its cheapest neighbor.

**Files:**
- Modify: `src/SimCore/Sim/FlowField.cs` (replace the stub entirely)
- Test: `tests/SimCore.Tests/FlowFieldTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SimCore.Tests/FlowFieldTests.cs
using SimCore.Sim;
using Xunit;

public class FlowFieldTests
{
    [Fact]
    public void Open_Map_Points_Toward_Target()
    {
        var g = new MapGrid(10, 10);
        var f = FlowField.Compute(g, targetX: 9, targetY: 5);
        var (dx, dy) = f.DirectionAt(0, 5);
        Assert.Equal(1, dx);  // straight east
        Assert.Equal(0, dy);
    }

    [Fact]
    public void Target_Cell_Has_Zero_Direction()
    {
        var g = new MapGrid(10, 10);
        var f = FlowField.Compute(g, 9, 5);
        Assert.Equal((0, 0), f.DirectionAt(9, 5));
    }

    [Fact]
    public void Flow_Routes_Around_Wall()
    {
        var g = new MapGrid(10, 10);
        for (int y = 0; y < 9; y++) g.SetPassable(5, y, false); // wall with gap at y=9
        var f = FlowField.Compute(g, 9, 0);
        // west of the wall at (4,0): direct east is blocked, flow must head south toward the gap
        var (_, dy) = f.DirectionAt(4, 0);
        Assert.Equal(1, dy);
    }

    [Fact]
    public void Unreachable_Cell_Has_Zero_Direction()
    {
        var g = new MapGrid(10, 10);
        for (int y = 0; y < 10; y++) g.SetPassable(5, y, false); // full wall
        var f = FlowField.Compute(g, 9, 5);
        Assert.Equal((0, 0), f.DirectionAt(0, 5));
    }

    [Fact]
    public void Same_Inputs_Produce_Identical_Fields()
    {
        var g = new MapGrid(20, 20);
        g.SetPassable(10, 10, false);
        var a = FlowField.Compute(g, 15, 15);
        var b = FlowField.Compute(g, 15, 15);
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 20; x++)
                Assert.Equal(a.DirectionAt(x, y), b.DirectionAt(x, y));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FlowFieldTests`
Expected: compilation FAILS (`Compute`, `DirectionAt` don't exist on the stub).

- [ ] **Step 3: Implement `FlowField` (replace stub)**

```csharp
// src/SimCore/Sim/FlowField.cs
using System.Collections.Generic;

namespace SimCore.Sim;

/// <summary>Per-cell direction field toward a target. Deterministic: fixed neighbor order.</summary>
public sealed class FlowField
{
    public int TargetX { get; }
    public int TargetY { get; }
    private readonly int _width;
    private readonly int _height;
    private readonly int[] _cost;        // BFS integration cost; int.MaxValue = unreachable
    private readonly sbyte[] _dirX;
    private readonly sbyte[] _dirY;

    // Fixed neighbor order — never reorder, determinism depends on it.
    private static readonly (int dx, int dy)[] Neighbors =
        { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };

    private FlowField(int width, int height, int targetX, int targetY)
    {
        _width = width; _height = height;
        TargetX = targetX; TargetY = targetY;
        _cost = new int[width * height];
        _dirX = new sbyte[width * height];
        _dirY = new sbyte[width * height];
        System.Array.Fill(_cost, int.MaxValue);
    }

    public (int dx, int dy) DirectionAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _width || y >= _height) return (0, 0);
        var i = y * _width + x;
        return (_dirX[i], _dirY[i]);
    }

    public static FlowField Compute(MapGrid map, int targetX, int targetY)
    {
        var f = new FlowField(map.Width, map.Height, targetX, targetY);
        if (!map.IsPassable(targetX, targetY)) return f;

        // BFS integration field
        var queue = new Queue<(int x, int y)>();
        f._cost[targetY * map.Width + targetX] = 0;
        queue.Enqueue((targetX, targetY));
        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            var c = f._cost[cy * map.Width + cx];
            foreach (var (dx, dy) in Neighbors)
            {
                int nx = cx + dx, ny = cy + dy;
                if (!map.IsPassable(nx, ny)) continue;
                // no corner cutting: diagonal requires both orthogonal cells passable
                if (dx != 0 && dy != 0 && (!map.IsPassable(cx + dx, cy) || !map.IsPassable(cx, cy + dy))) continue;
                var ni = ny * map.Width + nx;
                if (f._cost[ni] != int.MaxValue) continue;
                f._cost[ni] = c + 1;
                queue.Enqueue((nx, ny));
            }
        }

        // Direction field: each cell points at its cheapest passable neighbor
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            var i = y * map.Width + x;
            if (f._cost[i] == int.MaxValue || f._cost[i] == 0) continue;
            int best = f._cost[i];
            (int bx, int by) = (0, 0);
            foreach (var (dx, dy) in Neighbors)
            {
                int nx = x + dx, ny = y + dy;
                if (!map.IsPassable(nx, ny)) continue;
                if (dx != 0 && dy != 0 && (!map.IsPassable(x + dx, y) || !map.IsPassable(x, y + dy))) continue;
                var nc = f._cost[ny * map.Width + nx];
                if (nc < best) { best = nc; (bx, by) = (dx, dy); }
            }
            f._dirX[i] = (sbyte)bx;
            f._dirY[i] = (sbyte)by;
        }
        return f;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FlowFieldTests`
Expected: 5 tests PASS. Also run `dotnet test` — Task 6 tests must still pass (stub replacement is source-compatible: `Unit.Path` only stores the type).

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/FlowField.cs tests/SimCore.Tests/FlowFieldTests.cs
git commit -m "feat: BFS flow-field pathfinding with no corner cutting"
```

---

### Task 8: Flow-field movement in SimWorld

Replace straight-line movement: a `MoveCommand` computes a `FlowField` for the target cell (cached per target per tick batch); units follow the field cell-by-cell, then home in on the exact target point inside the final cell.

**Files:**
- Modify: `src/SimCore/Sim/SimWorld.cs` (the `Apply` and `MoveUnits` methods)
- Test: `tests/SimCore.Tests/FlowMovementTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/SimCore.Tests/FlowMovementTests.cs
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class FlowMovementTests
{
    [Fact]
    public void Unit_Walks_Around_Wall_To_Target()
    {
        var map = new MapGrid(10, 10);
        for (int y = 0; y < 9; y++) map.SetPassable(5, y, false); // wall, gap at y=9
        var w = new SimWorld(map, seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(1, 0), Fix.FromFraction(1, 2), 50);

        var target = w.Map.CellCenter(8, 0);
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, target) });
        for (int i = 0; i < 200 && w.GetUnit(id)!.HasMoveOrder; i++)
            w.Step(System.Array.Empty<Command>());

        var u = w.GetUnit(id)!;
        Assert.False(u.HasMoveOrder);                 // arrived
        Assert.Equal(target, u.Position);             // exactly at target
    }

    [Fact]
    public void Unit_With_Unreachable_Target_Gives_Up()
    {
        var map = new MapGrid(10, 10);
        for (int y = 0; y < 10; y++) map.SetPassable(5, y, false); // full wall
        var w = new SimWorld(map, seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(1, 5), Fix.FromFraction(1, 2), 50);

        w.Step(new Command[] { new MoveCommand(0, new[] { id }, w.Map.CellCenter(8, 5)) });
        w.Step(System.Array.Empty<Command>());

        var u = w.GetUnit(id)!;
        Assert.False(u.HasMoveOrder);                          // order cancelled
        Assert.Equal(w.Map.CellCenter(1, 5), u.Position);      // didn't move
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FlowMovementTests`
Expected: FAIL — with straight-line movement the first test walks into the wall region and `Position` never equals `target` cleanly around obstacles (assertion failure), and the second test keeps `HasMoveOrder` true.

- [ ] **Step 3: Rewrite movement in `SimWorld`**

Replace the `Apply` `MoveCommand` case and `MoveUnits` in `src/SimCore/Sim/SimWorld.cs`:

```csharp
    private void Apply(Command cmd)
    {
        switch (cmd)
        {
            case MoveCommand mv:
                var (tx, ty) = Map.WorldToCell(mv.Target);
                var field = FlowField.Compute(Map, tx, ty); // shared by all units in this command
                foreach (var id in mv.UnitIds)
                {
                    var u = GetUnit(id);
                    if (u is null || u.OwnerId != mv.PlayerId) continue;
                    u.HasMoveOrder = true;
                    u.MoveTarget = mv.Target;
                    u.Path = field;
                }
                break;
        }
    }

    private void MoveUnits()
    {
        foreach (var u in _units)
        {
            if (!u.HasMoveOrder) continue;

            var (cx, cy) = Map.WorldToCell(u.Position);
            var (ttx, tty) = Map.WorldToCell(u.MoveTarget);

            FixVec step;
            if (cx == ttx && cy == tty)
            {
                // final cell: home in on the exact target point
                step = u.MoveTarget - u.Position;
            }
            else
            {
                var (dx, dy) = u.Path!.DirectionAt(cx, cy);
                if (dx == 0 && dy == 0) { u.HasMoveOrder = false; u.Path = null; continue; } // unreachable
                step = Map.CellCenter(cx + dx, cy + dy) - u.Position;
            }

            var dist = step.Length();
            if (dist <= u.SpeedPerTick)
            {
                u.Position += step;
                if (cx == ttx && cy == tty) { u.HasMoveOrder = false; u.Path = null; }
            }
            else
            {
                u.Position += step.Normalized() * u.SpeedPerTick;
            }
        }
    }
```

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test`
Expected: FlowMovementTests PASS. In `SimWorldTests`, `MoveCommand_Moves_Unit_Toward_Target` and `Unit_Stops_At_Target` still pass (open map: flow steps east through cell centers, and the final-cell homing gives exact arrival). All other tests PASS.

If `MoveCommand_Moves_Unit_Toward_Target` fails on the Y coordinate: the unit now walks via cell centers (y = 0.5) instead of a pure straight line. Update that test's expectation to assert `u.Position.X > Fix.Zero` and drop the exact-Y assertion — the behavioral contract is "moves toward target," not "moves in a perfectly straight line."

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/SimWorld.cs tests/SimCore.Tests/FlowMovementTests.cs tests/SimCore.Tests/SimWorldTests.cs
git commit -m "feat: units follow flow fields, cancel orders on unreachable targets"
```

---

### Task 9: State hashing

FNV-1a 64-bit over every unit's id, owner, raw position, hp, and the current tick. This is the determinism oracle for CI and, later, multiplayer desync detection.

**Files:**
- Create: `src/SimCore/Sim/StateHasher.cs`
- Test: `tests/SimCore.Tests/StateHasherTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SimCore.Tests/StateHasherTests.cs
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class StateHasherTests
{
    private static SimWorld World()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 5);
        w.SpawnUnit(0, FixVec.FromInts(1, 1), Fix.FromFraction(1, 2), 50);
        w.SpawnUnit(1, FixVec.FromInts(5, 5), Fix.FromFraction(1, 2), 80);
        return w;
    }

    [Fact]
    public void Identical_Worlds_Hash_Equal() =>
        Assert.Equal(StateHasher.Hash(World()), StateHasher.Hash(World()));

    [Fact]
    public void Position_Change_Changes_Hash()
    {
        var a = World();
        var b = World();
        b.GetUnit(1)!.Position = FixVec.FromInts(2, 1);
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Hp_Change_Changes_Hash()
    {
        var a = World();
        var b = World();
        b.GetUnit(2)!.Hp = 79;
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Tick_Changes_Hash()
    {
        var a = World();
        var b = World();
        b.Step(System.Array.Empty<Command>());
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter StateHasherTests`
Expected: compilation FAILS.

- [ ] **Step 3: Implement `StateHasher`**

```csharp
// src/SimCore/Sim/StateHasher.cs
namespace SimCore.Sim;

public static class StateHasher
{
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    public static ulong Hash(SimWorld world)
    {
        var h = FnvOffset;
        h = Mix(h, (ulong)world.Tick);
        foreach (var u in world.Units) // List order is stable → deterministic
        {
            h = Mix(h, (ulong)u.Id);
            h = Mix(h, (ulong)u.OwnerId);
            h = Mix(h, (ulong)u.Position.X.Raw);
            h = Mix(h, (ulong)u.Position.Y.Raw);
            h = Mix(h, (ulong)u.Hp);
        }
        return h;
    }

    private static ulong Mix(ulong h, ulong value)
    {
        for (int i = 0; i < 8; i++)
        {
            h ^= (value >> (i * 8)) & 0xFF;
            h *= FnvPrime;
        }
        return h;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter StateHasherTests`
Expected: 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/StateHasher.cs tests/SimCore.Tests/StateHasherTests.cs
git commit -m "feat: FNV-1a world state hashing for determinism checks"
```

---

### Task 10: Determinism replay test (the CI guardrail)

The spec's flagship test: run a scripted scenario twice in fresh worlds, assert identical hashes every tick. This test protects multiplayer forever — it must run in CI on every commit from now on.

**Files:**
- Test: `tests/SimCore.Tests/DeterminismTests.cs`

- [ ] **Step 1: Write the test (it should pass immediately — that's the point; it exists to catch future regressions)**

```csharp
// tests/SimCore.Tests/DeterminismTests.cs
using System.Collections.Generic;
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class DeterminismTests
{
    /// <summary>Builds the standard scenario: 20 units, walls, scripted move orders.</summary>
    private static (SimWorld world, Dictionary<int, List<Command>> script) Scenario()
    {
        var map = new MapGrid(40, 40);
        for (int y = 5; y < 35; y++) map.SetPassable(20, y, false);
        var w = new SimWorld(map, seed: 1234);

        var ids = new List<int>();
        for (int i = 0; i < 20; i++)
            ids.Add(w.SpawnUnit(i % 2, w.Map.CellCenter(2 + i % 5, 2 + i / 5), Fix.FromFraction(2, 5), 60));

        var script = new Dictionary<int, List<Command>>
        {
            [0] = new() { new MoveCommand(0, ids.FindAll(i => w.GetUnit(i)!.OwnerId == 0).ToArray(), w.Map.CellCenter(35, 35)) },
            [50] = new() { new MoveCommand(1, ids.FindAll(i => w.GetUnit(i)!.OwnerId == 1).ToArray(), w.Map.CellCenter(35, 2)) },
            [120] = new() { new MoveCommand(0, new[] { ids[0], ids[2] }, w.Map.CellCenter(2, 38)) },
        };
        return (w, script);
    }

    [Fact]
    public void Same_Script_Produces_Identical_Hash_Every_Tick()
    {
        var (a, scriptA) = Scenario();
        var (b, scriptB) = Scenario();
        var empty = new List<Command>();

        for (int t = 0; t < 500; t++)
        {
            a.Step(scriptA.TryGetValue(t, out var ca) ? ca : empty);
            b.Step(scriptB.TryGetValue(t, out var cb) ? cb : empty);
            Assert.True(StateHasher.Hash(a) == StateHasher.Hash(b),
                $"Desync at tick {t}: worlds diverged.");
        }
    }

    [Fact]
    public void Replaying_After_Full_Run_Matches_Recorded_Hashes()
    {
        var (a, script) = Scenario();
        var empty = new List<Command>();
        var recorded = new ulong[500];
        for (int t = 0; t < 500; t++)
        {
            a.Step(script.TryGetValue(t, out var c) ? c : empty);
            recorded[t] = StateHasher.Hash(a);
        }

        var (b, script2) = Scenario();
        for (int t = 0; t < 500; t++)
        {
            b.Step(script2.TryGetValue(t, out var c) ? c : empty);
            Assert.Equal(recorded[t], StateHasher.Hash(b));
        }
    }
}
```

- [ ] **Step 2: Run the determinism tests**

Run: `dotnet test --filter DeterminismTests`
Expected: 2 tests PASS. If they FAIL, there is a real nondeterminism bug in SimCore — debug it now (likely suspects: iteration over a `Dictionary`, float usage, or unseeded randomness). Do not weaken the test.

- [ ] **Step 3: Run the entire suite**

Run: `dotnet test`
Expected: all tests across all tasks PASS (≈35 tests).

- [ ] **Step 4: Commit**

```bash
git add tests/SimCore.Tests/DeterminismTests.cs
git commit -m "test: 500-tick replay determinism guardrail"
```

---

### Task 11: GitHub Actions CI (optional if no remote yet — set up locally anyway)

**Files:**
- Create: `.github/workflows/ci.yml`

- [ ] **Step 1: Write the workflow**

```yaml
name: CI
on: [push, pull_request]
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet test --configuration Release
```

- [ ] **Step 2: Verify the same command works locally**

Run: `dotnet test --configuration Release`
Expected: all tests PASS in Release mode (catches Release-only JIT differences early — important for a determinism-sensitive codebase).

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: run test suite on push"
```

---

## Done Criteria

- `dotnet test --configuration Release` passes with ~35 tests including the 500-tick determinism replay.
- SimCore has zero Godot references (`grep -ri godot src/SimCore` → no hits).
- All sim math is `Fix`/`FixVec`/`int` — no `float`/`double` in `src/SimCore` outside `Fix.ToString()`.

**Next plan:** Combat, economy & buildings (plan 2 of 5) — adds component-bag units (the spec's ECS direction), attacks, resources, supply, production, and fog of war on top of this foundation.

## Plan-2 Inputs (carried forward from code reviews of this plan — STATUS: COMPLETE, all 11 tasks merged)

Findings the reviewers flagged as fine-for-now but load-bearing for plan 2:

1. **Stale flow fields vs. mutable passability** — `MoveUnits` never re-validates passability after a field is computed. When buildings change passability mid-game, add a passability version counter that invalidates `Unit.Path` (or per-step `IsPassable` checks).
2. **StateHasher coverage convention** — hash currently covers Tick/Id/OwnerId/Position/Hp only. Every new sim field (resources, supply, queues, cooldowns) must be added; establish a convention (components hash themselves) and add `DeterministicRandom` state (needs a `State` accessor) once any system consumes the RNG.
3. **Unit removal API** — combat needs death; removal must preserve `_units` list order and `_byId` sync. Add an explicit `SimWorld` method early.
4. **Mutable `Unit` exposure** — `SimWorld.Units` returns mutable Units; the "commands are the only mutation path" rule is by-convention, not type-enforced. Consider internal setters / read-only snapshots before the presentation layer (plan 4) consumes it.
5. **`FixVec.Length()` underflow** — vectors with magnitude < 2^-8 yield Length 0 / Normalized Zero (LengthSquared truncates 16 fractional bits pre-sqrt). Harmless for tile-scale movement; fix via a raw-precision path before sub-tile combat math (separation forces, tight melee ranges) consumes it.
6. **`DeterministicRandom.NextInt`** — inverted/empty ranges fail silently/oddly; add a guard before combat variance uses it.
7. **Flow-field caching** — fields are computed per command, not cached per target per tick batch; revisit if command volume or map size grows.
