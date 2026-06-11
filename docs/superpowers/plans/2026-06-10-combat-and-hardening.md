# Combat & Hardening Implementation Plan (Plan 2a)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add unit combat (weapons, attack orders, attack-move, death) to the deterministic sim core, and pay down all seven carried-forward review items from plan 1.

**Architecture:** Combat is a new system inside `SimWorld` (a `partial class` file, `SimWorld.Combat.cs`) running between command application and movement: cooldowns tick down, attack-movers acquire targets deterministically, in-range attackers deal damage, chasers follow cached flow fields. Death removal preserves list order. `StateHasher` is expanded to cover ALL mutable sim state (the new convention), and the golden hash constant is re-pinned whenever behavior intentionally changes.

**Tech Stack:** .NET 8, xUnit, existing SimCore (Fix/FixVec, MapGrid, SimWorld, FlowField, StateHasher). PATH note: if `dotnet` is not found, run `$env:Path += ';C:\Program Files\dotnet'`.

**Plan sequence update** (phase 1 is now 6 plans): 1 sim core ✅ → **2a combat & hardening (this plan)** → 2b economy/buildings/fog → 3 faction pack system → 4 Godot presentation → 5 CPU opponent & match flow.

**Golden-hash policy reminder:** `DeterminismTests.Final_Hash_Matches_Golden_Constant` pins sim behavior. Tasks that intentionally change behavior or the hash function include a re-pin step: run the test, copy the actual value from the failure message into `GoldenFinalHash`, re-run to confirm, commit together. Never re-pin to silence a failure you can't explain.

---

### Task 1: RNG hardening — argument guard + State accessor

Plan-1 review items: inverted/empty `NextInt` ranges fail silently; RNG state has no accessor so `StateHasher` can't cover it (needed by Task 7).

**Files:**
- Modify: `src/SimCore/DeterministicRandom.cs`
- Test: `tests/SimCore.Tests/DeterministicRandomTests.cs` (append tests)

- [ ] **Step 1: Write the failing tests** — append inside the existing `DeterministicRandomTests` class:

```csharp
    [Fact]
    public void NextInt_Throws_On_Empty_Range()
    {
        var r = new DeterministicRandom(7);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => r.NextInt(5, 5));
    }

    [Fact]
    public void NextInt_Throws_On_Inverted_Range()
    {
        var r = new DeterministicRandom(7);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => r.NextInt(10, 5));
    }

    [Fact]
    public void State_Changes_After_Draw_And_Is_Seed_Deterministic()
    {
        var a = new DeterministicRandom(42);
        var b = new DeterministicRandom(42);
        var before = a.State;
        a.NextUInt();
        Assert.NotEqual(before, a.State);
        b.NextUInt();
        Assert.Equal(a.State, b.State);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter DeterministicRandomTests`
Expected: compilation FAILS (`State` does not exist) — that's the red step.

- [ ] **Step 3: Implement** — in `src/SimCore/DeterministicRandom.cs`, add the `State` property and replace `NextInt`:

```csharp
    /// <summary>Internal state, exposed for state hashing (desync detection). Never use for logic.</summary>
    public ulong State => _state;

    /// <summary>Returns value in [minInclusive, maxExclusive). Throws on empty/inverted range.</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new System.ArgumentOutOfRangeException(nameof(maxExclusive),
                $"Range [{minInclusive}, {maxExclusive}) is empty or inverted.");
        return minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: 47 total passing (44 + 3 new).

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/DeterministicRandom.cs tests/SimCore.Tests/DeterministicRandomTests.cs
git commit -m "fix: guard NextInt ranges, expose RNG state for hashing"
```

---

### Task 2: FixVec.Length raw precision (underflow fix)

Plan-1 review item: `Length()` computed `Sqrt(LengthSquared())`, and `LengthSquared` truncates 16 fractional bits — vectors with magnitude < 2⁻⁸ collapse to zero length. Combat range math needs sub-tile precision. Fix: compute the sqrt on the un-truncated 128-bit sum of squared raws. Math: `X.Raw = x·2¹⁶`, so `X.Raw² + Y.Raw² = |v|²·2³²` and `sqrt` of that is `|v|·2¹⁶` — exactly the raw representation of the length. No shift needed, no precision lost.

**Files:**
- Modify: `src/SimCore/Math/Fix.cs` (extract `IntegerSqrt`)
- Modify: `src/SimCore/Math/FixVec.cs` (rewrite `Length()`)
- Test: `tests/SimCore.Tests/FixVecTests.cs` (append tests)
- Modify: `tests/SimCore.Tests/DeterminismTests.cs` (re-pin golden constant if movement rounding changed)

- [ ] **Step 1: Write the failing tests** — append inside `FixVecTests`:

```csharp
    [Fact]
    public void Length_Preserves_Tiny_Vectors()
    {
        // magnitude 100 raw units ≈ 0.0015 — old code truncated this to zero
        var v = new FixVec(new Fix(100), Fix.Zero);
        Assert.Equal(100L, v.Length().Raw);
    }

    [Fact]
    public void Normalized_Tiny_Vector_Is_Unit_Length()
    {
        var v = new FixVec(new Fix(100), Fix.Zero);
        Assert.Equal(Fix.One, v.Normalized().X);
        Assert.Equal(Fix.Zero, v.Normalized().Y);
    }

    [Fact]
    public void Length_3_4_Still_Exactly_5()
    {
        var v = new FixVec(Fix.FromInt(3), Fix.FromInt(4));
        Assert.Equal(Fix.FromInt(5), v.Length());
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FixVecTests`
Expected: `Length_Preserves_Tiny_Vectors` and `Normalized_Tiny_Vector_Is_Unit_Length` FAIL (length comes back 0). The 3-4-5 test passes already.

- [ ] **Step 3: Implement.** In `src/SimCore/Math/Fix.cs`, replace the `Sqrt` method with:

```csharp
    /// <summary>Integer Newton's method on the raw value; deterministic.</summary>
    public static Fix Sqrt(Fix v)
    {
        if (v.Raw <= 0) return Zero;
        // sqrt(raw * 2^16) gives the Q48.16 root of the Q48.16 input
        return new Fix(IntegerSqrt((System.UInt128)(ulong)v.Raw << FractionalBits));
    }

    /// <summary>floor(sqrt(n)) via integer Newton's method; deterministic.</summary>
    internal static long IntegerSqrt(System.UInt128 n)
    {
        if (n == 0) return 0;
        System.UInt128 x = n, y = (x + 1) / 2;
        while (y < x) { x = y; y = (x + n / x) / 2; }
        return (long)(ulong)x;
    }
```

In `src/SimCore/Math/FixVec.cs`, replace `Length()`:

```csharp
    /// <summary>Exact length via 128-bit sum of squared raws — no fractional truncation.</summary>
    public Fix Length()
    {
        var sum = (System.Int128)X.Raw * X.Raw + (System.Int128)Y.Raw * Y.Raw;
        return new Fix(Fix.IntegerSqrt((System.UInt128)sum));
    }
```

(`LengthSquared()` keeps its existing truncating behavior — it's used for tile-scale range comparisons where the truncation is irrelevant; the doc trail is this comment.)

- [ ] **Step 4: Run the full suite and re-pin the golden hash if needed**

Run: `dotnet test`
Expected: the three new tests PASS. `Final_Hash_Matches_Golden_Constant` may FAIL because `Normalized()` (used by movement) now divides by a more precise length — this is an intentional behavior change. If it fails: copy the actual hash from the failure message into `GoldenFinalHash` in `tests/SimCore.Tests/DeterminismTests.cs`, re-run, confirm all tests pass. If it passes unchanged, no edit needed.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Math/Fix.cs src/SimCore/Math/FixVec.cs tests/SimCore.Tests/FixVecTests.cs tests/SimCore.Tests/DeterminismTests.cs
git commit -m "fix: exact FixVec.Length via 128-bit raw math (re-pin golden hash)"
```

---

### Task 3: Unit death & removal

Plan-1 review item: no removal API. Units with `Hp <= 0` are removed at the end of every `Step`, preserving the relative order of survivors (list order = determinism contract) and keeping `_byId` in sync.

**Files:**
- Modify: `src/SimCore/Sim/SimWorld.cs` (`Step` + new `RemoveDead`)
- Test: `tests/SimCore.Tests/UnitDeathTests.cs`

- [ ] **Step 1: Write the failing tests** at `tests/SimCore.Tests/UnitDeathTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class UnitDeathTests
{
    private static SimWorld NewWorld() => new(new MapGrid(16, 16), seed: 1);

    [Fact]
    public void Dead_Units_Are_Removed_After_Step()
    {
        var w = NewWorld();
        var id = w.SpawnUnit(0, FixVec.FromInts(1, 1), Fix.FromInt(1), 10);
        w.GetUnit(id)!.Hp = 0;
        w.Step(System.Array.Empty<Command>());
        Assert.Null(w.GetUnit(id));
        Assert.Empty(w.Units);
    }

    [Fact]
    public void Survivor_Order_Is_Preserved()
    {
        var w = NewWorld();
        var a = w.SpawnUnit(0, FixVec.FromInts(1, 1), Fix.FromInt(1), 10);
        var b = w.SpawnUnit(0, FixVec.FromInts(2, 2), Fix.FromInt(1), 10);
        var c = w.SpawnUnit(0, FixVec.FromInts(3, 3), Fix.FromInt(1), 10);
        w.GetUnit(b)!.Hp = -5;
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(new[] { a, c }, System.Linq.Enumerable.Select(w.Units, u => u.Id));
    }

    [Fact]
    public void Commands_To_Dead_Ids_Are_Ignored()
    {
        var w = NewWorld();
        var id = w.SpawnUnit(0, FixVec.FromInts(1, 1), Fix.FromInt(1), 10);
        w.GetUnit(id)!.Hp = 0;
        w.Step(System.Array.Empty<Command>());
        // must not throw
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, FixVec.FromInts(5, 5)) });
        Assert.Equal(2, w.Tick);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter UnitDeathTests`
Expected: first two tests FAIL (dead unit still present); third passes already (existing null check).

- [ ] **Step 3: Implement.** In `src/SimCore/Sim/SimWorld.cs`, update `Step` and add `RemoveDead`:

```csharp
    /// <summary>Advance one tick. Commands are applied first, then systems run in fixed order.</summary>
    public void Step(IReadOnlyList<Command> commands)
    {
        foreach (var cmd in commands) Apply(cmd);
        MoveUnits();
        RemoveDead();
        Tick++;
    }

    /// <summary>Reverse-index removal preserves survivor order (list order = determinism contract).</summary>
    private void RemoveDead()
    {
        for (int i = _units.Count - 1; i >= 0; i--)
        {
            if (_units[i].Hp > 0) continue;
            _byId.Remove(_units[i].Id);
            _units.RemoveAt(i);
        }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all passing (50 total). The determinism scenario has no deaths, so the golden hash is unaffected.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/SimWorld.cs tests/SimCore.Tests/UnitDeathTests.cs
git commit -m "feat: dead units removed each tick, survivor order preserved"
```

---

### Task 4: Passability versioning, path invalidation, per-step flow-field cache

Plan-1 review items: flow fields go stale when passability changes (buildings in plan 2b will change it), and fields are recomputed per command instead of cached per tick batch.

**Files:**
- Modify: `src/SimCore/Sim/MapGrid.cs` (add `Version`, change `SetPassable`)
- Modify: `src/SimCore/Sim/Unit.cs` (add `PathVersion`)
- Modify: `src/SimCore/Sim/SimWorld.cs` (cache + reroute)
- Test: `tests/SimCore.Tests/PathInvalidationTests.cs`

- [ ] **Step 1: Write the failing tests** at `tests/SimCore.Tests/PathInvalidationTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class PathInvalidationTests
{
    [Fact]
    public void Version_Increments_Only_On_Real_Change()
    {
        var g = new MapGrid(8, 8);
        var v0 = g.Version;
        g.SetPassable(3, 3, true);   // already true — no-op
        Assert.Equal(v0, g.Version);
        g.SetPassable(3, 3, false);  // real change
        Assert.Equal(v0 + 1, g.Version);
        g.SetPassable(-1, 0, false); // out of bounds — no-op
        Assert.Equal(v0 + 1, g.Version);
    }

    [Fact]
    public void Unit_Reroutes_When_Wall_Appears_Mid_Walk()
    {
        var map = new MapGrid(12, 12);
        var w = new SimWorld(map, seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(1, 5), Fix.FromFraction(1, 2), 50);
        var target = w.Map.CellCenter(10, 5);
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, target) });

        // after 4 ticks (unit ~2 cells in), drop a wall across its straight path, gap at y=11
        for (int i = 0; i < 4; i++) w.Step(System.Array.Empty<Command>());
        for (int y = 0; y < 11; y++) map.SetPassable(6, y, false);

        for (int i = 0; i < 300 && w.GetUnit(id)!.HasMoveOrder; i++)
        {
            w.Step(System.Array.Empty<Command>());
            var (px, py) = w.Map.WorldToCell(w.GetUnit(id)!.Position);
            Assert.True(map.IsPassable(px, py), $"tick {i}: unit inside impassable cell ({px},{py})");
        }
        Assert.False(w.GetUnit(id)!.HasMoveOrder);
        Assert.Equal(target, w.GetUnit(id)!.Position);
    }

    [Fact]
    public void Unit_Cancels_When_Target_Becomes_Unreachable()
    {
        var map = new MapGrid(12, 12);
        var w = new SimWorld(map, seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(1, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, w.Map.CellCenter(10, 5)) });
        for (int y = 0; y < 12; y++) map.SetPassable(6, y, false); // full wall
        for (int i = 0; i < 50 && w.GetUnit(id)!.HasMoveOrder; i++) w.Step(System.Array.Empty<Command>());
        Assert.False(w.GetUnit(id)!.HasMoveOrder);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter PathInvalidationTests`
Expected: compilation FAILS (`MapGrid.Version` doesn't exist).

- [ ] **Step 3: Implement.**

In `src/SimCore/Sim/MapGrid.cs`, add a `Version` property and replace `SetPassable`:

```csharp
    /// <summary>Bumped on every real passability change; consumers invalidate cached paths against it.</summary>
    public int Version { get; private set; }

    public void SetPassable(int x, int y, bool value)
    {
        if (!InBounds(x, y)) return;
        var i = y * Width + x;
        if (_passable[i] == value) return;
        _passable[i] = value;
        Version++;
    }
```

In `src/SimCore/Sim/Unit.cs`, add below `Path`:

```csharp
    public int PathVersion { get; set; } // MapGrid.Version when Path was computed
```

In `src/SimCore/Sim/SimWorld.cs`:

Add a field next to `_byId`:

```csharp
    private readonly Dictionary<(int, int), FlowField> _fieldCache = new(); // lookup only — never iterated
```

Add this method:

```csharp
    /// <summary>Per-tick flow-field cache: one Compute per target cell per Step.</summary>
    private FlowField GetField(int targetCellX, int targetCellY)
    {
        if (!_fieldCache.TryGetValue((targetCellX, targetCellY), out var f))
        {
            f = FlowField.Compute(Map, targetCellX, targetCellY);
            _fieldCache[(targetCellX, targetCellY)] = f;
        }
        return f;
    }
```

In `Step`, clear the cache first:

```csharp
    public void Step(IReadOnlyList<Command> commands)
    {
        _fieldCache.Clear();
        foreach (var cmd in commands) Apply(cmd);
        MoveUnits();
        RemoveDead();
        Tick++;
    }
```

In `Apply`'s `MoveCommand` case, use the cache and stamp the version:

```csharp
            case MoveCommand mv:
                var (tx, ty) = Map.WorldToCell(mv.Target);
                var field = GetField(tx, ty);
                foreach (var id in mv.UnitIds)
                {
                    var u = GetUnit(id);
                    if (u is null || u.OwnerId != mv.PlayerId) continue;
                    u.HasMoveOrder = true;
                    u.MoveTarget = mv.Target;
                    u.Path = field;
                    u.PathVersion = Map.Version;
                }
                break;
```

In `MoveUnits`, refresh stale paths at the top of the non-final-cell branch:

```csharp
            else
            {
                if (u.Path is null || u.PathVersion != Map.Version)
                {
                    var (rtx, rty) = Map.WorldToCell(u.MoveTarget);
                    u.Path = GetField(rtx, rty);
                    u.PathVersion = Map.Version;
                }
                var (dx, dy) = u.Path.DirectionAt(cx, cy);
                if (dx == 0 && dy == 0) { u.HasMoveOrder = false; u.Path = null; continue; } // unreachable
                step = Map.CellCenter(cx + dx, cy + dy) - u.Position;
            }
```

(This also removes the `u.Path!` null-forgiving operator flagged in review — null path now self-heals.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all passing (53 total). The determinism scenario never mutates passability mid-run and cached fields are identical to recomputed ones, so the golden hash is unaffected.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/MapGrid.cs src/SimCore/Sim/Unit.cs src/SimCore/Sim/SimWorld.cs tests/SimCore.Tests/PathInvalidationTests.cs
git commit -m "feat: passability versioning invalidates paths; per-tick flow-field cache"
```

---

### Task 5: Weapon component and AttackCommand

Combat core: a `Weapon` component, an explicit attack order, chase-when-out-of-range, damage on cooldown cadence, target death handling. Combat logic lives in a new partial-class file to keep `SimWorld.cs` focused.

**Files:**
- Create: `src/SimCore/Sim/Weapon.cs`
- Create: `src/SimCore/Sim/SimWorld.Combat.cs`
- Modify: `src/SimCore/Sim/Unit.cs` (add `Weapon`, `AttackTargetId`)
- Modify: `src/SimCore/Sim/Commands.cs` (add `AttackCommand`)
- Modify: `src/SimCore/Sim/SimWorld.cs` (declare `partial`, `SpawnUnit` weapon param, `Apply` case, `Step` calls `UpdateCombat`)
- Test: `tests/SimCore.Tests/CombatTests.cs`

- [ ] **Step 1: Write the failing tests** at `tests/SimCore.Tests/CombatTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class CombatTests
{
    private static Weapon TestWeapon() =>
        new() { Damage = 10, Range = Fix.FromInt(2), CooldownTicks = 4 };

    private static SimWorld NewWorld() => new(new MapGrid(20, 20), seed: 1);

    [Fact]
    public void In_Range_Attack_Deals_Damage_On_Cooldown_Cadence()
    {
        var w = NewWorld();
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var victim = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 25);

        w.Step(new Command[] { new AttackCommand(0, new[] { attacker }, victim) });
        Assert.Equal(15, w.GetUnit(victim)!.Hp);          // hit on tick 1

        for (int i = 0; i < 3; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(15, w.GetUnit(victim)!.Hp);          // cooling down (ticks 2-4)

        w.Step(System.Array.Empty<Command>());
        Assert.Equal(5, w.GetUnit(victim)!.Hp);           // second hit on tick 5
    }

    [Fact]
    public void Target_Death_Removes_Unit_And_Clears_Attacker_Target()
    {
        var w = NewWorld();
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var victim = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 10);

        w.Step(new Command[] { new AttackCommand(0, new[] { attacker }, victim) });
        Assert.Null(w.GetUnit(victim));                   // 10 dmg kills, removed same tick
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.GetUnit(attacker)!.AttackTargetId);
    }

    [Fact]
    public void Out_Of_Range_Attacker_Chases_Target()
    {
        var w = NewWorld();
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(2, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var victim = w.SpawnUnit(1, w.Map.CellCenter(15, 5), Fix.FromFraction(1, 2), 200);

        var startDist = (w.GetUnit(victim)!.Position - w.GetUnit(attacker)!.Position).Length();
        w.Step(new Command[] { new AttackCommand(0, new[] { attacker }, victim) });
        for (int i = 0; i < 10; i++) w.Step(System.Array.Empty<Command>());
        var endDist = (w.GetUnit(victim)!.Position - w.GetUnit(attacker)!.Position).Length();
        Assert.True(endDist < startDist, "attacker should close distance");

        for (int i = 0; i < 60; i++) w.Step(System.Array.Empty<Command>());
        Assert.True(w.GetUnit(victim)!.Hp < 200, "chaser should eventually reach range and deal damage");
    }

    [Fact]
    public void Cannot_Attack_Own_Unit()
    {
        var w = NewWorld();
        var a = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var b = w.SpawnUnit(0, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new AttackCommand(0, new[] { a }, b) });
        Assert.Equal(50, w.GetUnit(b)!.Hp);
        Assert.Equal(0, w.GetUnit(a)!.AttackTargetId);
    }

    [Fact]
    public void Unit_Without_Weapon_Ignores_Attack_Command()
    {
        var w = NewWorld();
        var pacifist = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50);
        var victim = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new AttackCommand(0, new[] { pacifist }, victim) });
        Assert.Equal(50, w.GetUnit(victim)!.Hp);
        Assert.Equal(0, w.GetUnit(pacifist)!.AttackTargetId);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter CombatTests`
Expected: compilation FAILS (`Weapon`, `AttackCommand`, `AttackTargetId`, `SpawnUnit` weapon overload don't exist).

- [ ] **Step 3: Implement.**

Create `src/SimCore/Sim/Weapon.cs`:

```csharp
using SimCore.Math;

namespace SimCore.Sim;

/// <summary>Combat component. CooldownRemaining is mutable sim state — it is hashed (Task 7).</summary>
public sealed class Weapon
{
    public int Damage { get; init; }
    public Fix Range { get; init; }
    public int CooldownTicks { get; init; }
    public int CooldownRemaining { get; set; }
}
```

In `src/SimCore/Sim/Unit.cs`, add:

```csharp
    public Weapon? Weapon { get; set; }
    public int AttackTargetId { get; set; } // 0 = no target
```

In `src/SimCore/Sim/Commands.cs`, add:

```csharp
public sealed record AttackCommand(int PlayerId, int[] UnitIds, int TargetId) : Command(PlayerId);
```

In `src/SimCore/Sim/SimWorld.cs`:
- Change the class declaration to `public sealed partial class SimWorld`.
- Change `SpawnUnit` to accept a weapon:

```csharp
    public int SpawnUnit(int ownerId, FixVec pos, Fix speedPerTick, int hp, Weapon? weapon = null)
    {
        var u = new Unit { Id = _nextId++, OwnerId = ownerId, Position = pos, SpeedPerTick = speedPerTick, Hp = hp, Weapon = weapon };
        _units.Add(u);
        _byId[u.Id] = u;
        return u.Id;
    }
```

- In `Step`, call combat between commands and movement:

```csharp
    public void Step(IReadOnlyList<Command> commands)
    {
        _fieldCache.Clear();
        foreach (var cmd in commands) Apply(cmd);
        UpdateCombat();
        MoveUnits();
        RemoveDead();
        Tick++;
    }
```

- In `Apply`, add the case:

```csharp
            case AttackCommand atk:
                foreach (var id in atk.UnitIds)
                {
                    var u = GetUnit(id);
                    if (u is null || u.OwnerId != atk.PlayerId || u.Weapon is null) continue;
                    var t = GetUnit(atk.TargetId);
                    if (t is null || t.OwnerId == atk.PlayerId) continue;
                    u.AttackTargetId = atk.TargetId;
                    u.HasMoveOrder = false;
                    u.Path = null;
                }
                break;
```

Create `src/SimCore/Sim/SimWorld.Combat.cs`:

```csharp
using SimCore.Math;

namespace SimCore.Sim;

public sealed partial class SimWorld
{
    /// <summary>Runs after commands, before movement. Iteration over _units (stable order):
    /// earlier-spawned units strike first within a tick — deterministic first-mover rule.</summary>
    private void UpdateCombat()
    {
        // Pass 1: cooldowns tick down for everyone.
        foreach (var u in _units)
            if (u.Weapon is { CooldownRemaining: > 0 } w) w.CooldownRemaining--;

        // Pass 2: fight or chase.
        foreach (var u in _units)
        {
            if (u.Weapon is null || u.AttackTargetId == 0) continue;
            var target = GetUnit(u.AttackTargetId);
            if (target is null || target.Hp <= 0) { u.AttackTargetId = 0; continue; }

            var delta = target.Position - u.Position;
            if (delta.LengthSquared() <= u.Weapon.Range * u.Weapon.Range)
            {
                u.HasMoveOrder = false;
                u.Path = null;
                if (u.Weapon.CooldownRemaining == 0)
                {
                    target.Hp -= u.Weapon.Damage;
                    u.Weapon.CooldownRemaining = u.Weapon.CooldownTicks;
                }
            }
            else
            {
                // chase: follow a (cached) field toward the target's current cell
                var (tx, ty) = Map.WorldToCell(target.Position);
                u.HasMoveOrder = true;
                u.MoveTarget = target.Position;
                u.Path = GetField(tx, ty);
                u.PathVersion = Map.Version;
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all passing (58 total). The determinism scenario has no weapons yet, the `UpdateCombat` call is a no-op there, and the hash function is unchanged — golden constant unaffected.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/Weapon.cs src/SimCore/Sim/SimWorld.Combat.cs src/SimCore/Sim/Unit.cs src/SimCore/Sim/Commands.cs src/SimCore/Sim/SimWorld.cs tests/SimCore.Tests/CombatTests.cs
git commit -m "feat: weapons, attack command, chase and cooldown combat"
```

---

### Task 6: Attack-move with deterministic target acquisition

The bread-and-butter RTS order: move toward a point, engage anything hostile encountered on the way, resume after the kill, stop on arrival.

**Files:**
- Modify: `src/SimCore/Sim/Unit.cs` (add `IsAttackMoving`, `AttackMoveDest`)
- Modify: `src/SimCore/Sim/Commands.cs` (add `AttackMoveCommand`)
- Modify: `src/SimCore/Sim/SimWorld.cs` (`Apply` cases: new command + plain move cancels attack state)
- Modify: `src/SimCore/Sim/SimWorld.Combat.cs` (acquisition + resume logic)
- Test: `tests/SimCore.Tests/AttackMoveTests.cs`

- [ ] **Step 1: Write the failing tests** at `tests/SimCore.Tests/AttackMoveTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class AttackMoveTests
{
    private static Weapon TestWeapon() =>
        new() { Damage = 10, Range = Fix.FromInt(2), CooldownTicks = 2 };

    [Fact]
    public void AttackMove_Engages_Enemy_En_Route_Then_Arrives()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var soldier = w.SpawnUnit(0, w.Map.CellCenter(1, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var bystander = w.SpawnUnit(1, w.Map.CellCenter(8, 5), Fix.FromFraction(1, 2), 20);
        var dest = w.Map.CellCenter(15, 5);

        w.Step(new Command[] { new AttackMoveCommand(0, new[] { soldier }, dest) });
        for (int i = 0; i < 300 && (w.GetUnit(soldier)!.IsAttackMoving || w.GetUnit(soldier)!.HasMoveOrder); i++)
            w.Step(System.Array.Empty<Command>());

        Assert.Null(w.GetUnit(bystander));                       // killed on the way
        Assert.Equal(dest, w.GetUnit(soldier)!.Position);        // then continued to destination
        Assert.False(w.GetUnit(soldier)!.IsAttackMoving);
    }

    [Fact]
    public void AttackMove_Without_Enemies_Just_Moves()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var soldier = w.SpawnUnit(0, w.Map.CellCenter(1, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var dest = w.Map.CellCenter(10, 5);
        w.Step(new Command[] { new AttackMoveCommand(0, new[] { soldier }, dest) });
        for (int i = 0; i < 200 && (w.GetUnit(soldier)!.IsAttackMoving || w.GetUnit(soldier)!.HasMoveOrder); i++)
            w.Step(System.Array.Empty<Command>());
        Assert.Equal(dest, w.GetUnit(soldier)!.Position);
        Assert.False(w.GetUnit(soldier)!.IsAttackMoving);
    }

    [Fact]
    public void Acquisition_Prefers_Nearest_Then_Earliest_Spawned()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var soldier = w.SpawnUnit(0, w.Map.CellCenter(10, 10), Fix.FromFraction(1, 2), 50, TestWeapon());
        var east = w.SpawnUnit(1, w.Map.CellCenter(13, 10), Fix.FromFraction(1, 2), 100); // equidistant
        var west = w.SpawnUnit(1, w.Map.CellCenter(7, 10), Fix.FromFraction(1, 2), 100);  // equidistant

        w.Step(new Command[] { new AttackMoveCommand(0, new[] { soldier }, w.Map.CellCenter(10, 15)) });
        w.Step(System.Array.Empty<Command>());

        Assert.Equal(east, w.GetUnit(soldier)!.AttackTargetId);  // tie → earlier-spawned (lower id) wins
    }

    [Fact]
    public void Plain_Move_Cancels_Attack_State()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var soldier = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var enemy = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 500);
        w.Step(new Command[] { new AttackCommand(0, new[] { soldier }, enemy) });
        Assert.Equal(enemy, w.GetUnit(soldier)!.AttackTargetId);

        w.Step(new Command[] { new MoveCommand(0, new[] { soldier }, w.Map.CellCenter(1, 1)) });
        Assert.Equal(0, w.GetUnit(soldier)!.AttackTargetId);
        Assert.False(w.GetUnit(soldier)!.IsAttackMoving);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter AttackMoveTests`
Expected: compilation FAILS (`AttackMoveCommand`, `IsAttackMoving` don't exist).

- [ ] **Step 3: Implement.**

In `src/SimCore/Sim/Unit.cs`, add:

```csharp
    public bool IsAttackMoving { get; set; }
    public FixVec AttackMoveDest { get; set; }
```

In `src/SimCore/Sim/Commands.cs`, add:

```csharp
public sealed record AttackMoveCommand(int PlayerId, int[] UnitIds, FixVec Target) : Command(PlayerId);
```

In `src/SimCore/Sim/SimWorld.cs` `Apply`:

Add to the `MoveCommand` case body (inside the per-unit loop, after the ownership check):

```csharp
                    u.IsAttackMoving = false;
                    u.AttackTargetId = 0;
```

Add the new case:

```csharp
            case AttackMoveCommand am:
                var (amx, amy) = Map.WorldToCell(am.Target);
                var amField = GetField(amx, amy);
                foreach (var id in am.UnitIds)
                {
                    var u = GetUnit(id);
                    if (u is null || u.OwnerId != am.PlayerId) continue;
                    u.IsAttackMoving = true;
                    u.AttackMoveDest = am.Target;
                    u.AttackTargetId = 0;
                    u.HasMoveOrder = true;
                    u.MoveTarget = am.Target;
                    u.Path = amField;
                    u.PathVersion = Map.Version;
                }
                break;
```

In `src/SimCore/Sim/SimWorld.Combat.cs`, insert an acquisition/resume block at the TOP of pass 2's loop body (before the `if (u.Weapon is null || u.AttackTargetId == 0) continue;` line), and add the helper:

```csharp
        // Pass 2: fight or chase.
        foreach (var u in _units)
        {
            // Attack-move: acquire a target, or resume/finish the march.
            if (u.IsAttackMoving && u.Weapon is not null && u.AttackTargetId == 0)
            {
                var acquired = AcquireTarget(u, u.Weapon.Range + Fix.FromInt(2));
                if (acquired != 0)
                {
                    u.AttackTargetId = acquired;
                }
                else if (!u.HasMoveOrder)
                {
                    if (u.Position.Equals(u.AttackMoveDest))
                    {
                        u.IsAttackMoving = false; // arrived
                    }
                    else
                    {
                        // resume march toward original destination
                        var (rdx, rdy) = Map.WorldToCell(u.AttackMoveDest);
                        var f = GetField(rdx, rdy);
                        var (ccx, ccy) = Map.WorldToCell(u.Position);
                        var inDestCell = ccx == rdx && ccy == rdy;
                        if (!inDestCell && f.DirectionAt(ccx, ccy) == (0, 0))
                        {
                            u.IsAttackMoving = false; // destination unreachable — give up
                        }
                        else
                        {
                            u.HasMoveOrder = true;
                            u.MoveTarget = u.AttackMoveDest;
                            u.Path = f;
                            u.PathVersion = Map.Version;
                        }
                    }
                }
            }

            if (u.Weapon is null || u.AttackTargetId == 0) continue;
            // ... existing fight-or-chase body unchanged ...
        }
```

Add the helper method inside the same partial class:

```csharp
    /// <summary>Nearest living enemy within range; ties broken by spawn order (stable list iteration,
    /// strict less-than keeps the earliest). Deterministic.</summary>
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
        return best;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all passing (62 total). Determinism scenario still has no weapons or attack-moves; golden constant unaffected.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/Unit.cs src/SimCore/Sim/Commands.cs src/SimCore/Sim/SimWorld.cs src/SimCore/Sim/SimWorld.Combat.cs tests/SimCore.Tests/AttackMoveTests.cs
git commit -m "feat: attack-move with deterministic nearest-enemy acquisition"
```

---

### Task 7: StateHasher v2 — hash ALL mutable sim state (new convention)

Plan-1 review item: the hash oracle had blind spots (pending orders, RNG state) and no convention forcing new fields in. The convention, stated in a comment at the top of the class: **every mutable sim field must be hashed; add the field here and re-pin the golden constant in the same commit that adds the field.**

**Files:**
- Modify: `src/SimCore/Sim/StateHasher.cs`
- Test: `tests/SimCore.Tests/StateHasherTests.cs` (append tests)
- Modify: `tests/SimCore.Tests/DeterminismTests.cs` (re-pin golden constant)

- [ ] **Step 1: Write the failing tests** — append inside `StateHasherTests`:

```csharp
    [Fact]
    public void MoveTarget_Change_Changes_Hash()
    {
        var a = World();
        var b = World();
        b.GetUnit(1)!.MoveTarget = FixVec.FromInts(9, 9);
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Rng_Draw_Changes_Hash()
    {
        var a = World();
        var b = World();
        b.Rng.NextUInt();
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Weapon_Cooldown_Changes_Hash()
    {
        var a = World();
        var b = World();
        var weaponA = new Weapon { Damage = 5, Range = Fix.FromInt(2), CooldownTicks = 4 };
        var weaponB = new Weapon { Damage = 5, Range = Fix.FromInt(2), CooldownTicks = 4 };
        a.GetUnit(1)!.Weapon = weaponA;
        b.GetUnit(1)!.Weapon = weaponB;
        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));   // identical weapons → equal
        weaponB.CooldownRemaining = 3;
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b)); // cooldown drift → detected
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter StateHasherTests`
Expected: the three new tests FAIL (those fields aren't hashed yet); the original four still pass.

- [ ] **Step 3: Implement.** Replace the `Hash` method in `src/SimCore/Sim/StateHasher.cs`:

```csharp
    /// <summary>CONVENTION: every mutable sim field MUST be folded in here. When you add sim
    /// state, add it to this method and re-pin GoldenFinalHash in the same commit.</summary>
    public static ulong Hash(SimWorld world)
    {
        var h = FnvOffset;
        h = Mix(h, (ulong)world.Tick);
        h = Mix(h, world.Rng.State);
        foreach (var u in world.Units) // List order is stable → deterministic
        {
            h = Mix(h, (ulong)u.Id);
            h = Mix(h, (ulong)u.OwnerId);
            h = Mix(h, (ulong)u.Position.X.Raw);
            h = Mix(h, (ulong)u.Position.Y.Raw);
            h = Mix(h, (ulong)u.Hp);
            h = Mix(h, (ulong)u.SpeedPerTick.Raw);
            h = Mix(h, u.HasMoveOrder ? 1UL : 0UL);
            h = Mix(h, (ulong)u.MoveTarget.X.Raw);
            h = Mix(h, (ulong)u.MoveTarget.Y.Raw);
            h = Mix(h, u.IsAttackMoving ? 1UL : 0UL);
            h = Mix(h, (ulong)u.AttackMoveDest.X.Raw);
            h = Mix(h, (ulong)u.AttackMoveDest.Y.Raw);
            h = Mix(h, (ulong)u.AttackTargetId);
            if (u.Weapon is { } w)
            {
                h = Mix(h, (ulong)w.Damage);
                h = Mix(h, (ulong)w.Range.Raw);
                h = Mix(h, (ulong)w.CooldownTicks);
                h = Mix(h, (ulong)w.CooldownRemaining);
            }
            else
            {
                h = Mix(h, 0UL); // weapon-absent marker keeps record lengths unambiguous
            }
        }
        return h;
    }
```

(`Unit.PathVersion` and `Unit.Path` are deliberately NOT hashed: they are derived caches, recomputed from hashed state — note this in a comment.)

- [ ] **Step 4: Run the suite and re-pin the golden constant**

Run: `dotnet test`
Expected: the three new tests PASS; `Final_Hash_Matches_Golden_Constant` FAILS (hash function changed — expected). Copy the actual value from the failure message into `GoldenFinalHash` in `tests/SimCore.Tests/DeterminismTests.cs`, re-run `dotnet test`, expect all 65 passing in both Debug and `--configuration Release`.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/StateHasher.cs tests/SimCore.Tests/StateHasherTests.cs tests/SimCore.Tests/DeterminismTests.cs
git commit -m "feat: StateHasher covers all mutable sim state incl. RNG (re-pin golden hash)"
```

---

### Task 8: Determinism scenario v2 — combat in the replay guardrail

The 500-tick guardrail currently exercises only movement. Combat (chase, acquisition, cooldowns, death/removal) is now sim behavior and must be inside the determinism net.

**Files:**
- Modify: `tests/SimCore.Tests/DeterminismTests.cs` (scenario + golden re-pin)

- [ ] **Step 1: Replace the `Scenario()` helper** in `tests/SimCore.Tests/DeterminismTests.cs`:

```csharp
    /// <summary>Standard scenario v2: 20 armed units, walls, movement, then a pitched battle.</summary>
    private static (SimWorld world, Dictionary<int, List<Command>> script) Scenario()
    {
        var map = new MapGrid(40, 40);
        for (int y = 5; y < 35; y++) map.SetPassable(20, y, false);
        var w = new SimWorld(map, seed: 1234);

        var ids = new List<int>();
        for (int i = 0; i < 20; i++)
        {
            var weapon = new Weapon { Damage = 5, Range = Fix.FromInt(2), CooldownTicks = 8 };
            ids.Add(w.SpawnUnit(i % 2, w.Map.CellCenter(2 + i % 5, 2 + i / 5), Fix.FromFraction(2, 5), 60, weapon));
        }

        int[] Owned(int owner) => ids.FindAll(i => w.GetUnit(i)!.OwnerId == owner).ToArray();

        var script = new Dictionary<int, List<Command>>
        {
            [0] = new() { new MoveCommand(0, Owned(0), w.Map.CellCenter(35, 35)) },
            [50] = new() { new MoveCommand(1, Owned(1), w.Map.CellCenter(35, 2)) },
            [120] = new() { new MoveCommand(0, new[] { ids[0], ids[2] }, w.Map.CellCenter(2, 38)) },
            // v2: both armies attack-move into each other → acquisition, chase, cooldowns, deaths
            [200] = new() { new AttackMoveCommand(0, Owned(0), w.Map.CellCenter(35, 2)) },
            [220] = new() { new AttackMoveCommand(1, Owned(1), w.Map.CellCenter(35, 35)) },
        };
        return (w, script);
    }
```

(The two `[Fact]` test bodies and `Final_Hash_Matches_Golden_Constant` are unchanged — only the scenario and, in step 3, the constant.)

- [ ] **Step 2: Run the determinism tests**

Run: `dotnet test --filter DeterminismTests`
Expected: `Same_Script_Produces_Identical_Hash_Every_Tick` and `Replaying_After_Full_Run_Matches_Recorded_Hashes` PASS (combat is deterministic — if either FAILS, there is a real nondeterminism bug in the combat code; debug it, do not weaken the test). `Final_Hash_Matches_Golden_Constant` FAILS (scenario changed — expected).

- [ ] **Step 3: Re-pin the golden constant**

Copy the actual hash from the failure message into `GoldenFinalHash`. Run `dotnet test` (Debug) and `dotnet test --configuration Release` — expect all 65 passing in both. Sanity-check the battle actually happened: temporarily add `System.Console.WriteLine(w.Units.Count)` after a run if desired — unit count should be well below 20 (deaths occurred) — then remove it.

- [ ] **Step 4: Commit**

```bash
git add tests/SimCore.Tests/DeterminismTests.cs
git commit -m "test: determinism scenario v2 with pitched battle (re-pin golden hash)"
```

---

## Done Criteria

- `dotnet test --configuration Release` passes (~65 tests) including determinism replay with combat.
- All seven plan-1 carry-forward items addressed: ✅ NextInt guard (T1), ✅ Length underflow (T2), ✅ unit removal (T3), ✅ stale paths + field caching (T4), ✅ RNG state hashed + hasher convention (T7). The remaining item — mutable `Unit` exposure — is explicitly deferred to plan 4 (presentation), where the consumer that makes it dangerous first appears; note it in plan 4's inputs.
- `grep -ri godot src/SimCore` → no hits; no float/double outside `Fix.ToString()`.

**Next plan:** Economy, buildings & fog (plan 2b) — player resources, buildings with passability footprints (exercising Task 4's invalidation for real), production queues, supply, harvesting, and per-player vision maps.

## Plan-2b Inputs (carried forward from code reviews — STATUS: COMPLETE, merged 2026-06-10, 72 tests)

1. **Hash MapGrid passability** the moment build/destroy commands mutate it mid-run — day-one task, already tripwired in the StateHasher convention comment.
2. **Weapon instance aliasing** — Weapon is a mutable class; one instance shared across SpawnUnit calls shares cooldown state. Unit-template/faction work must clone per spawn (or move cooldown onto Unit).
3. **Fog vs omniscient combat** — AttackCommand accepts any enemy anywhere; chase reads target.Position with perfect information. Fog needs visibility gates at command application and during chase/acquisition.
4. **Scale costs** — AcquireTarget is O(attackers × units)/tick; _fieldCache accumulates one field per distinct destination on a static map. Fine at tens of units; spatial index / eviction when counts grow.
5. **SpawnUnit parameter growth** — (owner, pos, speed, hp, weapon?) won't scale to buildings/harvesters; introduce a spawn-spec/archetype record.
6. **Command boundary validation thin** — PlayerId unvalidated, UnitIds assumed non-null; harden when network/CPU-opponent layers produce commands.
7. **Leash/acquisition magic numbers** — acquisition = Range+2 and leash = Range+4 are hand-synced literals in SimWorld.Combat.cs; extract named constants on next touch. Also: explicit-attack chasers walk to a corpse's last position after a third-party kill (deterministic, undocumented); LengthSquared truncation undocumented at its definition.
8. **(Inherited, → plan 4)** Mutable Unit exposure via Units/GetUnit.
9. **Scenario nicety** — symmetric-exchange (mutual simultaneous damage) coverage dropped out of the golden trajectory in v2.1 (still pinned by its unit test); restore when the scenario next re-pins.
