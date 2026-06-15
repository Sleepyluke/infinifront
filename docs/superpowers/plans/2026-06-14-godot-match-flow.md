# Godot Match Flow & Menu Implementation Plan (5e)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the playable match flow — a start menu (pick your faction, the CPU's faction, a difficulty), a 1v1 match vs the CPU, and a Victory/Defeat screen with Restart — on top of the deterministic core built in 5a–5d.

**Architecture:** Headless-testable seams in SimCore/SimCore.Packs (`PackCatalog` to list factions, `MatchSetup.BuildStandard1v1` to construct a match); thin Godot UI (`MenuScreen`, `GameOverScreen`, `Main.cs` wiring) that is compile-checked and user-playtested. Restart/menu use a static `MatchConfig` + full scene reload (robust, no partial view re-init). No SimCore determinism impact.

**Tech Stack:** C# / .NET 8, Godot 4.6 .NET, xUnit. SimCore stays Godot-free; Godot layer floats only in render math.

**Source spec:** `docs/superpowers/specs/2026-06-14-godot-match-flow-design.md`

---

## Conventions for every task

- Run from repo root `C:\Users\lssha\llm-rts`. If `dotnet` missing: bash `export PATH="$PATH:/c/Program Files/dotnet"`.
- SimCore tests: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`. Godot build: `dotnet build godot/LlmRts.Godot.csproj -v q`.
- **Baseline:** 313 SimCore tests pass; golden = `1571756151672809223UL` (5e adds NO hashed state — golden must stay unchanged). After each commit, confirm `git log --oneline -1`. End commit messages with `Co-Authored-By: RuFlo <ruv@ruv.net>`.

## Engine facts (verified against source)

- `FactionPackLoader.LoadFromJson(json)` → `PackLoadResult(FactionDef? Faction, IReadOnlyList<string> Errors)`; `ToJson(FactionDef)`. `ReferenceFaction.Def` (id "reference"). `FactionDef`: `Id`, `Name`, `UnitList`, `BuildingList`, `GetBuilding(id)→BuildingDef?`. `UnitDef(Id,Tier,ProducedBy,Requires,Spec)`; `UnitSpec` has `Weapon?`/`Harvester?`/`MineralCost`. `BuildingDef(Id,Tier,Requires,Spec)`; `BuildingSpec` has `IsDepot`/`CanTrain`/`SupplyProvided`.
- `SimWorld(MapGrid, ulong seed, FactionDef?[] factions)` ctor (5b); `SetCpu(int, AiDifficulty)` (5c); `AddCompletedBuilding(player, BuildingSpec, x, y, defId)`, `SpawnUnit(player, FixVec, UnitSpec, defId)`, `AddResourceNode(x,y,amount)`, `Players[p].Minerals`, `Map.CellCenter(x,y)`, `Map.SetPassable(x,y,bool)`, `Phase`/`WinnerId` (5a). `enum AiDifficulty { Easy, Medium, Hard }`, `enum MatchPhase { InProgress, Over }`.
- Godot: `godot/scripts/Main.cs` `_Ready` builds everything in code; `SimRunner` (`World`, `Paused`, `Init(world)`, `Ticked` event, `Enqueue`). `TestMap.Build()` (the only world-builder) is called at `Main.cs:17`. `Hud` is a `CanvasLayer` building `Control` nodes in code (`new Button{Text=...}; btn.Pressed += action; container.AddChild(btn)`). Project main scene `Main.tscn` (`godot/project.godot:5`). `RepoPaths` (test helper, `tests/SimCore.Tests/Packs/RepoPaths.cs`, namespace `SimCore.Tests.Packs`) finds the repo root and `Pack(rel)`.

## File Structure

- `src/SimCore.Packs/PackCatalog.cs` — NEW (Task 1).
- `src/SimCore/Sim/MatchSetup.cs` — NEW (Task 2).
- `godot/scripts/TestMap.cs` — MODIFY: delegate to `MatchSetup` (Task 2).
- `godot/scripts/MatchConfig.cs`, `MenuScreen.cs`, `GameOverScreen.cs` — NEW (Task 3); `godot/scripts/Main.cs` — MODIFY (Task 3).
- `tests/SimCore.Tests/Packs/PackCatalogTests.cs`, `tests/SimCore.Tests/MatchSetupTests.cs` — NEW.

---

## Task 1: `PackCatalog` (SimCore.Packs) + tests

**Files:**
- Create: `src/SimCore.Packs/PackCatalog.cs`
- Test: `tests/SimCore.Tests/Packs/PackCatalogTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/SimCore.Tests/Packs/PackCatalogTests.cs`:

```csharp
using System.IO;
using System.Linq;
using SimCore.Packs;
using SimCore.Sim;
using Xunit;

namespace SimCore.Tests.Packs;

public class PackCatalogTests
{
    [Fact]
    public void Always_Includes_The_Reference_Faction_First()
    {
        var entries = PackCatalog.Load(RepoPaths.Pack("")); // the repo packs/ dir
        Assert.NotEmpty(entries);
        Assert.Equal("reference", entries[0].Faction.Id);
        // The on-disk packs/reference dedups against the in-code reference (no duplicate id).
        Assert.Single(entries, e => e.Faction.Id == "reference");
    }

    [Fact]
    public void Missing_Directory_Yields_Only_Reference()
    {
        var entries = PackCatalog.Load(Path.Combine(Path.GetTempPath(), "nope-" + System.Guid.NewGuid().ToString("N")));
        Assert.Single(entries);
        Assert.Equal("reference", entries[0].Faction.Id);
    }

    [Fact]
    public void Loads_A_Distinct_Valid_Pack_And_Skips_A_Malformed_One()
    {
        var dir = Path.Combine(Path.GetTempPath(), "packcat-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "alpha"));
        Directory.CreateDirectory(Path.Combine(dir, "broken"));
        try
        {
            // A valid distinct faction (id "alpha").
            var alpha = new FactionDef("alpha", "Alpha",
                new[] { new UnitDef("w", 1, "hq", System.Array.Empty<string>(),
                    new UnitSpec(10, SimCore.Math.Fix.One, 1, 1, 1, Harvester: new HarvesterSpec(1, 1))) },
                new[] { new BuildingDef("hq", 1, System.Array.Empty<string>(),
                    new BuildingSpec(10, 1, 1, 1, 1, IsDepot: true, CanTrain: true)) });
            File.WriteAllText(Path.Combine(dir, "alpha", "faction.json"), FactionPackLoader.ToJson(alpha));
            File.WriteAllText(Path.Combine(dir, "broken", "faction.json"), "{ not valid json ");

            var entries = PackCatalog.Load(dir);
            Assert.Contains(entries, e => e.Faction.Id == "alpha");
            Assert.DoesNotContain(entries, e => e.Faction.Id == "broken");
            Assert.Equal("reference", entries[0].Faction.Id); // reference still first
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
```

- [ ] **Step 2: Run, expect failure** — compile FAILS (`PackCatalog` missing).

- [ ] **Step 3: Implement `PackCatalog`**

Create `src/SimCore.Packs/PackCatalog.cs`:

```csharp
using System.Collections.Generic;
using SimCore.Sim;

namespace SimCore.Packs;

/// <summary>An available faction for match setup: a display name + the loaded def.</summary>
public sealed record FactionEntry(string Name, FactionDef Faction);

/// <summary>Lists the factions available to play: the in-code reference faction first, then every
/// valid pack found under a directory (each <dir>/<name>/faction.json, via FactionPackLoader).
/// Deterministic order; invalid/duplicate-id packs are skipped; never throws.</summary>
public static class PackCatalog
{
    public static IReadOnlyList<FactionEntry> Load(string packsDir)
    {
        var list = new List<FactionEntry> { new("Reference", ReferenceFaction.Def) };
        var seenIds = new HashSet<string>(System.StringComparer.Ordinal) { ReferenceFaction.Def.Id };

        if (!System.IO.Directory.Exists(packsDir)) return list;

        var dirs = System.IO.Directory.GetDirectories(packsDir);
        System.Array.Sort(dirs, System.StringComparer.Ordinal); // deterministic order
        foreach (var dir in dirs)
        {
            var jsonPath = System.IO.Path.Combine(dir, "faction.json");
            if (!System.IO.File.Exists(jsonPath)) continue;
            string text;
            try { text = System.IO.File.ReadAllText(jsonPath); }
            catch { continue; }
            var result = FactionPackLoader.LoadFromJson(text);
            if (result.Faction is null || result.Errors.Count > 0) continue;
            if (!seenIds.Add(result.Faction.Id)) continue; // skip duplicate id (e.g. the on-disk reference)
            list.Add(new FactionEntry(result.Faction.Name, result.Faction));
        }
        return list;
    }
}
```

- [ ] **Step 4: Run, expect pass** — `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`. Expected: 313 + 3 = 316; golden unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore.Packs/PackCatalog.cs tests/SimCore.Tests/Packs/PackCatalogTests.cs
git commit -m "feat(packs): PackCatalog — list reference + valid packs from a directory

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 2: `MatchSetup` (SimCore) + tests + TestMap delegate

**Files:**
- Create: `src/SimCore/Sim/MatchSetup.cs`
- Modify: `godot/scripts/TestMap.cs`
- Test: `tests/SimCore.Tests/MatchSetupTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/SimCore.Tests/MatchSetupTests.cs`:

```csharp
using System.Collections.Generic;
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class MatchSetupTests
{
    private static int Workers(SimWorld w, int p)
    {
        int c = 0; foreach (var u in w.Units) if (u.OwnerId == p && u.Harvester is not null) c++; return c;
    }
    private static int Buildings(SimWorld w, int p)
    {
        int c = 0; foreach (var b in w.Buildings) if (b.OwnerId == p) c++; return c;
    }

    [Fact]
    public void Builds_A_Standard_1v1_With_Per_Player_Factions_And_Cpu()
    {
        var w = MatchSetup.BuildStandard1v1(ReferenceFaction.Def, ReferenceFaction.Def, AiDifficulty.Hard, seed: 1);
        Assert.Same(ReferenceFaction.Def, w.FactionFor(0));
        Assert.Same(ReferenceFaction.Def, w.FactionFor(1));
        Assert.Equal(PlayerController.Human, w.Players[0].Controller);
        Assert.Equal(PlayerController.Cpu, w.Players[1].Controller);
        Assert.Equal(AiDifficulty.Hard, w.Players[1].Difficulty);
        Assert.True(Buildings(w, 0) >= 1 && Buildings(w, 1) >= 1, "both players need a starting base");
        Assert.True(Workers(w, 0) >= 1 && Workers(w, 1) >= 1, "both players need starting workers");
        Assert.Equal(MatchPhase.InProgress, w.Phase);
    }

    [Fact]
    public void Steps_Without_Throwing()
    {
        var w = MatchSetup.BuildStandard1v1(ReferenceFaction.Def, ReferenceFaction.Def, AiDifficulty.Medium, seed: 2);
        var empty = new List<Command>();
        for (int t = 0; t < 50; t++) w.Step(empty); // CPU acts; no exception
    }

    [Fact]
    public void Uses_The_Cpu_Players_Own_Faction()
    {
        var cpu = new FactionDef("beta", "Beta",
            new[] { new UnitDef("bw", 1, "bhall", System.Array.Empty<string>(),
                new UnitSpec(10, Fix.One, 1, 1, 1, Harvester: new HarvesterSpec(1, 1))) },
            new[] { new BuildingDef("bhall", 1, System.Array.Empty<string>(),
                new BuildingSpec(10, 2, 2, 1, 1, IsDepot: true, CanTrain: true)) });
        var w = MatchSetup.BuildStandard1v1(ReferenceFaction.Def, cpu, AiDifficulty.Easy, seed: 3);
        Assert.Same(cpu, w.FactionFor(1));
        Assert.Contains(w.Buildings, b => b.OwnerId == 1 && b.DefId == "bhall"); // CPU base from its own faction
    }
}
```

- [ ] **Step 2: Run, expect failure** — compile FAILS (`MatchSetup` missing).

- [ ] **Step 3: Implement `MatchSetup`**

Create `src/SimCore/Sim/MatchSetup.cs`:

```csharp
using SimCore.Math;

namespace SimCore.Sim;

/// <summary>Builds a standard 1v1 match world (player 0 = human, player 1 = CPU) from two
/// factions + a difficulty. Starting bases are role-resolved from each player's OWN faction, so
/// it works for any pack. Deterministic; SimCore-only (no Godot).</summary>
public static class MatchSetup
{
    public const int MapSize = 40;

    public static SimWorld BuildStandard1v1(FactionDef humanFaction, FactionDef cpuFaction,
                                            AiDifficulty difficulty, ulong seed)
    {
        var map = new MapGrid(MapSize, MapSize);
        // Rock ridge at x=20 with gaps at y=8..11 and y=28..31 (matches the legacy sandbox).
        for (int y = 0; y < MapSize; y++)
            if (y < 8 || (y > 11 && y < 28) || y > 31) map.SetPassable(20, y, false);

        var w = new SimWorld(map, seed, new FactionDef?[] { humanFaction, cpuFaction });
        w.SetCpu(1, difficulty);

        PlaceBase(w, 0, humanFaction, depotX: 4, depotY: 4, raxX: 8, raxY: 4, nodeX: 2, nodeY: 8, workerX: 6, workerY: 8);
        PlaceBase(w, 1, cpuFaction, depotX: 34, depotY: 34, raxX: 30, raxY: 34, nodeX: 37, nodeY: 28, workerX: 33, workerY: 28);
        return w;
    }

    private static void PlaceBase(SimWorld w, int player, FactionDef faction,
        int depotX, int depotY, int raxX, int raxY, int nodeX, int nodeY, int workerX, int workerY)
    {
        w.Players[player].Minerals = 300;
        var worker = FirstWorker(faction);
        var combat = CheapestCombat(faction);
        var workerProd = worker is null ? null : faction.GetBuilding(worker.ProducedBy);
        var combatProd = combat is null ? null : faction.GetBuilding(combat.ProducedBy);

        if (workerProd is not null)
            w.AddCompletedBuilding(player, workerProd.Spec, depotX, depotY, workerProd.Id);
        if (combatProd is not null && combatProd.Id != workerProd?.Id)
            w.AddCompletedBuilding(player, combatProd.Spec, raxX, raxY, combatProd.Id);

        for (int i = 0; i < 4; i++) w.AddResourceNode(nodeX, nodeY + i, amount: 500);

        if (worker is not null)
            for (int i = 0; i < 3; i++)
                w.SpawnUnit(player, w.Map.CellCenter(workerX, workerY + i), worker.Spec, worker.Id);
    }

    private static UnitDef? FirstWorker(FactionDef f)
    {
        foreach (var u in f.UnitList) if (u.Spec.Harvester is not null) return u;
        return null;
    }

    private static UnitDef? CheapestCombat(FactionDef f)
    {
        UnitDef? best = null;
        foreach (var u in f.UnitList)
            if (u.Spec.Weapon is not null && (best is null || u.Spec.MineralCost < best.Spec.MineralCost))
                best = u;
        return best;
    }
}
```

- [ ] **Step 4: Delegate `TestMap` to `MatchSetup`**

Replace the body of `godot/scripts/TestMap.cs`'s `Build()` so the sandbox boots a real 1v1 (human reference vs an Easy CPU reference). Replace the whole file with:

```csharp
using SimCore.Sim;

namespace LlmRts.Godot;

public static class TestMap
{
    public const int Size = MatchSetup.MapSize;

    /// <summary>Default sandbox match: human (Reference) vs an Easy CPU (Reference).
    /// The menu (Main + MenuScreen) overrides this with the player's chosen config.</summary>
    public static SimWorld Build() =>
        MatchSetup.BuildStandard1v1(ReferenceFaction.Def, ReferenceFaction.Def, AiDifficulty.Easy, seed: 42);
}
```

- [ ] **Step 5: Run SimCore tests + build Godot**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q` → 316 + 3 = 319; golden unchanged.
Run: `dotnet build godot/LlmRts.Godot.csproj -v q` → builds clean (TestMap delegate compiles; `MatchSetup`/`AiDifficulty` resolve from SimCore).

- [ ] **Step 6: Commit**

```bash
git add src/SimCore/Sim/MatchSetup.cs godot/scripts/TestMap.cs tests/SimCore.Tests/MatchSetupTests.cs
git commit -m "feat(sim): MatchSetup.BuildStandard1v1 (per-player factions + CPU); TestMap delegates

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 3: Godot menu + game-over flow (compile-checked + playtested)

**Files:**
- Create: `godot/scripts/MatchConfig.cs`, `godot/scripts/MenuScreen.cs`, `godot/scripts/GameOverScreen.cs`
- Modify: `godot/scripts/Main.cs`

This task's automated gate is **compilation** (`dotnet build godot/LlmRts.Godot.csproj`); behavior is verified by the manual playtest checklist in Step 6. Write clean, compiling Godot 4 C#.

- [ ] **Step 1: Create the static match-config holder**

Create `godot/scripts/MatchConfig.cs`:

```csharp
using SimCore.Sim;

namespace LlmRts.Godot;

/// <summary>Process-static chosen match config, set by the menu and read by Main on (re)load.
/// Survives GetTree().ReloadCurrentScene() so Restart/Play rebuild a fresh scene deterministically.</summary>
public static class MatchConfig
{
    public static bool Configured;
    public static FactionDef Human = ReferenceFaction.Def;
    public static FactionDef Cpu = ReferenceFaction.Def;
    public static AiDifficulty Difficulty = AiDifficulty.Easy;

    public static void Set(FactionDef human, FactionDef cpu, AiDifficulty difficulty)
    {
        Human = human; Cpu = cpu; Difficulty = difficulty; Configured = true;
    }

    public static void Clear() => Configured = false;
}
```

- [ ] **Step 2: Create the start menu**

Create `godot/scripts/MenuScreen.cs`:

```csharp
using Godot;
using SimCore.Packs;
using SimCore.Sim;
using System.Collections.Generic;

namespace LlmRts.Godot;

/// <summary>Start menu: pick your faction, the CPU's faction, and a difficulty, then Play.
/// On Play, stores the choice in MatchConfig and reloads the scene to start the match.</summary>
public partial class MenuScreen : CanvasLayer
{
    private IReadOnlyList<FactionEntry> _factions = System.Array.Empty<FactionEntry>();
    private int _human, _cpu;
    private AiDifficulty _difficulty = AiDifficulty.Easy;
    private OptionButton _humanPick = null!, _cpuPick = null!;
    private Label _diffLabel = null!;

    public override void _Ready()
    {
        Layer = 100; // above the world/HUD
        _factions = PackCatalog.Load(PacksDir());

        var panel = new PanelContainer();
        var box = new VBoxContainer();
        panel.AddChild(box);
        AddChild(panel);
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);

        box.AddChild(new Label { Text = "LlmRts — New Match", HorizontalAlignment = HorizontalAlignment.Center });

        box.AddChild(new Label { Text = "Your faction:" });
        _humanPick = MakeFactionPicker(i => _human = i);
        box.AddChild(_humanPick);

        box.AddChild(new Label { Text = "CPU faction:" });
        _cpuPick = MakeFactionPicker(i => _cpu = i);
        box.AddChild(_cpuPick);

        box.AddChild(new Label { Text = "Difficulty:" });
        var diffRow = new HBoxContainer();
        foreach (var d in new[] { AiDifficulty.Easy, AiDifficulty.Medium, AiDifficulty.Hard })
        {
            var captured = d;
            var b = new Button { Text = d.ToString() };
            b.Pressed += () => { _difficulty = captured; _diffLabel.Text = "Difficulty: " + captured; };
            diffRow.AddChild(b);
        }
        box.AddChild(diffRow);
        _diffLabel = new Label { Text = "Difficulty: " + _difficulty };
        box.AddChild(_diffLabel);

        var play = new Button { Text = "Play" };
        play.Pressed += OnPlay;
        box.AddChild(play);
    }

    private OptionButton MakeFactionPicker(System.Action<int> onSelect)
    {
        var opt = new OptionButton();
        for (int i = 0; i < _factions.Count; i++) opt.AddItem(_factions[i].Name, i);
        opt.Selected = 0;
        opt.ItemSelected += id => onSelect((int)id);
        return opt;
    }

    private void OnPlay()
    {
        if (_factions.Count == 0) return;
        MatchConfig.Set(_factions[_human].Faction, _factions[_cpu].Faction, _difficulty);
        GetTree().ReloadCurrentScene();
    }

    private static string PacksDir() =>
        System.IO.Path.Combine(System.AppContext.BaseDirectory, "packs");
}
```

(Note: `PacksDir()` resolves `packs` next to the Godot build output. The packs are committed at the repo root `packs/`; wiring the exact runtime path is a playtest detail — if the menu shows only "Reference", the packs dir path needs adjusting, but the flow still works with the reference faction. Document this in the playtest notes.)

- [ ] **Step 3: Create the game-over overlay**

Create `godot/scripts/GameOverScreen.cs`:

```csharp
using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

/// <summary>Watches the match outcome; when Over, pauses the sim and shows Victory/Defeat/Draw
/// with Restart (same config) and Menu buttons.</summary>
public partial class GameOverScreen : CanvasLayer
{
    private SimRunner _runner = null!;
    private PanelContainer _panel = null!;
    private Label _result = null!;
    private bool _shown;

    public void Init(SimRunner runner)
    {
        _runner = runner;
        _runner.Ticked += Check;
    }

    public override void _Ready()
    {
        Layer = 90;
        _panel = new PanelContainer { Visible = false };
        var box = new VBoxContainer();
        _panel.AddChild(box);
        AddChild(_panel);
        _panel.SetAnchorsPreset(Control.LayoutPreset.Center);

        _result = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        box.AddChild(_result);

        var restart = new Button { Text = "Restart" };
        restart.Pressed += () => GetTree().ReloadCurrentScene(); // MatchConfig still set
        box.AddChild(restart);

        var menu = new Button { Text = "Menu" };
        menu.Pressed += () => { MatchConfig.Clear(); GetTree().ReloadCurrentScene(); };
        box.AddChild(menu);
    }

    private void Check()
    {
        if (_shown || _runner.World.Phase != MatchPhase.Over) return;
        _shown = true;
        _runner.Paused = true;
        int winner = _runner.World.WinnerId;
        _result.Text = winner == 0 ? "Victory!" : winner == 1 ? "Defeat" : "Draw";
        _panel.Visible = true;
    }
}
```

- [ ] **Step 4: Wire `Main.cs`**

In `godot/scripts/Main.cs`, change `_Ready` to: build the world from `MatchConfig` (or the default), and either show the menu (unconfigured) or attach the game-over overlay (in a match). Replace the first two lines of `_Ready` (the `Runner` creation + `Runner.Init(TestMap.Build())`) with:

```csharp
        Runner = new SimRunner { Name = "SimRunner" };
        Runner.Init(MatchConfig.Configured
            ? MatchSetup.BuildStandard1v1(MatchConfig.Human, MatchConfig.Cpu, MatchConfig.Difficulty, seed: 42)
            : TestMap.Build());
        AddChild(Runner);
```

Then at the END of `_Ready` (after the `GD.Print(...)` line), add:

```csharp
        if (!MatchConfig.Configured)
        {
            Runner.Paused = true;                 // hold the sim behind the menu
            AddChild(new MenuScreen { Name = "Menu" });
        }
        else
        {
            var gameOver = new GameOverScreen { Name = "GameOver" };
            AddChild(gameOver);
            gameOver.Init(Runner);
        }
```

(`MatchSetup` and `AiDifficulty` are in `SimCore.Sim`; `Main.cs` already uses `SimCore.Sim` types via `Runner.World`. Add `using SimCore.Sim;` at the top of `Main.cs` if not present.)

- [ ] **Step 5: Build Godot (compile gate) + SimCore tests**

Run: `dotnet build godot/LlmRts.Godot.csproj -v q` → builds clean (0 errors).
Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q` → 319, golden unchanged (no SimCore change in this task).

- [ ] **Step 6: Commit**

```bash
git add godot/scripts/MatchConfig.cs godot/scripts/MenuScreen.cs godot/scripts/GameOverScreen.cs godot/scripts/Main.cs
git commit -m "feat(godot): start menu (faction + difficulty) + victory/defeat screen + restart

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

**Manual playtest checklist (for the user, via `scripts/play.ps1`):** menu appears at launch; both faction dropdowns list at least "Reference"; choosing a difficulty updates the label; Play starts a match; the CPU builds workers/army and attacks per the chosen tier; destroying the CPU's last building shows "Victory!"; losing your last building shows "Defeat"; Restart starts a fresh match with the same config; Menu returns to the start menu.

---

## Task 4: Full gate + finish Plan 5

**Files:** none (verification) — optionally annotate this plan.

- [ ] **Step 1: SimCore untouched-determinism confirm**

```bash
git diff --stat master -- src/SimCore/Sim/StateHasher.cs tests/SimCore.Tests/DeterminismTests.cs
```
Expected: only whatever 5e legitimately added (nothing in StateHasher; DeterminismTests unchanged). Golden still `1571756151672809223UL`.

- [ ] **Step 2: Full solution gate (Release + Debug)**

Run: `dotnet test --configuration Release --nologo -v q` then `dotnet test --configuration Debug --nologo -v q`
Expected: PASS — SimCore.Tests 319, SpriteSlicer.Tests 6, 0 failures; 3 determinism tests green with golden unchanged; Debug == Release.

- [ ] **Step 3: Godot build gate**

Run: `dotnet build godot/LlmRts.Godot.csproj -v q`
Expected: builds clean, 0 errors.

- [ ] **Step 4: Commit (if anything annotated; else skip) and report**

Plan 5 (CPU opponent + match flow) is complete: 5a match state, 5b per-player factions, 5c CPU + Easy, 5d Medium/Hard, 5e menu + match flow. Report completion to the user with the playtest checklist (the menu/overlay need a human playtest).

---

## Self-Review (author checklist — completed)

- **Spec coverage:** `PackCatalog` (Task 1); `MatchSetup.BuildStandard1v1` + TestMap delegate (Task 2); `MatchConfig` + `MenuScreen` (pick both factions + difficulty) + `GameOverScreen` (Victory/Defeat/Draw + Restart/Menu) + `Main` wiring (Task 3); gates (Task 4). Restart/menu via static config + scene reload (robust). Single map. No hashed state → golden unchanged. Every spec "In scope" item maps to a task.
- **Placeholder scan:** none — complete code in every step; the Godot UI is full compiling code (verified by the build gate), with the one runtime path caveat (packs dir) documented for playtest.
- **Type consistency:** `PackCatalog.Load`/`FactionEntry`/`MatchSetup.BuildStandard1v1`/`MatchConfig.Set/Clear`/`MenuScreen`/`GameOverScreen.Init` consistent across tasks; `MatchSetup` resolves the combat producer via the unit's `ProducedBy` (so the reference base gets depot+barracks even though the depot is now CanTrain); Godot APIs (CanvasLayer/OptionButton/Button.Pressed/VBoxContainer/GetTree().ReloadCurrentScene) match Hud.cs's established style; `Phase`/`WinnerId`/`SetCpu`/`FactionFor` match SimCore.
