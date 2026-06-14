# Faction Mechanics + Regenerating Shields Implementation Plan (Plan 3c)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `FactionDef` mechanic selector plus the regenerating-shields exemplar (per-unit shield pool that absorbs damage and regenerates), establishing the pattern for future faction mechanics.

**Architecture:** `FactionDef.Mechanic` (a `MechanicDef?`) is the selector. Units gain `ShieldHp` + `TicksSinceDamaged`. Combat damage routes through `ApplyDamage` (shield-first). A `UpdateShields` Step hook regenerates shields. Shared faction for now, read via `MechanicFor(unit)`. Tasks 1–4 hold the golden (shield state unhashed + no scenario uses it); Task 5 hashes the state + a shield scenario and re-pins once.

**Tech Stack:** .NET 8, xUnit, existing SimCore. PATH: if `dotnet` not found, run `$env:Path += ';C:\Program Files\dotnet'`.

**Spec:** `docs/superpowers/specs/2026-06-13-faction-mechanics-shields-design.md`. **Arc:** plan 3c of 4.

**Golden-hash protocol:** `Trajectory_Hash_Matches_Golden_Constant` (tests/SimCore.Tests/DeterminismTests.cs, currently `6959374437731592347UL`). Tasks 1–4 are **golden-unchanged**. The single re-pin is in Task 5. The two replay tests must never fail.

**Current state (post-3b, ~210 SimCore tests):** `FactionDef` has chained ctors — 4-arg `(id,name,units,buildings)` → 5-arg `(…,upgrades)`. `Unit` has DefId/Weapon/Shieldless fields. Combat damage in `SimWorld.Combat.cs`: `if (targetUnit is not null) targetUnit.Hp -= EffectiveDamage(u); else targetBuilding!.Hp -= EffectiveDamage(u);`. `Step` order: UpdateVision → Apply → UpdateCombat → MoveUnits → UpdateHarvest → UpdateConstruction → UpdateProduction → UpdateResearch → RemoveDead → RemoveDeadBuildings → Tick++. `Spawn` helper builds the `Unit{}`. StateHasher per-unit loop ends with patrol fields.

---

### Task 1: MechanicDef + FactionDef.Mechanic selector + Validate

**Files:**
- Modify: `src/SimCore/Sim/FactionDef.cs`
- Test: `tests/SimCore.Tests/MechanicCatalogTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SimCore.Tests/MechanicCatalogTests.cs
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class MechanicCatalogTests
{
    private static UnitSpec U() => new(40, Fix.FromFraction(1, 2), 50, 1, 20);
    private static BuildingSpec B() => new(100, 2, 2, 100, 10, CanTrain: true);
    private static FactionDef Make(MechanicDef? mech) => new("f", "F",
        units: new[] { new UnitDef("trooper", 1, "rax", new string[0], U()) },
        buildings: new[] { new BuildingDef("rax", 1, new string[0], B()) },
        upgrades: System.Array.Empty<UpgradeDef>(),
        mechanic: mech);

    [Fact]
    public void Mechanic_Defaults_Null_And_Old_Ctors_Work()
    {
        var f = new FactionDef("f", "F",
            units: new[] { new UnitDef("trooper", 1, "rax", new string[0], U()) },
            buildings: new[] { new BuildingDef("rax", 1, new string[0], B()) });
        Assert.Null(f.Mechanic);
    }

    [Fact]
    public void Shield_Mechanic_Is_Stored()
    {
        var f = Make(new MechanicDef(MechanicKind.RegeneratingShields, 20, 1, 30));
        Assert.Equal(MechanicKind.RegeneratingShields, f.Mechanic!.Kind);
        Assert.Equal(20, f.Mechanic.MaxShield);
        Assert.Equal(1, f.Mechanic.RegenPerTick);
        Assert.Equal(30, f.Mechanic.RegenDelayTicks);
        Assert.Empty(f.Validate());
    }

    [Fact]
    public void Negative_Shield_Params_Flagged()
    {
        var f = Make(new MechanicDef(MechanicKind.RegeneratingShields, -5, 1, 30));
        Assert.Contains(f.Validate(), e => e.Contains("mechanic"));
    }

    [Fact]
    public void None_Kind_With_Shield_Params_Flagged()
    {
        var f = Make(new MechanicDef(MechanicKind.None, 20, 1, 30));
        Assert.Contains(f.Validate(), e => e.Contains("mechanic"));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter MechanicCatalogTests`
Expected: compilation FAILS (`MechanicDef`/`MechanicKind`/the mechanic ctor param don't exist).

- [ ] **Step 3: Implement.** In `src/SimCore/Sim/FactionDef.cs`:

Add at the bottom (next to the other records/enums):
```csharp
public enum MechanicKind { None = 0, RegeneratingShields = 1 }

public sealed record MechanicDef(MechanicKind Kind, int MaxShield, int RegenPerTick, int RegenDelayTicks);
```

In `FactionDef`, add the property and a new chained ctor (read the current chained ctors first; add `Mechanic` and a 6-arg ctor; chain the existing 5-arg `(…,upgrades)` ctor to it with `mechanic: null`):
```csharp
    public MechanicDef? Mechanic { get; }

    // existing 5-arg ctor body: change its signature to delegate to the new 6-arg one:
    //   public FactionDef(string id, string name, IEnumerable<UnitDef> units,
    //       IEnumerable<BuildingDef> buildings, IEnumerable<UpgradeDef> upgrades)
    //       : this(id, name, units, buildings, upgrades, mechanic: null) { }
    // and add the real 6-arg ctor that does the actual field population PLUS `Mechanic = mechanic;`
```
Concretely: rename the current populating ctor to take a 6th param `MechanicDef? mechanic = null` and set `Mechanic = mechanic;`. Keep the 4-arg ctor chaining to the 5-arg (`upgrades: empty`), and have the 5-arg chain into the 6-arg (or just add `mechanic` as a defaulted last param on the existing populating ctor — simplest: the populating ctor gains `MechanicDef? mechanic = null` as its last parameter; the 4-arg chain passes nothing). Verify all existing `new FactionDef(...)` call sites still compile.

In `Validate()`, before the final `return errors;`, add:
```csharp
        if (Mechanic is { } m)
        {
            if (m.Kind == MechanicKind.None && (m.MaxShield != 0 || m.RegenPerTick != 0 || m.RegenDelayTicks != 0))
                errors.Add("mechanic kind is None but has nonzero params");
            if (m.MaxShield < 0 || m.RegenPerTick < 0 || m.RegenDelayTicks < 0)
                errors.Add($"mechanic has negative params (shield {m.MaxShield}, regen {m.RegenPerTick}, delay {m.RegenDelayTicks})");
        }
```

- [ ] **Step 4: Run full `dotnet test`** — expect +4 tests, all passing, golden unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/FactionDef.cs tests/SimCore.Tests/MechanicCatalogTests.cs
git commit -m "feat: MechanicDef + FactionDef.Mechanic selector + validation"
```

---

### Task 2: Unit shield state + spawn init + MechanicFor/HasShields

**Files:**
- Modify: `src/SimCore/Sim/Unit.cs`
- Modify: `src/SimCore/Sim/SimWorld.cs` (Spawn init; add helpers)
- Create: `src/SimCore/Sim/SimWorld.Mechanics.cs` (helpers; regen hook lands in Task 4)
- Test: `tests/SimCore.Tests/ShieldSpawnTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SimCore.Tests/ShieldSpawnTests.cs
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class ShieldSpawnTests
{
    private static readonly string[] None = System.Array.Empty<string>();

    private static FactionDef ShieldFaction(int max) => new("f", "F",
        units: System.Array.Empty<UnitDef>(),
        buildings: System.Array.Empty<BuildingDef>(),
        upgrades: System.Array.Empty<UpgradeDef>(),
        mechanic: new MechanicDef(MechanicKind.RegeneratingShields, max, 1, 10));

    [Fact]
    public void Shield_Faction_Units_Spawn_With_Full_Shield()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, faction: ShieldFaction(25));
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 2), Fix.FromInt(1), 50);
        Assert.Equal(25, w.GetUnit(id)!.ShieldHp);
    }

    [Fact]
    public void Non_Shield_Faction_Units_Spawn_With_Zero_Shield()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, faction: TestFactions.Standard);
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 2), Fix.FromInt(1), 50);
        Assert.Equal(0, w.GetUnit(id)!.ShieldHp);
    }

    [Fact]
    public void No_Faction_Units_Spawn_With_Zero_Shield()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 2), Fix.FromInt(1), 50);
        Assert.Equal(0, w.GetUnit(id)!.ShieldHp);
        Assert.Equal(0, w.GetUnit(id)!.TicksSinceDamaged);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter ShieldSpawnTests`
Expected: compilation FAILS (`Unit.ShieldHp` doesn't exist).

- [ ] **Step 3: Implement.**

In `src/SimCore/Sim/Unit.cs`, add (near the bottom of the field list):
```csharp
    public int ShieldHp { get; set; }
    public int TicksSinceDamaged { get; set; }
```

Create `src/SimCore/Sim/SimWorld.Mechanics.cs`:
```csharp
namespace SimCore.Sim;

public sealed partial class SimWorld
{
    /// <summary>The mechanic governing this unit. Today: the shared world Faction's mechanic.
    /// Structured as a per-unit accessor so it becomes per-player when packs let each player
    /// pick a faction (plan 5). Returns null when there is no mechanic.</summary>
    private MechanicDef? MechanicFor(Unit u) => Faction?.Mechanic;

    private bool HasShields(Unit u) => MechanicFor(u) is { Kind: MechanicKind.RegeneratingShields };

    /// <summary>Initial shield pool for a unit spawned under the current faction.</summary>
    private int InitialShield() =>
        Faction?.Mechanic is { Kind: MechanicKind.RegeneratingShields } m ? m.MaxShield : 0;
}
```

In `src/SimCore/Sim/SimWorld.cs` `Spawn`, set the initial shield in the `Unit{}` initializer — add `ShieldHp = InitialShield()` to the object initializer (read the current `Spawn` first; add the field alongside `SightRange = sightRange`). `TicksSinceDamaged` defaults to 0.

- [ ] **Step 4: Run full `dotnet test`** — expect +3 tests, all passing. Golden unchanged (ShieldHp not hashed yet; existing scenario uses a non-shield faction so all ShieldHp == 0, and no behavior reads it yet).

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/Unit.cs src/SimCore/Sim/SimWorld.cs src/SimCore/Sim/SimWorld.Mechanics.cs tests/SimCore.Tests/ShieldSpawnTests.cs
git commit -m "feat: Unit shield state + spawn init + MechanicFor/HasShields helpers"
```

---

### Task 3: ApplyDamage — combat routes through shields

**Files:**
- Modify: `src/SimCore/Sim/SimWorld.Mechanics.cs` (add `ApplyDamage`)
- Modify: `src/SimCore/Sim/SimWorld.Combat.cs` (use it)
- Test: `tests/SimCore.Tests/ShieldDamageTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SimCore.Tests/ShieldDamageTests.cs
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class ShieldDamageTests
{
    private static FactionDef ShieldFaction(int max) => new("f", "F",
        units: System.Array.Empty<UnitDef>(),
        buildings: System.Array.Empty<BuildingDef>(),
        upgrades: System.Array.Empty<UpgradeDef>(),
        mechanic: new MechanicDef(MechanicKind.RegeneratingShields, max, 1, 5));

    // Attacker (no-faction-mechanic irrelevant; mechanic is shared so attacker also has shields,
    // but we only inspect the victim). 1 attack of 10 dmg lands on tick 1.
    private static (SimWorld w, int atk, int vic) Setup(int shieldMax, int vicHp)
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: ShieldFaction(shieldMax));
        var weapon = new Weapon { Damage = 10, Range = Fix.FromInt(2), CooldownTicks = 1000 }; // one shot
        var atk = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, weapon);
        var vic = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), vicHp);
        return (w, atk, vic);
    }

    [Fact]
    public void Damage_Below_Shield_Only_Reduces_Shield()
    {
        var (w, atk, vic) = Setup(shieldMax: 25, vicHp: 50);
        w.Step(new Command[] { new AttackCommand(0, new[] { atk }, vic) });
        var v = w.GetUnit(vic)!;
        Assert.Equal(15, v.ShieldHp);   // 25 - 10
        Assert.Equal(50, v.Hp);         // HP untouched
        Assert.Equal(0, v.TicksSinceDamaged); // reset on hit
    }

    [Fact]
    public void Damage_Above_Shield_Spills_To_Hp()
    {
        var (w, atk, vic) = Setup(shieldMax: 4, vicHp: 50);
        w.Step(new Command[] { new AttackCommand(0, new[] { atk }, vic) });
        var v = w.GetUnit(vic)!;
        Assert.Equal(0, v.ShieldHp);    // 4 absorbed
        Assert.Equal(44, v.Hp);         // 50 - (10-4)
    }

    [Fact]
    public void Non_Shield_Faction_Damage_Goes_Straight_To_Hp()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: TestFactions.Standard);
        var weapon = new Weapon { Damage = 10, Range = Fix.FromInt(2), CooldownTicks = 1000 };
        var atk = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, weapon);
        var vic = w.SpawnUnit(1, w.Map.CellCenter(6, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new AttackCommand(0, new[] { atk }, vic) });
        Assert.Equal(40, w.GetUnit(vic)!.Hp);
        Assert.Equal(0, w.GetUnit(vic)!.ShieldHp);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter ShieldDamageTests`
Expected: the first two FAIL (damage currently goes straight to Hp; ShieldHp untouched). The non-shield test passes already.

- [ ] **Step 3: Implement.**

In `src/SimCore/Sim/SimWorld.Mechanics.cs`, add:
```csharp
    /// <summary>Applies combat damage to a unit, shield-first. Resets the damage timer
    /// so shield regen pauses. Works uniformly: a unit with no shields has ShieldHp 0,
    /// so all damage goes to Hp (identical to direct subtraction).</summary>
    private static void ApplyDamage(Unit target, int amount)
    {
        int toShield = System.Math.Min(target.ShieldHp, amount);
        target.ShieldHp -= toShield;
        target.Hp -= (amount - toShield);
        target.TicksSinceDamaged = 0;
    }
```

In `src/SimCore/Sim/SimWorld.Combat.cs`, replace the unit-damage line. Find:
```csharp
                    if (targetUnit is not null) targetUnit.Hp -= EffectiveDamage(u);
                    else targetBuilding!.Hp -= EffectiveDamage(u);
```
Replace with:
```csharp
                    if (targetUnit is not null) ApplyDamage(targetUnit, EffectiveDamage(u));
                    else targetBuilding!.Hp -= EffectiveDamage(u);
```
(Buildings keep direct HP — shields are units-only.)

- [ ] **Step 4: Run full `dotnet test`** — expect +3 tests, all passing. **Golden unchanged** — the determinism scenario uses a non-shield faction, so every unit's `ShieldHp == 0` and `ApplyDamage` subtracts exactly as before (and `TicksSinceDamaged` is unhashed). If golden changes, `ApplyDamage` isn't equivalent for the 0-shield case — debug, do NOT re-pin.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/SimWorld.Mechanics.cs src/SimCore/Sim/SimWorld.Combat.cs tests/SimCore.Tests/ShieldDamageTests.cs
git commit -m "feat: combat damage routes through shields (ApplyDamage)"
```

---

### Task 4: UpdateShields regen hook

**Files:**
- Modify: `src/SimCore/Sim/SimWorld.Mechanics.cs` (add `UpdateShields`)
- Modify: `src/SimCore/Sim/SimWorld.cs` (`Step` wiring)
- Test: `tests/SimCore.Tests/ShieldRegenTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SimCore.Tests/ShieldRegenTests.cs
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class ShieldRegenTests
{
    private static FactionDef ShieldFaction(int max, int regen, int delay) => new("f", "F",
        units: System.Array.Empty<UnitDef>(),
        buildings: System.Array.Empty<BuildingDef>(),
        upgrades: System.Array.Empty<UpgradeDef>(),
        mechanic: new MechanicDef(MechanicKind.RegeneratingShields, max, regen, delay));

    [Fact]
    public void Shield_Regens_After_Delay_And_Caps_At_Max()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, faction: ShieldFaction(max: 10, regen: 2, delay: 3));
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 2), Fix.FromInt(1), 50);
        w.GetUnit(id)!.ShieldHp = 0; // drained

        // delay=3: ticks 1,2,3 increment TicksSinceDamaged to 1,2,3; regen starts when >=3.
        w.Step(System.Array.Empty<Command>()); // t=1, tsd 0->1, no regen
        w.Step(System.Array.Empty<Command>()); // t=2, tsd 2
        Assert.Equal(0, w.GetUnit(id)!.ShieldHp);
        w.Step(System.Array.Empty<Command>()); // t=3, tsd 3 -> regen +2
        Assert.Equal(2, w.GetUnit(id)!.ShieldHp);
        for (int i = 0; i < 10; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(10, w.GetUnit(id)!.ShieldHp); // capped at max
    }

    [Fact]
    public void Non_Shield_Faction_Has_No_Regen_State_Churn()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 1, faction: TestFactions.Standard);
        var id = w.SpawnUnit(0, w.Map.CellCenter(2, 2), Fix.FromInt(1), 50);
        for (int i = 0; i < 5; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.GetUnit(id)!.ShieldHp);
        Assert.Equal(0, w.GetUnit(id)!.TicksSinceDamaged); // skipped entirely for non-shield units
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter ShieldRegenTests`
Expected: `Shield_Regens_After_Delay_And_Caps_At_Max` FAILS (no regen happens); the non-shield test passes already.

- [ ] **Step 3: Implement.**

In `src/SimCore/Sim/SimWorld.Mechanics.cs`, add:
```csharp
    /// <summary>Per-tick faction-mechanic update. Today: regenerating shields.
    /// Future mechanics dispatch here on MechanicKind. Units without the mechanic are
    /// untouched, so this is a no-op for non-mechanic factions (golden-safe).</summary>
    private void UpdateShields()
    {
        if (Faction?.Mechanic is not { Kind: MechanicKind.RegeneratingShields } m) return;
        foreach (var u in _units)
        {
            if (!HasShields(u)) continue;
            u.TicksSinceDamaged++;
            if (u.TicksSinceDamaged >= m.RegenDelayTicks && u.ShieldHp < m.MaxShield)
                u.ShieldHp = System.Math.Min(m.MaxShield, u.ShieldHp + m.RegenPerTick);
        }
    }
```
(Note the early-return when the world faction has no shield mechanic: this guarantees zero state churn for non-shield factions, keeping the golden stable. `HasShields(u)` is redundant with the early return under the single shared faction, but keeps the loop correct when `MechanicFor` becomes per-player.)

In `src/SimCore/Sim/SimWorld.cs` `Step`, add `UpdateShields();` immediately after `UpdateResearch();` (and before `RemoveDead();`). Read `Step` first to place it exactly.

- [ ] **Step 4: Run full `dotnet test`** — expect +2 tests, all passing. **Golden unchanged** — the scenario's faction has no mechanic, so `UpdateShields` early-returns every tick. If golden changes, the early-return guard is wrong — debug, do NOT re-pin.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/SimWorld.Mechanics.cs src/SimCore/Sim/SimWorld.cs tests/SimCore.Tests/ShieldRegenTests.cs
git commit -m "feat: UpdateShields per-tick regen hook"
```

---

### Task 5: StateHasher v5 + determinism scenario v7 (one re-pin)

**Files:**
- Modify: `src/SimCore/Sim/StateHasher.cs`
- Modify: `tests/SimCore.Tests/DeterminismTests.cs`
- Test: `tests/SimCore.Tests/ShieldHashTests.cs`

- [ ] **Step 1: Write the failing hash tests**

```csharp
// tests/SimCore.Tests/ShieldHashTests.cs
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class ShieldHashTests
{
    private static SimWorld World()
    {
        var w = new SimWorld(new MapGrid(16, 16), seed: 5, faction: TestFactions.Standard);
        w.SpawnUnit(0, FixVec.FromInts(1, 1), Fix.FromFraction(1, 2), 50);
        w.SpawnUnit(1, FixVec.FromInts(5, 5), Fix.FromFraction(1, 2), 80);
        return w;
    }

    [Fact]
    public void ShieldHp_Change_Changes_Hash()
    {
        var a = World();
        var b = World();
        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
        b.Units[0].ShieldHp = 7;
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void TicksSinceDamaged_Change_Changes_Hash()
    {
        var a = World();
        var b = World();
        b.Units[0].TicksSinceDamaged = 3;
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter ShieldHashTests`
Expected: both FAIL (shield fields not hashed).

- [ ] **Step 3: Extend StateHasher.** In `src/SimCore/Sim/StateHasher.cs`, in the per-unit loop (after the patrol fields, before the loop closes), add:
```csharp
            h = Mix(h, (ulong)u.ShieldHp);
            h = Mix(h, (ulong)u.TicksSinceDamaged);
```
Update the convention doc-comment to note shield state is now folded (v5).

- [ ] **Step 4:** Run `dotnet test --filter ShieldHashTests` — expect PASS. The golden test now FAILS (hash function grew) — re-pin in Step 6.

- [ ] **Step 5: Add shields to the determinism scenario.** In `tests/SimCore.Tests/DeterminismTests.cs` `Scenario()`, give `scenarioFaction` the shield mechanic by adding the `mechanic:` argument to its construction:
```csharp
            mechanic: new MechanicDef(MechanicKind.RegeneratingShields, MaxShield: 15, RegenPerTick: 1, RegenDelayTicks: 10));
```
(Append it as the last argument to the existing `new FactionDef("scenario", "Scenario", units:…, buildings:…, upgrades:…)` call.) Now every unit in the scenario spawns with 15 shield, absorbs damage shield-first, and regenerates — combat outcomes shift across the whole battle, all under the hashed trajectory.

- [ ] **Step 6: Re-pin and verify.**

Run `dotnet test --filter DeterminismTests`:
- The two replay tests MUST PASS (if not, real nondeterminism — debug; BLOCKED if unresolved).
- `Trajectory_Hash_Matches_Golden_Constant` FAILS with the actual folded value — copy it into `GoldenTrajectoryHash`.

Re-run `dotnet test` (Debug) AND `dotnet test --configuration Release` — all pass (~217 SimCore), identical golden in both. If Debug/Release disagree, STOP (BLOCKED).

VERIFY (temporary instrumentation, then remove): after a 500-tick run, at least one surviving unit has `ShieldHp > 0` AND the scenario had ticks where a unit's `ShieldHp` absorbed damage (e.g. assert some unit's ShieldHp changed from 15 during the battle). Confirm shields actually engaged. Remove instrumentation; `git status` clean.

- [ ] **Step 7: Commit**

```bash
git add src/SimCore/Sim/StateHasher.cs tests/SimCore.Tests/ShieldHashTests.cs tests/SimCore.Tests/DeterminismTests.cs
git commit -m "feat: StateHasher v5 (shield state); scenario v7 fields shields (re-pin golden)"
```

---

### Task 6: Godot shield bar

**Files:**
- Modify: the Godot unit-view renderer (locate via grep — likely `godot/scripts/UnitView.cs` or `ViewSync.cs`)

- [ ] **Step 1: Locate the HP-bar draw.** Run:
```bash
cd /c/Users/lssha/llm-rts && grep -rn "Hp\|HealthBar\|DrawRect\|bar" godot/scripts/ | grep -i "bar\|hp\|health" | head -30
```
Read the file that draws the unit HP bar.

- [ ] **Step 2: Add a shield bar** above the HP bar, shown only when the unit's `ShieldHp > 0`. Read the unit's shield via the sim (the view already reads unit state for the HP bar — read `ShieldHp` and, if the faction has a shield mechanic, `Faction.Mechanic.MaxShield` for the fraction). Draw a thin cyan bar (e.g. `Colors.Cyan`) directly above the HP bar, width proportional to `ShieldHp / MaxShield`. Match the existing bar-drawing helper/style; keep it minimal. If `ShieldHp == 0`, draw nothing extra.

- [ ] **Step 3: Build the Godot project.**
```bash
cd /c/Users/lssha/llm-rts && dotnet build godot/LlmRts.Godot.csproj -c Debug
```
Expected: 0 errors. Then `dotnet test` to confirm SimCore unaffected (~217 pass).

- [ ] **Step 4: Commit**

```bash
git add godot/
git commit -m "feat: Godot shield bar above HP bar when ShieldHp > 0"
```

## Done Criteria

- `dotnet test --configuration Release` passes (~217 SimCore + 6 SpriteSlicer) including scenario v7 (shields exercised mid-run).
- A faction can declare a `RegeneratingShields` mechanic; its units spawn shielded, absorb damage shield-first, and regenerate after a delay.
- Only new hashed state: per-unit `ShieldHp` + `TicksSinceDamaged`. `MechanicDef` is static/unhashed.
- Framework establishes the pattern (selector + `MechanicFor` accessor + `UpdateShields` dispatch point) for future mechanics without a speculative hook-bus.
- Godot builds and shows a shield bar.
- `grep -rni godot src/SimCore` → no hits; no float/double in src/SimCore outside `Fix.ToString()`.

**Next plan (3d):** Faction pack format + validator — JSON ↔ `FactionDef` (units/buildings/upgrades/mechanic/tiers), the `Fix` wire-format converter (recorded landmine), point-budget balance formula, multi-stage validator + fix-it loop, pack loader, the Faction Forge prompt, and the reference faction as the first data pack.

## Plan-3d Inputs (carried forward from 3c final review — STATUS: 3c COMPLETE, merged 2026-06-13, 224 SimCore tests, golden 5141900307592480923UL)

1. **`Fix` JSON converter is THE landmine.** `Fix` is a readonly struct wrapping `long Raw` (Q48.16). Three Fix fields must round-trip deterministically: `UnitSpec.Speed`, `WeaponSpec.Range`, `UpgradeDef.Delta`. Do NOT reuse `Fix.ToString()` (uses double, lossy — it's the lone double in SimCore). Write a `JsonConverter<Fix>` using a decimal string (or fraction) that parses back exactly via `FromFraction`/raw — deterministic, human-authorable. `MaxShield`/`RegenPerTick`/`RegenDelayTicks`/HP/cooldown/tier are plain ints (fine).
2. **Feed the ctor, never rebuild dicts.** JSON↔FactionDef mapper must pass ordered arrays to the FactionDef ctor (which builds the lookup dicts in order); never reconstruct `_units`/`_buildings`/`_upgrades` directly — preserves spawn/iteration determinism.
3. **`MechanicDef`**: serialize `MechanicKind` by NAME (forward-compat as kinds grow). Budget formula prices the mechanic from its fields (shield cost ≈ MaxShield × regen rate). Mechanic is static/unhashed — pricing is pure pack-load concern.
4. **Nullable/defaulted fields:** `UnitSpec.Weapon`/`.Harvester` are nullable (omit/null in JSON); `BuildingSpec` has many defaulted bools/ints (IsDepot, CanTrain, SupplyProvided, SightRange) — treat as optional-with-defaults in pack JSON.
5. **`Validate()` is the validator SEED** (referential integrity + mechanic params only). 3d ADDS the multi-stage validator: point-budget balance formula, tier monotonicity, producer-reachability, structural rules — folding the existing checks in.
6. **Budget formula prices all four catalogs:** units (HP/speed/weapon/sight/cost), buildings, upgrades (Delta × stat × target breadth), mechanic. Define a power formula + per-tier budget; cost must scale with power within tolerance.
7. **`FactionDef` is a class, not a record** — no structural `==`. Round-trip test (FactionDef→JSON→FactionDef) must compare via the lists/fields or a test helper, not `==`.
8. **Per-player seam (plan 5, NOT 3d):** `InitialShield()` and the Godot shield bar read the shared `Faction?.Mechanic`; when factions go per-player they'll need owner-aware lookup. Out of scope for 3d (still single shared faction).
