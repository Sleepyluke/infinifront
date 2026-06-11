# Godot Playable Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A playable Godot sandbox on a 40×40 map: select/move/attack/build/train/harvest with sliced Gemini sprites, Tab-switching between the two players.

**Architecture:** Sim-authoritative view sync (spec: `docs/superpowers/specs/2026-06-10-godot-playable-slice-design.md`). SimWorld ticks at 10/s inside Godot; the view diffs sim state by entity id, interpolates positions at 60 fps, and converts input to `Command` objects. The sim stays headless and deterministic; floats exist only at the render boundary.

**Tech Stack:** Godot 4.6.3 .NET (`%LOCALAPPDATA%\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe` — NEVER the Steam exe, it has no C# support), .NET 8, SixLabors.ImageSharp (slicer only), xUnit.

**Environment note:** in fresh shells run `$env:Path += ';C:\Program Files\dotnet'` if `dotnet` is not found. Repo root: `C:\Users\lssha\llm-rts`.

---

## File structure

```
src/SimCore/Sim/ReferenceSpecs.cs        NEW  reference faction data (all tuning constants)
src/SimCore/Sim/SimWorld.Buildings.cs    MOD  add AddCompletedBuilding setup API
tools/SpriteSlicer/SpriteSlicer.csproj   NEW  console tool (ImageSharp)
tools/SpriteSlicer/Slicer.cs             NEW  chroma key + slice + recenter (pure functions)
tools/SpriteSlicer/Sidecar.cs            NEW  sidecar JSON model + loader
tools/SpriteSlicer/Program.cs            NEW  CLI entry
tools/SpriteSlicer/sidecars/*.json       NEW  hand-authored rects per raw sheet
tests/SpriteSlicer.Tests/                NEW  xUnit tests on synthetic images
tests/SimCore.Tests/ReferenceSpecsTests.cs NEW sanity tests
tests/SimCore.Tests/SetupApiTests.cs     NEW  AddCompletedBuilding tests
godot/project.godot                      NEW  minimal project file
godot/LlmRts.Godot.csproj                NEW  Godot.NET.Sdk project, refs SimCore
godot/Main.tscn                          NEW  single scene: root Node2D + Main.cs
godot/scripts/Main.cs                    NEW  composition root: builds everything in _Ready
godot/scripts/SimRunner.cs               NEW  tick accumulator, command queue, pause
godot/scripts/TestMap.cs                 NEW  hardcoded 40×40 world setup
godot/scripts/RenderMath.cs              NEW  Fix→float, cell→pixel, facing helpers
godot/scripts/SheetAnimator.cs           NEW  contract-sheet SpriteFrames builder
godot/scripts/ViewSync.cs                NEW  id-diff node lifecycle + corpse playback
godot/scripts/UnitView.cs                NEW  per-unit sprite/anim/healthbar/ring
godot/scripts/BuildingView.cs            NEW  building states + silhouette fallback
godot/scripts/NodeView.cs                NEW  mineral node view (fallback diamond)
godot/scripts/MapView.cs                 NEW  terrain _Draw (flat color + wall rects)
godot/scripts/CameraRig.cs               NEW  pan/edge-scroll/zoom
godot/scripts/SelectionController.cs     NEW  click/box/shift select, Tab player switch
godot/scripts/CommandController.cs       NEW  context orders, A-move, build ghost
godot/scripts/Hud.cs                     NEW  resources, selection panel, build/train buttons
godot/assets/units/*.png                 GEN  slicer output (contract sheets)
godot/assets/icons/*.png                 GEN  slicer output (64×64 icons)
LlmRts.sln                               MOD  add 3 projects
```

**Sprite sheet contract (from `docs/art/gemini-sprite-brief.md` §2):** 64×64 cells; rows top-to-bottom `idle-S, idle-W, idle-N, walk-S, walk-W, walk-N, attack-S, attack-W, attack-N, death-S`; frame counts idle 4, walk 6, attack 6, death 6; East = West flipped at render. Sheet = 384×640 px, short rows left-aligned, trailing cells transparent.

---

### Task 1: ReferenceSpecs — the reference faction as data

All tuning constants in one reviewable file. These numbers are *placeholders to be tuned by playtesting* — that's the point of this slice.

**Files:**
- Create: `src/SimCore/Sim/ReferenceSpecs.cs`
- Test: `tests/SimCore.Tests/ReferenceSpecsTests.cs`

- [ ] **Step 1: Write the failing tests** at `tests/SimCore.Tests/ReferenceSpecsTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class ReferenceSpecsTests
{
    [Fact]
    public void All_Units_Have_Positive_Costs_And_Hp()
    {
        foreach (var s in new[] { ReferenceSpecs.Fabber, ReferenceSpecs.Trooper, ReferenceSpecs.Outrider, ReferenceSpecs.Tank })
        {
            Assert.True(s.MaxHp > 0);
            Assert.True(s.MineralCost > 0);
            Assert.True(s.SupplyCost > 0);
            Assert.True(s.BuildTimeTicks > 0);
            Assert.True(s.Speed > Fix.Zero);
        }
    }

    [Fact]
    public void Fabber_Is_The_Only_Harvester()
    {
        Assert.NotNull(ReferenceSpecs.Fabber.Harvester);
        Assert.Null(ReferenceSpecs.Trooper.Harvester);
        Assert.Null(ReferenceSpecs.Outrider.Harvester);
        Assert.Null(ReferenceSpecs.Tank.Harvester);
    }

    [Fact]
    public void Combat_Units_Have_Weapons_Fabber_Does_Not()
    {
        Assert.Null(ReferenceSpecs.Fabber.Weapon);
        Assert.NotNull(ReferenceSpecs.Trooper.Weapon);
        Assert.NotNull(ReferenceSpecs.Outrider.Weapon);
        Assert.NotNull(ReferenceSpecs.Tank.Weapon);
    }

    [Fact]
    public void Depot_Provides_Supply_And_Is_Depot()
    {
        Assert.True(ReferenceSpecs.Depot.SupplyProvided > 0);
        Assert.True(ReferenceSpecs.Depot.IsDepot);
        Assert.False(ReferenceSpecs.Depot.CanTrain);
    }

    [Fact]
    public void Barracks_Trains_And_Is_Not_A_Depot()
    {
        Assert.True(ReferenceSpecs.Barracks.CanTrain);
        Assert.False(ReferenceSpecs.Barracks.IsDepot);
    }

    [Fact]
    public void Trained_Unit_Can_Be_Afforded_From_One_Depot_Of_Supply()
    {
        // every trainable unit must fit within a single depot's supply grant
        foreach (var s in new[] { ReferenceSpecs.Trooper, ReferenceSpecs.Outrider, ReferenceSpecs.Tank })
            Assert.True(s.SupplyCost <= ReferenceSpecs.Depot.SupplyProvided);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter ReferenceSpecsTests`
Expected: compilation FAILS (`ReferenceSpecs` does not exist).

- [ ] **Step 3: Implement** at `src/SimCore/Sim/ReferenceSpecs.cs`:

```csharp
using SimCore.Math;

namespace SimCore.Sim;

/// <summary>Reference faction tuning data. ALL values are placeholders to be
/// tuned by playtesting (the playable slice exists to calibrate them).
/// Precursor of the plan-3 faction pack: when packs land, this file becomes
/// the hand-built reference pack's content.</summary>
public static class ReferenceSpecs
{
    // --- units ----------------------------------------------------------
    public static readonly UnitSpec Fabber = new(
        MaxHp: 40, Speed: Fix.FromFraction(1, 4), MineralCost: 50, SupplyCost: 1,
        BuildTimeTicks: 100,
        Harvester: new HarvesterSpec(CarryCapacity: 5, GatherTicks: 10));

    public static readonly UnitSpec Trooper = new(
        MaxHp: 45, Speed: Fix.FromFraction(1, 5), MineralCost: 50, SupplyCost: 1,
        BuildTimeTicks: 80,
        Weapon: new WeaponSpec(Damage: 6, Range: Fix.FromInt(4), CooldownTicks: 8));

    public static readonly UnitSpec Outrider = new(
        MaxHp: 60, Speed: Fix.FromFraction(1, 2), MineralCost: 75, SupplyCost: 2,
        BuildTimeTicks: 120,
        Weapon: new WeaponSpec(Damage: 4, Range: Fix.FromInt(3), CooldownTicks: 5));

    public static readonly UnitSpec Tank = new(
        MaxHp: 150, Speed: Fix.FromFraction(1, 8), MineralCost: 150, SupplyCost: 3,
        BuildTimeTicks: 200,
        Weapon: new WeaponSpec(Damage: 20, Range: Fix.FromInt(6), CooldownTicks: 20));

    // --- buildings (2x2 footprints) --------------------------------------
    public static readonly BuildingSpec Depot = new(
        MaxHp: 400, Width: 2, Height: 2, MineralCost: 100, BuildTimeTicks: 150,
        SupplyProvided: 8, IsDepot: true);

    public static readonly BuildingSpec Barracks = new(
        MaxHp: 350, Width: 2, Height: 2, MineralCost: 150, BuildTimeTicks: 200,
        CanTrain: true);
}
```

NOTE: check `src/SimCore/Sim/Specs.cs` for the exact `UnitSpec`/`WeaponSpec` parameter names before writing — the constructor shapes above must match the existing records (e.g. if the weapon record is named differently, follow the codebase, not this plan).

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: all pass (109 existing + 6 new). Golden trajectory UNCHANGED (pure data addition).

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/ReferenceSpecs.cs tests/SimCore.Tests/ReferenceSpecsTests.cs
git commit -m "feat: reference faction specs as data (placeholder tuning values)"
```

---

### Task 2: AddCompletedBuilding setup API

The test map needs pre-built bases. `PlaceBuilding` is internal and leaves buildings under construction; the view assembly needs a public setup API that completes them (Hp = MaxHp, IsComplete, supply granted) — mirroring what `UpdateConstruction` does at completion.

**Files:**
- Modify: `src/SimCore/Sim/SimWorld.Buildings.cs`
- Test: `tests/SimCore.Tests/SetupApiTests.cs`

- [ ] **Step 1: Write the failing tests** at `tests/SimCore.Tests/SetupApiTests.cs`:

```csharp
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class SetupApiTests
{
    [Fact]
    public void Completed_Building_Is_Complete_With_Full_Hp_And_Supply()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        var capBefore = w.Players[0].SupplyCap;
        var id = w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 5, 5);
        var b = w.GetBuilding(id)!;
        Assert.True(b.IsComplete);
        Assert.Equal(ReferenceSpecs.Depot.MaxHp, b.Hp);
        Assert.Equal(capBefore + ReferenceSpecs.Depot.SupplyProvided, w.Players[0].SupplyCap);
        Assert.False(w.Map.IsPassable(5, 5)); // footprint blocked
    }

    [Fact]
    public void Completed_Barracks_Can_Train_Immediately()
    {
        var w = new SimWorld(new MapGrid(20, 20), seed: 1);
        w.Players[0].Minerals = 500;
        w.Players[0].SupplyCap = 10;
        var id = w.AddCompletedBuilding(0, ReferenceSpecs.Barracks, 5, 5);
        w.Step(new Command[] { new TrainCommand(0, id, ReferenceSpecs.Trooper) });
        Assert.Single(w.GetBuilding(id)!.Queue);
    }

    [Fact]
    public void Determinism_Holds_With_Setup_Buildings()
    {
        ulong Run()
        {
            var w = new SimWorld(new MapGrid(20, 20), seed: 7);
            w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 3, 3);
            w.AddCompletedBuilding(1, ReferenceSpecs.Depot, 14, 14);
            for (int i = 0; i < 50; i++) w.Step(System.Array.Empty<Command>());
            return StateHasher.Hash(w);
        }
        Assert.Equal(Run(), Run());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter SetupApiTests`
Expected: compilation FAILS (`AddCompletedBuilding` does not exist).

- [ ] **Step 3: Implement.** In `src/SimCore/Sim/SimWorld.Buildings.cs`, add below `PlaceBuilding` (read that method first — reuse it):

```csharp
    /// <summary>Setup-time API (map generation): place a building already
    /// completed — full Hp, IsComplete, supply granted. Mirrors the
    /// UpdateConstruction completion path. Not reachable via commands.</summary>
    public int AddCompletedBuilding(int ownerId, BuildingSpec spec, int cellX, int cellY)
    {
        var id = PlaceBuilding(ownerId, spec, cellX, cellY);
        var b = _buildingsById[id];
        b.IsComplete = true;
        b.Hp = spec.MaxHp;
        b.BuildProgress = spec.BuildTimeTicks;
        _players[ownerId].SupplyCap += spec.SupplyProvided;
        return id;
    }
```

NOTE: verify `PlaceBuilding`'s return type and the completion fields against `UpdateConstruction` in `SimWorld.Buildings.cs` — the completion path there is the source of truth; replicate it exactly (including initial Hp behavior: in-construction buildings may start at partial Hp).

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: all pass. Golden trajectory UNCHANGED (new API is not called by any sim path).

- [ ] **Step 5: Commit**

```bash
git add src/SimCore/Sim/SimWorld.Buildings.cs tests/SimCore.Tests/SetupApiTests.cs
git commit -m "feat: AddCompletedBuilding setup API for map generation"
```

---

### Task 3: SpriteSlicer core — chroma key, slice, recenter (TDD)

Pure image functions, fully unit-testable with synthetic images. No file I/O in this task.

**Files:**
- Create: `tools/SpriteSlicer/SpriteSlicer.csproj`
- Create: `tools/SpriteSlicer/Slicer.cs`
- Create: `tests/SpriteSlicer.Tests/SpriteSlicer.Tests.csproj`
- Create: `tests/SpriteSlicer.Tests/SlicerTests.cs`
- Modify: `LlmRts.sln` (add both projects)

- [ ] **Step 1: Create the projects**

`tools/SpriteSlicer/SpriteSlicer.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SixLabors.ImageSharp" Version="3.1.6" />
  </ItemGroup>
</Project>
```

`tests/SpriteSlicer.Tests/SpriteSlicer.Tests.csproj`: copy `tests/SimCore.Tests/SimCore.Tests.csproj` and change the ProjectReference to `..\..\tools\SpriteSlicer\SpriteSlicer.csproj`.

Run:
```bash
dotnet sln add tools/SpriteSlicer/SpriteSlicer.csproj tests/SpriteSlicer.Tests/SpriteSlicer.Tests.csproj
```

Create `tools/SpriteSlicer/Program.cs` containing just `// CLI added in Task 4` and an empty `Main` so the project builds:

```csharp
public static class Program { public static void Main(string[] args) { } }
```

- [ ] **Step 2: Write the failing tests** at `tests/SpriteSlicer.Tests/SlicerTests.cs`:

```csharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SpriteSlicer;
using Xunit;

public class SlicerTests
{
    private static Image<Rgba32> Filled(int w, int h, Rgba32 color)
    {
        var img = new Image<Rgba32>(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                img[x, y] = color;
        return img;
    }

    private static readonly Rgba32 Magenta = new(255, 0, 255, 255);
    private static readonly Rgba32 Green = new(0, 200, 0, 255);

    [Fact]
    public void ChromaKey_Clears_Magenta_Keeps_Content()
    {
        var img = Filled(10, 10, Magenta);
        img[5, 5] = Green;
        Slicer.ChromaKey(img);
        Assert.Equal(0, img[0, 0].A);
        Assert.Equal(255, img[5, 5].A);
    }

    [Fact]
    public void ChromaKey_Tolerance_Catches_Compression_Fringe()
    {
        var img = Filled(4, 1, new Rgba32(247, 12, 244, 255)); // near-magenta
        Slicer.ChromaKey(img);
        Assert.Equal(0, img[0, 0].A);
    }

    [Fact]
    public void SliceRow_Produces_FrameCount_Equal_Cells()
    {
        var strip = Filled(300, 100, Magenta);
        var frames = Slicer.SliceRow(strip, new Rectangle(0, 0, 300, 100), 3);
        Assert.Equal(3, frames.Count);
        Assert.All(frames, f => Assert.Equal(100, f.Width));
    }

    [Fact]
    public void RenderToCell_Centers_Content_On_Baseline()
    {
        // 100x100 frame, content = 20x40 green block at left edge
        var frame = Filled(100, 100, new Rgba32(0, 0, 0, 0));
        for (int y = 30; y < 70; y++)
            for (int x = 0; x < 20; x++)
                frame[x, y] = Green;
        var cell = Slicer.RenderToCell(frame, cellSize: 64, baselinePx: 8);
        Assert.Equal(64, cell.Width);
        Assert.Equal(64, cell.Height);
        // content bbox must be horizontally centered and bottom-aligned to 64-8
        var (minX, minY, maxX, maxY) = Slicer.ContentBounds(cell)!.Value;
        Assert.Equal(64 - 8 - 1, maxY);                       // bottom on baseline
        Assert.True(System.Math.Abs((minX + maxX) / 2 - 31) <= 1); // centered ±1px
    }

    [Fact]
    public void ContentBounds_Returns_Null_For_Empty_Frame()
    {
        var empty = Filled(10, 10, new Rgba32(0, 0, 0, 0));
        Assert.Null(Slicer.ContentBounds(empty));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter SlicerTests`
Expected: compilation FAILS (`Slicer` does not exist).

- [ ] **Step 4: Implement** at `tools/SpriteSlicer/Slicer.cs`:

```csharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SpriteSlicer;

public static class Slicer
{
    /// <summary>Per-pixel distance to #FF00FF below tolerance → alpha 0.
    /// Tolerance is generous (Gemini PNGs carry compression fringe).</summary>
    public static void ChromaKey(Image<Rgba32> img, int tolerance = 40)
    {
        img.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    int d = System.Math.Abs(p.R - 255) + p.G + System.Math.Abs(p.B - 255);
                    if (d <= tolerance) row[x] = new Rgba32(0, 0, 0, 0);
                }
            }
        });
    }

    /// <summary>Cuts a row rect into equal-width frames.</summary>
    public static List<Image<Rgba32>> SliceRow(Image<Rgba32> sheet, Rectangle rowRect, int frameCount)
    {
        var frames = new List<Image<Rgba32>>(frameCount);
        int fw = rowRect.Width / frameCount;
        for (int i = 0; i < frameCount; i++)
            frames.Add(sheet.Clone(c => c.Crop(new Rectangle(rowRect.X + i * fw, rowRect.Y, fw, rowRect.Height))));
        return frames;
    }

    /// <summary>Bounding box of non-transparent pixels, or null if empty.</summary>
    public static (int minX, int minY, int maxX, int maxY)? ContentBounds(Image<Rgba32> img)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        img.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    if (row[x].A > 0)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            }
        });
        return maxX < 0 ? null : (minX, minY, maxX, maxY);
    }

    /// <summary>Crops to content, scales (nearest-neighbor) to fit the cell with
    /// a 2px margin, centers horizontally, bottom-aligns to cellSize - baselinePx.</summary>
    public static Image<Rgba32> RenderToCell(Image<Rgba32> frame, int cellSize, int baselinePx)
    {
        var cell = new Image<Rgba32>(cellSize, cellSize);
        var bounds = ContentBounds(frame);
        if (bounds is null) return cell; // empty frame → empty cell

        var (minX, minY, maxX, maxY) = bounds.Value;
        var content = frame.Clone(c => c.Crop(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)));

        int avail = cellSize - baselinePx - 2;
        double scale = System.Math.Min((double)(cellSize - 4) / content.Width, (double)avail / content.Height);
        if (scale < 1.0 || scale > 1.0) // always normalize to cell scale
            content.Mutate(c => c.Resize(
                System.Math.Max(1, (int)(content.Width * scale)),
                System.Math.Max(1, (int)(content.Height * scale)),
                KnownResamplers.NearestNeighbor));

        int px = (cellSize - content.Width) / 2;
        int py = cellSize - baselinePx - content.Height;
        cell.Mutate(c => c.DrawImage(content, new Point(px, py), 1f));
        return cell;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter SlicerTests`
Expected: 5 passing.

- [ ] **Step 6: Run the full suite** (`dotnet test`) — everything green — **then commit**

```bash
git add tools/SpriteSlicer tests/SpriteSlicer.Tests LlmRts.sln
git commit -m "feat: SpriteSlicer core - chroma key, row slicing, baseline recentering"
```

---

### Task 4: SpriteSlicer CLI + sidecars + slice the four unit sheets

Semi-manual task: the CLI is coded TDD-free (thin I/O shell over tested core), then sidecar rects are authored by eyeballing each raw sheet, then the tool runs for real.

**Files:**
- Create: `tools/SpriteSlicer/Sidecar.cs`, modify `tools/SpriteSlicer/Program.cs`
- Create: `tools/SpriteSlicer/sidecars/trooper.json`, `fabber.json`, `outrider.json`, `tank.json`
- Output: `godot/assets/units/*.png`, `godot/assets/icons/*.png`

- [ ] **Step 1: Sidecar model** at `tools/SpriteSlicer/Sidecar.cs`:

```csharp
namespace SpriteSlicer;

public sealed record RectDef(int X, int Y, int W, int H);

public sealed record RowDef(string Anim, string Facing, int Frames, RectDef Rect);

/// <summary>Hand-authored description of where content lives in a raw AI sheet.
/// Output contract: rows idle-S,idle-W,idle-N,walk-S,walk-W,walk-N,
/// attack-S,attack-W,attack-N,death-S; frames 4,4,4,6,6,6,6,6,6,6; 64px cells.</summary>
public sealed record Sidecar(
    string Source,          // raw sheet path, relative to repo root
    string Output,          // output sheet path
    string IconOutput,      // output icon path
    RectDef Icon,           // icon source rect in the raw sheet
    int BaselinePx,         // feet offset from cell bottom (default 8)
    List<RowDef> Rows);
```

- [ ] **Step 2: CLI** at `tools/SpriteSlicer/Program.cs` (replace the stub):

```csharp
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SpriteSlicer;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: SpriteSlicer <sidecar.json> [more sidecars...] (paths relative to repo root)");
    return 1;
}

// canonical output row order and frame counts (sprite sheet contract)
var contract = new (string Anim, string Facing, int Frames)[]
{
    ("idle","S",4), ("idle","W",4), ("idle","N",4),
    ("walk","S",6), ("walk","W",6), ("walk","N",6),
    ("attack","S",6), ("attack","W",6), ("attack","N",6),
    ("death","S",6),
};
const int Cell = 64;
const int SheetW = 6 * Cell; // widest row

foreach (var sidecarPath in args)
{
    var sc = JsonSerializer.Deserialize<Sidecar>(File.ReadAllText(sidecarPath),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException($"unreadable sidecar {sidecarPath}");

    using var raw = Image.Load<Rgba32>(sc.Source);
    var sheet = new Image<Rgba32>(SheetW, contract.Length * Cell);

    for (int r = 0; r < contract.Length; r++)
    {
        var (anim, facing, frames) = contract[r];
        var row = sc.Rows.FirstOrDefault(x => x.Anim == anim && x.Facing == facing)
            ?? throw new InvalidDataException($"{sidecarPath}: missing row {anim}-{facing}");
        if (row.Frames != frames)
            throw new InvalidDataException($"{sidecarPath}: {anim}-{facing} has {row.Frames} frames, contract needs {frames}");
        var rect = new Rectangle(row.Rect.X, row.Rect.Y, row.Rect.W, row.Rect.H);
        if (rect.Right > raw.Width || rect.Bottom > raw.Height)
            throw new InvalidDataException($"{sidecarPath}: {anim}-{facing} rect {rect} outside {raw.Width}x{raw.Height}");

        var cut = Slicer.SliceRow(raw, rect, frames);
        for (int f = 0; f < cut.Count; f++)
        {
            Slicer.ChromaKey(cut[f]);
            using var cell = Slicer.RenderToCell(cut[f], Cell, sc.BaselinePx);
            sheet.Mutate(c => c.DrawImage(cell, new Point(f * Cell, r * Cell), 1f));
            cut[f].Dispose();
        }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(sc.Output)!);
    sheet.SaveAsPng(sc.Output);

    using var icon = raw.Clone(c => c.Crop(new Rectangle(sc.Icon.X, sc.Icon.Y, sc.Icon.W, sc.Icon.H)));
    Slicer.ChromaKey(icon);
    icon.Mutate(c => c.Resize(64, 64, KnownResamplers.NearestNeighbor));
    Directory.CreateDirectory(Path.GetDirectoryName(sc.IconOutput)!);
    icon.SaveAsPng(sc.IconOutput);

    Console.WriteLine($"OK {sc.Source} -> {sc.Output} + {sc.IconOutput}");
}
return 0;
```

- [ ] **Step 3: Build and verify the CLI fails loudly on a bad sidecar**

```bash
dotnet build tools/SpriteSlicer
echo '{"source":"nope.png","output":"x.png","iconOutput":"i.png","icon":{"x":0,"y":0,"w":1,"h":1},"baselinePx":8,"rows":[]}' > /tmp/bad.json
dotnet run --project tools/SpriteSlicer -- /tmp/bad.json
```
Expected: non-zero exit, "file not found"-style error. Nothing half-written.

- [ ] **Step 4: Author the four sidecars (MANUAL EYEBALL WORK)**

For each of `assets/raw-sprites/trooper-v2.png`, `fabber-v1.png` (or v2, pick the cleaner), `outrider-v1.png`, `tank-v1.png`:

1. Open the sheet in an image viewer with pixel coordinates (Windows Photos zoom, or Paint's status bar).
2. For each contract row, find the matching labeled strip in the raw sheet and record its pixel rect (x, y, w, h) EXCLUDING the baked-in text label. The raw sheets are ~600px wide per the earlier review; rect heights ≈ row height minus label.
3. Raw-sheet mapping quirks to expect (from the visual review of these sheets):
   - Sheets label rows like "IDLE SOUTH", "WALK WEST", "WORK/ATTACK SOUTH", "MOVING SOUTH" (vehicles) — map MOVING→walk, WORK/ATTACK→attack, IDLE (STATIONARY)→idle.
   - Some sheets have 4-frame rows where the contract wants 6, or vice versa. If a row has MORE frames than the contract, set the rect to span only the first N contract frames. If FEWER, duplicate by widening is NOT possible — instead set `frames` to the actual count and pad: acceptable interim is repeating the rect (e.g. 4 real frames → list `"frames": 4` is a contract violation, so instead reduce rect to 2/3 width covering 4 frames and accept the tool error...). **Decision: if a raw row has fewer frames than contract, author the rect for the frames that exist and pad the sidecar by repeating the last frame: set the rect so `W = frames_present * frame_width`, then add a `"frames"` value equal to contract count is wrong — DO THIS instead: temporarily edit the CONTRACT row count down is also wrong.** The clean fix, implement it in this step: add to `Program.cs` after `var cut = Slicer.SliceRow(...)`:

```csharp
        while (cut.Count < frames) cut.Add(cut[^1].Clone()); // pad short rows by repeating last frame
```

   and relax the equality check to `if (row.Frames > frames) throw ...` (extra frames are an error; fewer get padded). Sidecar `frames` = real frames present in the raw rect.
4. Icon rect: the "UI ICON" cell each sheet has (bottom-right typically).
5. `baselinePx`: 8 for infantry; 4 for vehicles (tank/outrider sit lower).

Sidecar template (`tools/SpriteSlicer/sidecars/trooper.json`):

```json
{
  "source": "assets/raw-sprites/trooper-v2.png",
  "output": "godot/assets/units/trooper.png",
  "iconOutput": "godot/assets/icons/trooper.png",
  "icon": { "x": 530, "y": 100, "w": 60, "h": 60 },
  "baselinePx": 8,
  "rows": [
    { "anim": "idle",   "facing": "S", "frames": 4, "rect": { "x": 0,   "y": 10,  "w": 230, "h": 50 } },
    { "anim": "idle",   "facing": "W", "frames": 4, "rect": { "x": 0,   "y": 70,  "w": 230, "h": 50 } },
    { "anim": "idle",   "facing": "N", "frames": 4, "rect": { "x": 0,   "y": 130, "w": 230, "h": 50 } },
    { "anim": "walk",   "facing": "S", "frames": 6, "rect": { "x": 240, "y": 10,  "w": 350, "h": 50 } },
    { "anim": "walk",   "facing": "W", "frames": 6, "rect": { "x": 240, "y": 70,  "w": 350, "h": 50 } },
    { "anim": "walk",   "facing": "N", "frames": 6, "rect": { "x": 240, "y": 130, "w": 350, "h": 50 } },
    { "anim": "attack", "facing": "S", "frames": 6, "rect": { "x": 0,   "y": 210, "w": 230, "h": 50 } },
    { "anim": "attack", "facing": "W", "frames": 6, "rect": { "x": 240, "y": 210, "w": 350, "h": 50 } },
    { "anim": "attack", "facing": "N", "frames": 4, "rect": { "x": 0,   "y": 210, "w": 230, "h": 50 } },
    { "anim": "death",  "facing": "S", "frames": 6, "rect": { "x": 0,   "y": 270, "w": 350, "h": 50 } }
  ]
}
```

ALL rect numbers above are EXAMPLES — measure the real ones per sheet. Some raw sheets lack attack-N rows entirely; point those at the attack-S rect (acceptable v0 approximation, noted per sidecar in a `"_note"` field).

- [ ] **Step 5: Run the slicer on all four**

```bash
cd C:\Users\lssha\llm-rts
dotnet run --project tools/SpriteSlicer -- tools/SpriteSlicer/sidecars/trooper.json tools/SpriteSlicer/sidecars/fabber.json tools/SpriteSlicer/sidecars/outrider.json tools/SpriteSlicer/sidecars/tank.json
```
Expected: four `OK` lines; `godot/assets/units/*.png` are 384×640; icons 64×64.

- [ ] **Step 6: Visually inspect the output sheets** (open each PNG): frames transparent-backed, roughly centered, no label text fragments. Iterate rects until clean — this is the fiddly step; budget patience.

- [ ] **Step 7: Run full suite, then commit**

```bash
dotnet test
git add tools/SpriteSlicer godot/assets
git commit -m "feat: slicer CLI + sidecars; sliced contract sheets for 4 units"
```

### Task 5: Godot project scaffold

A Godot 4.6 .NET project that references SimCore and boots to an empty scene. Headless smoke check proves the toolchain.

**Files:**
- Create: `godot/project.godot`, `godot/LlmRts.Godot.csproj`, `godot/Main.tscn`, `godot/scripts/Main.cs`
- Modify: `LlmRts.sln`

Set once per shell:
```powershell
$env:GODOT = "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe"
```

- [ ] **Step 1: `godot/project.godot`**

```ini
config_version=5

[application]
config/name="LlmRts"
run/main_scene="res://Main.tscn"
config/features=PackedStringArray("4.6", "C#")

[dotnet]
project/assembly_name="LlmRts.Godot"

[display]
window/size/viewport_width=1600
window/size/viewport_height=900

[rendering]
textures/canvas_textures/default_texture_filter=0
```

(`default_texture_filter=0` = nearest — pixel art stays crisp.)

- [ ] **Step 2: `godot/LlmRts.Godot.csproj`**

```xml
<Project Sdk="Godot.NET.Sdk/4.6.3">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\src\SimCore\SimCore.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: `godot/Main.tscn`** (the only hand-authored scene; everything else is built in code)

```ini
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://scripts/Main.cs" id="1"]

[node name="Main" type="Node2D"]
script = ExtResource("1")
```

- [ ] **Step 4: `godot/scripts/Main.cs`** (stub for now; composition added in later tasks)

```csharp
using Godot;

namespace LlmRts.Godot;

public partial class Main : Node2D
{
    public override void _Ready()
    {
        GD.Print("LlmRts boot OK");
    }
}
```

- [ ] **Step 5: Add to solution, build, smoke-run**

```powershell
dotnet sln add godot/LlmRts.Godot.csproj
dotnet build godot/LlmRts.Godot.csproj          # must succeed without the engine
& $env:GODOT --headless --path godot --import   # imports assets, generates .godot/
& $env:GODOT --headless --path godot --quit-after 2
```
Expected: build succeeds; the run prints `LlmRts boot OK` then exits 0. If the editor asks to build solutions, run `& $env:GODOT --headless --path godot --build-solutions --quit-after 2` once.

- [ ] **Step 6: Add `godot/.godot/` to `.gitignore`, run full suite, commit**

```bash
echo "godot/.godot/" >> .gitignore
dotnet test
git add godot .gitignore LlmRts.sln
git commit -m "feat: Godot 4.6 .NET project scaffold referencing SimCore"
```

---

### Task 6: RenderMath + SimRunner + TestMap — the sim ticks inside Godot

**Files:**
- Create: `godot/scripts/RenderMath.cs`, `godot/scripts/SimRunner.cs`, `godot/scripts/TestMap.cs`
- Modify: `godot/scripts/Main.cs`

- [ ] **Step 1: `godot/scripts/RenderMath.cs`** — the ONLY place Fix meets float:

```csharp
using Godot;
using SimCore.Math;

namespace LlmRts.Godot;

public static class RenderMath
{
    public const int CellPx = 64;
    private static readonly float FixScale = Fix.FromInt(1).Raw;

    public static float ToF(Fix v) => v.Raw / FixScale;
    public static Vector2 ToPx(FixVec v) => new(ToF(v.X) * CellPx, ToF(v.Y) * CellPx);
    public static Vector2 CellToPx(int cx, int cy) => new(cx * CellPx, cy * CellPx);
    public static (int cx, int cy) PxToCell(Vector2 px) =>
        ((int)System.Math.Floor(px.X / CellPx), (int)System.Math.Floor(px.Y / CellPx));

    /// <summary>Snap a direction vector to S/W/N/E. Ties prefer horizontal.</summary>
    public static string FacingOf(Vector2 dir)
    {
        if (dir.LengthSquared() < 0.0001f) return "S";
        return System.Math.Abs(dir.X) >= System.Math.Abs(dir.Y)
            ? (dir.X < 0 ? "W" : "E")
            : (dir.Y < 0 ? "N" : "S");
    }
}
```

NOTE: `Fix.Raw`'s actual type/visibility — check `src/SimCore/Math/Fix.cs`. If `Raw` is internal, add a public `ToFloat()` is NOT allowed in sim... `Raw` is public (StateHasher and tests use it from other files; verify). If internal-only, add `public float ToFloatUnsafe()` to Fix as a render-boundary helper with a doc comment forbidding sim use, plus a one-line test.

- [ ] **Step 2: `godot/scripts/SimRunner.cs`**:

```csharp
using System.Collections.Generic;
using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

/// <summary>Owns the SimWorld. Fixed 10 ticks/s via accumulator; queued
/// commands drain into each Step. Interpolation fraction exposed for views.</summary>
public partial class SimRunner : Node
{
    public const float TickSeconds = 0.1f;

    public SimWorld World { get; private set; } = null!;
    public bool Paused { get; set; }
    public float Alpha { get; private set; }          // 0..1 fraction into current tick
    public long TickCount { get; private set; }
    public event System.Action? Ticked;               // fired after every Step

    private readonly List<Command> _queue = new();
    private float _accum;

    public void Init(SimWorld world) => World = world;

    public void Enqueue(Command c) => _queue.Add(c);

    public override void _Process(double delta)
    {
        if (Paused) return;
        _accum += (float)delta;
        while (_accum >= TickSeconds)
        {
            _accum -= TickSeconds;
            World.Step(_queue.ToArray());
            _queue.Clear();
            TickCount++;
            Ticked?.Invoke();
        }
        Alpha = _accum / TickSeconds;
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Space }) Paused = !Paused;
    }
}
```

- [ ] **Step 3: `godot/scripts/TestMap.cs`** — hardcoded sandbox world:

```csharp
using SimCore.Math;
using SimCore.Sim;

namespace LlmRts.Godot;

public static class TestMap
{
    public const int Size = 40;

    /// <summary>Two bases in opposite corners, mineral lines near each,
    /// a rock ridge across the middle with two gaps.</summary>
    public static SimWorld Build()
    {
        var w = new SimWorld(new MapGrid(Size, Size), seed: 42);

        foreach (var p in new[] { 0, 1 })
        {
            w.Players[p].Minerals = 300;
        }

        // rock ridge: vertical wall at x=20, gaps at y=8..11 and y=28..31
        for (int y = 0; y < Size; y++)
            if (y is < 8 or (> 11 and < 28) or > 31)
                w.Map.SetPassable(20, y, false);

        // player 0 base (top-left)
        w.AddCompletedBuilding(0, ReferenceSpecs.Depot, 4, 4);
        w.AddCompletedBuilding(0, ReferenceSpecs.Barracks, 8, 4);
        for (int i = 0; i < 4; i++)
            w.AddResourceNode(2, 8 + i, amount: 500);
        for (int i = 0; i < 3; i++)
            w.SpawnUnit(0, w.Map.CellCenter(6, 8 + i), ReferenceSpecs.Fabber);
        for (int i = 0; i < 4; i++)
            w.SpawnUnit(0, w.Map.CellCenter(10, 8 + i), ReferenceSpecs.Trooper);

        // player 1 base (bottom-right, mirrored)
        w.AddCompletedBuilding(1, ReferenceSpecs.Depot, 34, 34);
        w.AddCompletedBuilding(1, ReferenceSpecs.Barracks, 30, 34);
        for (int i = 0; i < 4; i++)
            w.AddResourceNode(37, 28 + i, amount: 500);
        for (int i = 0; i < 3; i++)
            w.SpawnUnit(1, w.Map.CellCenter(33, 28 + i), ReferenceSpecs.Fabber);
        for (int i = 0; i < 4; i++)
            w.SpawnUnit(1, w.Map.CellCenter(29, 28 + i), ReferenceSpecs.Trooper);

        return w;
    }
}
```

- [ ] **Step 4: Wire into `Main.cs`** (replace file):

```csharp
using Godot;

namespace LlmRts.Godot;

public partial class Main : Node2D
{
    public SimRunner Runner { get; private set; } = null!;

    public override void _Ready()
    {
        Runner = new SimRunner { Name = "SimRunner" };
        Runner.Init(TestMap.Build());
        AddChild(Runner);
        GD.Print($"LlmRts boot OK units={Runner.World.Units.Count} buildings={Runner.World.Buildings.Count}");
    }
}
```

- [ ] **Step 5: Build + smoke-run**

```powershell
dotnet build godot/LlmRts.Godot.csproj
& $env:GODOT --headless --path godot --quit-after 30
```
Expected: prints `LlmRts boot OK units=14 buildings=4`, runs ~30 frames of ticking without errors, exits 0.

- [ ] **Step 6: Full suite + commit**

```bash
dotnet test
git add godot
git commit -m "feat: SimRunner tick loop and hardcoded TestMap inside Godot"
```

---

### Task 7: SheetAnimator + ViewSync + UnitView — units on screen

**Files:**
- Create: `godot/scripts/SheetAnimator.cs`, `godot/scripts/ViewSync.cs`, `godot/scripts/UnitView.cs`, `godot/scripts/MapView.cs`
- Modify: `godot/scripts/Main.cs`

- [ ] **Step 1: `godot/scripts/SheetAnimator.cs`** — builds SpriteFrames from a contract sheet:

```csharp
using Godot;

namespace LlmRts.Godot;

/// <summary>Contract sheet layout (brief §2): 64px cells, rows
/// idle-S/W/N (4f), walk-S/W/N (6f), attack-S/W/N (6f), death-S (6f).
/// Animation names: "idle-S" etc.; East renders West flipped (caller's job).</summary>
public static class SheetAnimator
{
    private const int Cell = 64;
    private static readonly (string Name, int Row, int Frames, float Fps, bool Loop)[] Layout =
    {
        ("idle-S", 0, 4, 4f, true), ("idle-W", 1, 4, 4f, true), ("idle-N", 2, 4, 4f, true),
        ("walk-S", 3, 6, 10f, true), ("walk-W", 4, 6, 10f, true), ("walk-N", 5, 6, 10f, true),
        ("attack-S", 6, 6, 12f, false), ("attack-W", 7, 6, 12f, false), ("attack-N", 8, 6, 12f, false),
        ("death-S", 9, 6, 8f, false),
    };

    public static SpriteFrames? Load(string unitKey)
    {
        var path = $"res://assets/units/{unitKey}.png";
        if (!ResourceLoader.Exists(path)) return null; // fallback path: caller draws a silhouette
        var tex = GD.Load<Texture2D>(path);
        var frames = new SpriteFrames();
        foreach (var (name, row, count, fps, loop) in Layout)
        {
            frames.AddAnimation(name);
            frames.SetAnimationSpeed(name, fps);
            frames.SetAnimationLoop(name, loop);
            for (int f = 0; f < count; f++)
            {
                var at = new AtlasTexture { Atlas = tex, Region = new Rect2(f * Cell, row * Cell, Cell, Cell) };
                frames.AddFrame(name, at);
            }
        }
        return frames;
    }
}
```

- [ ] **Step 2: `godot/scripts/UnitView.cs`**:

```csharp
using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

public partial class UnitView : Node2D
{
    public int UnitId { get; private set; }
    public int OwnerId { get; private set; }
    public bool Selected { get; set; }

    private AnimatedSprite2D? _sprite;        // null → silhouette fallback
    private Color _fallbackColor;
    private string _facing = "S";
    private Vector2 _prevPos, _currPos;
    private int _maxHp = 1, _hp = 1;
    private bool _attacking;

    public static readonly Color[] PlayerColors = { new(0.2f, 0.45f, 1f), new(1f, 0.35f, 0.25f) };

    public void Init(Unit u, string unitKey)
    {
        UnitId = u.Id;
        OwnerId = u.OwnerId;
        _maxHp = u.Hp;
        _fallbackColor = PlayerColors[u.OwnerId];
        var frames = SheetAnimator.Load(unitKey);
        if (frames is not null)
        {
            _sprite = new AnimatedSprite2D { SpriteFrames = frames, Centered = true };
            _sprite.AnimationFinished += () => _attacking = false;
            AddChild(_sprite);
            _sprite.Play("idle-S");
        }
        _prevPos = _currPos = RenderMath.ToPx(u.Position);
        Position = _currPos;
        YSortEnabled = true;
    }

    /// <summary>Called by ViewSync after every sim tick.</summary>
    public void SyncTick(Unit u)
    {
        _prevPos = _currPos;
        _currPos = RenderMath.ToPx(u.Position);
        _hp = u.Hp;
        _maxHp = System.Math.Max(_maxHp, u.Hp);

        var delta = _currPos - _prevPos;
        if (delta.LengthSquared() > 0.01f) _facing = RenderMath.FacingOf(delta);
        else if (u.AttackTargetId != 0) { /* face target below via ViewSync-provided pos */ }

        bool moving = delta.LengthSquared() > 0.01f;
        bool justFired = u.Weapon is not null && u.Weapon.CooldownRemaining == u.Weapon.CooldownTicks;
        if (justFired) _attacking = true;

        PlayAnim(_attacking ? "attack" : moving ? "walk" : "idle");
    }

    /// <summary>Face an explicit world position (attack target).</summary>
    public void FaceToward(Vector2 worldPx)
    {
        var d = worldPx - _currPos;
        if (d.LengthSquared() > 0.01f) _facing = RenderMath.FacingOf(d);
    }

    private void PlayAnim(string baseName)
    {
        if (_sprite is null) return;
        var facing = _facing == "E" ? "W" : _facing;
        _sprite.FlipH = _facing == "E";
        var anim = baseName == "death" || baseName == "attack" && facing == "?" ? $"{baseName}-S" : $"{baseName}-{facing}";
        if (baseName == "death") anim = "death-S";
        if (_sprite.Animation != anim || !_sprite.IsPlaying())
            _sprite.Play(anim);
    }

    /// <summary>Plays death and frees itself. Called on a detached corpse copy.</summary>
    public void PlayDeathAndFree()
    {
        if (_sprite is null) { QueueFree(); return; }
        _sprite.FlipH = false;
        _sprite.Play("death-S");
        _sprite.AnimationFinished += QueueFree;
    }

    public override void _Process(double delta)
    {
        var runner = GetNodeOrNull<SimRunner>("/root/Main/SimRunner");
        if (runner is not null) Position = _prevPos.Lerp(_currPos, runner.Alpha);
        QueueRedraw();
    }

    public override void _Draw()
    {
        // silhouette fallback when no sheet
        if (_sprite is null)
        {
            DrawCircle(Vector2.Zero, 20, _fallbackColor);
            DrawCircle(Vector2.Zero, 20, Colors.Black with { A = 0.6f }, filled: false, width: 2);
        }
        // selection ring
        if (Selected)
            DrawArc(new Vector2(0, 18), 22, 0, Mathf.Tau, 32, Colors.Lime, 2);
        // health bar only when damaged
        if (_hp < _maxHp && _hp > 0)
        {
            float frac = (float)_hp / _maxHp;
            var color = frac > 0.66f ? Colors.Lime : frac > 0.33f ? Colors.Yellow : Colors.Red;
            DrawRect(new Rect2(-16, -36, 32, 4), Colors.Black);
            DrawRect(new Rect2(-16, -36, 32 * frac, 4), color);
        }
    }
}
```

- [ ] **Step 3: `godot/scripts/MapView.cs`** — terrain (flat-color fallback until brief-2 art):

```csharp
using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

public partial class MapView : Node2D
{
    private MapGrid _map = null!;
    private int _drawnVersion = -1;

    public void Init(MapGrid map) => _map = map;

    public override void _Process(double delta)
    {
        if (_map.Version != _drawnVersion) { _drawnVersion = _map.Version; QueueRedraw(); }
    }

    public override void _Draw()
    {
        const int px = RenderMath.CellPx;
        DrawRect(new Rect2(0, 0, _map.Width * px, _map.Height * px), new Color(0.42f, 0.38f, 0.32f));
        for (int y = 0; y < _map.Height; y++)
            for (int x = 0; x < _map.Width; x++)
                if (!_map.IsPassable(x, y))
                    DrawRect(new Rect2(x * px, y * px, px, px), new Color(0.22f, 0.20f, 0.18f));
        // faint grid
        for (int x = 0; x <= _map.Width; x++)
            DrawLine(new Vector2(x * px, 0), new Vector2(x * px, _map.Height * px), new Color(0, 0, 0, 0.06f));
        for (int y = 0; y <= _map.Height; y++)
            DrawLine(new Vector2(0, y * px), new Vector2(_map.Width * px, y * px), new Color(0, 0, 0, 0.06f));
    }
}
```

NOTE: impassable cells from *buildings* also satisfy `!IsPassable`; BuildingView (Task 8) draws on top, so the dark cell behind a building is invisible. Resource node cells get NodeView sprites on top likewise. `MapGrid.Version` increments on any passability change — check the property name in `src/SimCore/Sim/MapGrid.cs`.

- [ ] **Step 4: `godot/scripts/ViewSync.cs`**:

```csharp
using System.Collections.Generic;
using System.Linq;
using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

/// <summary>Diffs sim state by entity id after every tick; owns view node lifecycle.</summary>
public partial class ViewSync : Node2D
{
    private SimRunner _runner = null!;
    private readonly Dictionary<int, UnitView> _units = new();
    private readonly Dictionary<int, BuildingView> _buildings = new();
    private readonly Dictionary<int, NodeView> _nodes = new();

    /// <summary>UnitSpec identity → sheet key. Spec records compare by value, so
    /// matching against ReferenceSpecs members works for sandbox-spawned units.</summary>
    private static string KeyOf(Unit u) =>
        u.Harvester is not null ? "fabber"
        : u.Weapon is null ? "trooper"
        : u.Weapon.CooldownTicks >= 20 ? "tank"
        : u.Weapon.CooldownTicks <= 5 ? "outrider"
        : "trooper";

    public IReadOnlyDictionary<int, UnitView> Units => _units;
    public IReadOnlyDictionary<int, BuildingView> Buildings => _buildings;

    public void Init(SimRunner runner)
    {
        _runner = runner;
        runner.Ticked += OnTick;
        YSortEnabled = true;
        OnTick(); // initial population
    }

    private void OnTick()
    {
        var w = _runner.World;

        // units
        var live = new HashSet<int>();
        foreach (var u in w.Units)
        {
            live.Add(u.Id);
            if (!_units.TryGetValue(u.Id, out var v))
            {
                v = new UnitView();
                AddChild(v);
                v.Init(u, KeyOf(u));
                _units[u.Id] = v;
            }
            v.SyncTick(u);
            if (u.AttackTargetId != 0)
            {
                var tu = w.GetUnit(u.AttackTargetId);
                if (tu is not null) v.FaceToward(RenderMath.ToPx(tu.Position));
                else
                {
                    var tb = w.GetBuilding(u.AttackTargetId);
                    if (tb is not null) v.FaceToward(RenderMath.CellToPx(tb.CellX, tb.CellY));
                }
            }
        }
        foreach (var id in _units.Keys.Where(id => !live.Contains(id)).ToList())
        {
            _units[id].PlayDeathAndFree(); // node stays as corpse until anim ends
            _units.Remove(id);
        }

        // buildings
        var liveB = new HashSet<int>();
        foreach (var b in w.Buildings)
        {
            liveB.Add(b.Id);
            if (!_buildings.TryGetValue(b.Id, out var v))
            {
                v = new BuildingView();
                AddChild(v);
                v.Init(b);
                _buildings[b.Id] = v;
            }
            v.SyncTick(b);
        }
        foreach (var id in _buildings.Keys.Where(id => !liveB.Contains(id)).ToList())
        {
            _buildings[id].PlayDestructionAndFree();
            _buildings.Remove(id);
        }

        // resource nodes
        var liveN = new HashSet<int>();
        foreach (var n in w.Nodes)
        {
            liveN.Add(n.Id);
            if (!_nodes.TryGetValue(n.Id, out var v))
            {
                v = new NodeView();
                AddChild(v);
                v.Init(n);
                _nodes[n.Id] = v;
            }
        }
        foreach (var id in _nodes.Keys.Where(id => !liveN.Contains(id)).ToList())
        {
            _nodes[id].QueueFree();
            _nodes.Remove(id);
        }
    }
}
```

NOTE on `KeyOf`: heuristic spec→sheet mapping keyed off distinguishing stats. Fragile by design but contained in one function; replace with a proper spec-id when faction packs land (plan 3). If tuning changes make two specs collide on these stats, update the heuristic in the same commit as the tuning change.

- [ ] **Step 5: Minimal `BuildingView`/`NodeView` stubs so this task compiles** (full versions in Task 8). `godot/scripts/BuildingView.cs`:

```csharp
using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

public partial class BuildingView : Node2D
{
    protected Building B = null!;
    public int BuildingId => B.Id;
    public void Init(Building b) { B = b; Position = RenderMath.CellToPx(b.CellX, b.CellY); }
    public virtual void SyncTick(Building b) { B = b; QueueRedraw(); }
    public virtual void PlayDestructionAndFree() => QueueFree();
    public override void _Draw() =>
        DrawRect(new Rect2(0, 0, B.Spec.Width * RenderMath.CellPx, B.Spec.Height * RenderMath.CellPx),
            UnitView.PlayerColors[B.OwnerId] with { A = 0.5f });
}
```

`godot/scripts/NodeView.cs`:

```csharp
using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

public partial class NodeView : Node2D
{
    public void Init(ResourceNode n)
    {
        Position = RenderMath.CellToPx(n.CellX, n.CellY) + new Vector2(32, 32);
        QueueRedraw();
    }
    public override void _Draw()
    {
        var pts = new Vector2[] { new(0, -20), new(16, 0), new(0, 20), new(-16, 0) };
        DrawColoredPolygon(pts, new Color(0.4f, 0.8f, 1f));
    }
}
```

- [ ] **Step 6: Wire into `Main._Ready`** (after `AddChild(Runner)`):

```csharp
        var mapView = new MapView { Name = "MapView" };
        mapView.Init(Runner.World.Map);
        AddChild(mapView);

        var viewSync = new ViewSync { Name = "ViewSync" };
        AddChild(viewSync);
        viewSync.Init(Runner);
```

- [ ] **Step 7: Build + run windowed (first visual check)**

```powershell
dotnet build godot/LlmRts.Godot.csproj
& $env:GODOT --path godot
```
Expected: window opens; terrain + ridge visible; 14 units rendered with sprites (or colored circles for any sheet that failed slicing); idle animations playing. Close the window. Headless variant for CI-style check: `& $env:GODOT --headless --path godot --quit-after 60`.

- [ ] **Step 8: Full suite + commit**

```bash
dotnet test
git add godot
git commit -m "feat: ViewSync renders units/buildings/nodes with interpolation"
```

### Task 8: BuildingView with construction/wreck states + CameraRig

**Files:**
- Modify: `godot/scripts/BuildingView.cs` (replace stub)
- Create: `godot/scripts/CameraRig.cs`
- Modify: `godot/scripts/Main.cs`

- [ ] **Step 1: Replace `godot/scripts/BuildingView.cs`** — silhouette fallback per spec (no building art exists yet; when brief-2 sheets arrive a texture path can be added the same way `SheetAnimator` checks `ResourceLoader.Exists`):

```csharp
using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

/// <summary>Silhouette-fallback building rendering: colored footprint,
/// construction progress shading, glyph, destruction flash-out.</summary>
public partial class BuildingView : Node2D
{
    private Building _b = null!;
    private bool _isDepot, _canTrain;
    private int _ownerId, _maxHp;
    private float _progress; // 0..1 construction
    private int _hp;
    private Vector2 _sizePx;

    public int BuildingId { get; private set; }

    public void Init(Building b)
    {
        _b = b;
        BuildingId = b.Id;
        _ownerId = b.OwnerId;
        _isDepot = b.Spec.IsDepot;
        _canTrain = b.Spec.CanTrain;
        _maxHp = b.Spec.MaxHp;
        _sizePx = new Vector2(b.Spec.Width * RenderMath.CellPx, b.Spec.Height * RenderMath.CellPx);
        Position = RenderMath.CellToPx(b.CellX, b.CellY);
        SyncTick(b);
    }

    public void SyncTick(Building b)
    {
        _b = b;
        _hp = b.Hp;
        _progress = b.IsComplete ? 1f : (float)b.BuildProgress / b.Spec.BuildTimeTicks;
        QueueRedraw();
    }

    public void PlayDestructionAndFree()
    {
        // simple flash-out: dark rubble rect fading over 0.8s, then free
        var tween = CreateTween();
        Modulate = new Color(0.2f, 0.18f, 0.16f);
        tween.TweenProperty(this, "modulate:a", 0f, 0.8f);
        tween.TweenCallback(Callable.From(QueueFree));
    }

    public override void _Draw()
    {
        var baseColor = UnitView.PlayerColors[_ownerId];
        // under construction: dimmed body fills bottom-up with progress
        DrawRect(new Rect2(Vector2.Zero, _sizePx), baseColor with { A = 0.25f });
        var filledH = _sizePx.Y * _progress;
        DrawRect(new Rect2(0, _sizePx.Y - filledH, _sizePx.X, filledH), baseColor with { A = 0.85f });
        DrawRect(new Rect2(Vector2.Zero, _sizePx), Colors.Black, filled: false, width: 2);

        // glyph: D / B
        var glyph = _isDepot ? "D" : _canTrain ? "B" : "?";
        DrawString(ThemeDB.FallbackFont, _sizePx / 2 + new Vector2(-8, 10), glyph,
            HorizontalAlignment.Center, -1, 28, Colors.White);

        // health bar when damaged
        if (_hp < _maxHp && _hp > 0)
        {
            float frac = (float)_hp / _maxHp;
            var c = frac > 0.66f ? Colors.Lime : frac > 0.33f ? Colors.Yellow : Colors.Red;
            DrawRect(new Rect2(4, -10, _sizePx.X - 8, 5), Colors.Black);
            DrawRect(new Rect2(4, -10, (_sizePx.X - 8) * frac, 5), c);
        }

        // production queue pips (head item progress drawn in HUD; here just count)
        for (int i = 0; i < _b.Queue.Count; i++)
            DrawRect(new Rect2(4 + i * 10, _sizePx.Y + 4, 8, 8), Colors.White with { A = 0.8f });
    }
}
```

- [ ] **Step 2: `godot/scripts/CameraRig.cs`**:

```csharp
using Godot;

namespace LlmRts.Godot;

public partial class CameraRig : Camera2D
{
    private const float PanSpeed = 900f;
    private const int EdgePx = 12;
    private static readonly float[] ZoomSteps = { 0.5f, 1f, 2f };
    private int _zoomIdx = 1;

    public override void _Ready()
    {
        MakeCurrent();
        Position = new Vector2(8 * RenderMath.CellPx, 8 * RenderMath.CellPx); // player 0 base
        ApplyZoom();
    }

    public override void _Process(double delta)
    {
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        if (Input.IsKeyPressed(Key.A) && !Input.IsKeyPressed(Key.Shift)) { } // A reserved for attack-move when combined with click; bare A still pans:
        if (Input.IsKeyPressed(Key.A)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.D)) dir.X += 1;

        var vp = GetViewport().GetVisibleRect().Size;
        var mouse = GetViewport().GetMousePosition();
        if (mouse.X <= EdgePx) dir.X -= 1;
        if (mouse.X >= vp.X - EdgePx) dir.X += 1;
        if (mouse.Y <= EdgePx) dir.Y -= 1;
        if (mouse.Y >= vp.Y - EdgePx) dir.Y += 1;

        if (dir != Vector2.Zero)
            Position += dir.Normalized() * PanSpeed * (float)delta / Zoom.X;

        var limit = TestMap.Size * RenderMath.CellPx;
        Position = new Vector2(Mathf.Clamp(Position.X, 0, limit), Mathf.Clamp(Position.Y, 0, limit));
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp && _zoomIdx < ZoomSteps.Length - 1) { _zoomIdx++; ApplyZoom(); }
            if (mb.ButtonIndex == MouseButton.WheelDown && _zoomIdx > 0) { _zoomIdx--; ApplyZoom(); }
        }
    }

    private void ApplyZoom() => Zoom = Vector2.One * ZoomSteps[_zoomIdx];
}
```

NOTE: WASD pan conflicts with A-for-attack-move. Resolution implemented in Task 10: `CommandController` consumes the A keypress to arm attack-move mode and pans are fine to also fire (a brief pan while arming is acceptable for the sandbox; refine only if it annoys in playtesting).

- [ ] **Step 3: Add to `Main._Ready`:** `AddChild(new CameraRig { Name = "Camera" });`

- [ ] **Step 4: Build + run** (`& $env:GODOT --path godot`). Verify: bases render as colored squares with D/B glyphs, camera pans (WASD + edges), wheel zooms in steps, buildings show progress when one is under construction (verifiable after Task 11). Close.

- [ ] **Step 5: Full suite + commit**

```bash
dotnet test
git add godot
git commit -m "feat: building silhouette views and RTS camera rig"
```

---

### Task 9: SelectionController — click, box, shift, Tab

**Files:**
- Create: `godot/scripts/SelectionController.cs`
- Modify: `godot/scripts/Main.cs`

- [ ] **Step 1: `godot/scripts/SelectionController.cs`**:

```csharp
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LlmRts.Godot;

/// <summary>Owns "who is selected" and "which player am I". Draws the drag box.</summary>
public partial class SelectionController : Node2D
{
    public int ControlledPlayer { get; private set; }
    public readonly HashSet<int> SelectedUnits = new();
    public int SelectedBuilding { get; private set; } // 0 = none

    private ViewSync _view = null!;
    private SimRunner _runner = null!;
    private bool _dragging;
    private Vector2 _dragStart;

    public event System.Action? SelectionChanged;
    public event System.Action? PlayerSwitched;

    public void Init(ViewSync view, SimRunner runner) { _view = view; _runner = runner; }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey { Pressed: true, Keycode: Key.Tab }:
                ControlledPlayer = 1 - ControlledPlayer;
                Clear();
                PlayerSwitched?.Invoke();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } down:
                _dragging = true;
                _dragStart = GetGlobalMousePosition();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                if (!_dragging) break;
                _dragging = false;
                var end = GetGlobalMousePosition();
                bool additive = Input.IsKeyPressed(Key.Shift);
                if (_dragStart.DistanceTo(end) < 8) ClickSelect(end, additive);
                else BoxSelect(new Rect2(_dragStart, Vector2.Zero).Expand(end), additive);
                QueueRedraw();
                GetViewport().SetInputAsHandled();
                break;
        }
        if (_dragging) QueueRedraw();
    }

    private void ClickSelect(Vector2 worldPx, bool additive)
    {
        if (!additive) Clear();
        // nearest own unit within 24px, else own building under cursor
        var hit = _view.Units.Values
            .Where(v => v.OwnerId == ControlledPlayer && v.Position.DistanceTo(worldPx) < 24)
            .OrderBy(v => v.Position.DistanceTo(worldPx))
            .FirstOrDefault();
        if (hit is not null) { SelectedUnits.Add(hit.UnitId); }
        else
        {
            var (cx, cy) = RenderMath.PxToCell(worldPx);
            var b = _runner.World.Buildings.FirstOrDefault(b =>
                b.OwnerId == ControlledPlayer &&
                cx >= b.CellX && cx < b.CellX + b.Spec.Width &&
                cy >= b.CellY && cy < b.CellY + b.Spec.Height);
            if (b is not null) SelectedBuilding = b.Id;
        }
        ApplyHighlights();
    }

    private void BoxSelect(Rect2 box, bool additive)
    {
        if (!additive) Clear();
        foreach (var v in _view.Units.Values)
            if (v.OwnerId == ControlledPlayer && box.HasPoint(v.Position))
                SelectedUnits.Add(v.UnitId);
        ApplyHighlights();
    }

    private void Clear()
    {
        SelectedUnits.Clear();
        SelectedBuilding = 0;
        ApplyHighlights();
    }

    private void ApplyHighlights()
    {
        foreach (var v in _view.Units.Values) v.Selected = SelectedUnits.Contains(v.UnitId);
        SelectionChanged?.Invoke();
    }

    /// <summary>Drop ids of units that died; called each tick by Main.</summary>
    public void PruneDead()
    {
        SelectedUnits.RemoveWhere(id => !_view.Units.ContainsKey(id));
        if (SelectedBuilding != 0 && _runner.World.GetBuilding(SelectedBuilding) is null) SelectedBuilding = 0;
    }

    public override void _Draw()
    {
        if (_dragging)
        {
            var box = new Rect2(_dragStart, Vector2.Zero).Expand(GetGlobalMousePosition());
            DrawRect(box, Colors.Lime with { A = 0.15f });
            DrawRect(box, Colors.Lime, filled: false, width: 1);
        }
    }

    public override void _Process(double delta) { if (_dragging) QueueRedraw(); }
}
```

- [ ] **Step 2: Wire into `Main._Ready`** (keep references; order matters — selection before commands):

```csharp
        Selection = new SelectionController { Name = "Selection" };
        AddChild(Selection);
        Selection.Init(viewSync, Runner);
        Runner.Ticked += Selection.PruneDead;
```

Add `public SelectionController Selection { get; private set; } = null!;` to `Main`.

- [ ] **Step 3: Build + run.** Verify: click-select own unit (ring appears), box-select group, Shift adds, clicking enemy units does nothing, Tab switches side (rings clear; selecting the other army now works). Close.

- [ ] **Step 4: Full suite + commit**

```bash
dotnet test
git add godot
git commit -m "feat: selection - click, box, shift-add, Tab player switch"
```

---

### Task 10: CommandController — orders, attack-move, build ghost

**Files:**
- Create: `godot/scripts/CommandController.cs`
- Modify: `godot/scripts/Main.cs`

- [ ] **Step 1: `godot/scripts/CommandController.cs`**:

```csharp
using System.Linq;
using Godot;
using SimCore.Math;
using SimCore.Sim;

namespace LlmRts.Godot;

/// <summary>Turns input + current selection into sim Commands. Owns the two
/// pending modes: attack-move (A) and build-ghost placement.</summary>
public partial class CommandController : Node2D
{
    private SimRunner _runner = null!;
    private SelectionController _sel = null!;
    private ViewSync _view = null!;

    private bool _attackMoveArmed;
    private BuildingSpec? _ghostSpec;     // non-null → placement mode

    public void Init(SimRunner runner, SelectionController sel, ViewSync view)
    {
        _runner = runner; _sel = sel; _view = view;
    }

    public void ArmBuildGhost(BuildingSpec spec) => _ghostSpec = spec;

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey { Pressed: true, Keycode: Key.A } when _sel.SelectedUnits.Count > 0:
                _attackMoveArmed = true;
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                _attackMoveArmed = false;
                _ghostSpec = null;
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } when _attackMoveArmed:
                IssueAttackMove(GetGlobalMousePosition());
                _attackMoveArmed = false;
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } when _ghostSpec is not null:
                TryPlaceGhost(GetGlobalMousePosition());
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                if (_ghostSpec is not null) { _ghostSpec = null; QueueRedraw(); }
                else ContextOrder(GetGlobalMousePosition());
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private int[] SelectedIds() => _sel.SelectedUnits.ToArray();

    private FixVec ToSim(Vector2 px) =>
        FixVec.FromInts(0, 0) + new FixVec(
            Fix.FromFraction((int)(px.X * 1000), 1000 * RenderMath.CellPx),
            Fix.FromFraction((int)(px.Y * 1000), 1000 * RenderMath.CellPx));

    private void ContextOrder(Vector2 worldPx)
    {
        if (_sel.SelectedUnits.Count == 0) return;
        var w = _runner.World;
        var p = _sel.ControlledPlayer;
        var ids = SelectedIds();

        // enemy unit under cursor?
        var enemy = _view.Units.Values.FirstOrDefault(v =>
            v.OwnerId != p && v.Position.DistanceTo(worldPx) < 24);
        if (enemy is not null) { _runner.Enqueue(new AttackCommand(p, ids, enemy.UnitId)); return; }

        var (cx, cy) = RenderMath.PxToCell(worldPx);

        // enemy building?
        var eb = w.Buildings.FirstOrDefault(b => b.OwnerId != p &&
            cx >= b.CellX && cx < b.CellX + b.Spec.Width && cy >= b.CellY && cy < b.CellY + b.Spec.Height);
        if (eb is not null) { _runner.Enqueue(new AttackCommand(p, ids, eb.Id)); return; }

        // resource node? harvesters harvest, the rest move
        var node = w.Nodes.FirstOrDefault(n => n.CellX == cx && n.CellY == cy);
        if (node is not null)
        {
            var harvesters = ids.Where(id => w.GetUnit(id)?.Harvester is not null).ToArray();
            var rest = ids.Except(harvesters).ToArray();
            if (harvesters.Length > 0) _runner.Enqueue(new HarvestCommand(p, harvesters, node.Id));
            if (rest.Length > 0) _runner.Enqueue(new MoveCommand(p, rest, ToSim(worldPx)));
            return;
        }

        _runner.Enqueue(new MoveCommand(p, ids, ToSim(worldPx)));
    }

    private void IssueAttackMove(Vector2 worldPx) =>
        _runner.Enqueue(new AttackMoveCommand(_sel.ControlledPlayer, SelectedIds(), ToSim(worldPx)));

    private void TryPlaceGhost(Vector2 worldPx)
    {
        var spec = _ghostSpec!;
        var (cx, cy) = RenderMath.PxToCell(worldPx);
        var w = _runner.World;
        var worker = SelectedIds().Select(w.GetUnit).FirstOrDefault(u => u?.Harvester is not null);
        if (worker is null) { _ghostSpec = null; QueueRedraw(); return; }
        _runner.Enqueue(new BuildCommand(_sel.ControlledPlayer, worker.Id, spec, cx, cy));
        _ghostSpec = null;
        QueueRedraw();
    }

    public override void _Process(double delta) { if (_ghostSpec is not null) QueueRedraw(); }

    public override void _Draw()
    {
        if (_ghostSpec is null) return;
        var (cx, cy) = RenderMath.PxToCell(GetGlobalMousePosition());
        bool ok = FootprintFree(cx, cy, _ghostSpec.Width, _ghostSpec.Height);
        var rect = new Rect2(RenderMath.CellToPx(cx, cy),
            new Vector2(_ghostSpec.Width * RenderMath.CellPx, _ghostSpec.Height * RenderMath.CellPx));
        DrawRect(rect, (ok ? Colors.Lime : Colors.Red) with { A = 0.35f });
        DrawRect(rect, ok ? Colors.Lime : Colors.Red, filled: false, width: 2);
    }

    /// <summary>View-side placement preview: all cells in-bounds and passable.
    /// The sim's own check (FootprintPlaceable) remains authoritative at command time.</summary>
    private bool FootprintFree(int cx, int cy, int wCells, int hCells)
    {
        var map = _runner.World.Map;
        for (int y = cy; y < cy + hCells; y++)
            for (int x = cx; x < cx + wCells; x++)
                if (x < 0 || y < 0 || x >= map.Width || y >= map.Height || !map.IsPassable(x, y))
                    return false;
        return true;
    }
}
```

NOTE on `ToSim`: check `FixVec`/`Fix` for an exact factory (e.g. `Fix.FromFraction(numerator, denominator)` exists — verify a `FixVec(Fix, Fix)` constructor is public in `src/SimCore/Math/FixVec.cs`; if only `FromInts` exists, add a public `FromFix(Fix x, Fix y)` or use the constructor). Precision via FromFraction(px*1000, 64000) is sub-cell and good enough for click targets.

- [ ] **Step 2: Wire into `Main._Ready`:**

```csharp
        Commands = new CommandController { Name = "Commands" };
        AddChild(Commands);
        Commands.Init(Runner, Selection, viewSync);
```

Add `public CommandController Commands { get; private set; } = null!;` to `Main`.

- [ ] **Step 3: Build + run. Manual verification:**
- Right-click ground → units path there (walk anims, interpolated movement).
- Right-click enemy trooper → units chase and shoot; target flashes HP bar; corpses play death row.
- A + click beyond the ridge → units attack-move, engaging what they meet at the gaps.
- Right-click mineral node with Fabbers selected → harvest loop runs; watch minerals climb (HUD comes next task — check via building queue pips or debug print).
- Tab → command the other army to fight back.

- [ ] **Step 4: Full suite + commit**

```bash
dotnet test
git add godot
git commit -m "feat: context orders, attack-move, build ghost placement"
```

---

### Task 11: HUD — resources, selection panel, build/train buttons

**Files:**
- Create: `godot/scripts/Hud.cs`
- Modify: `godot/scripts/Main.cs`

- [ ] **Step 1: `godot/scripts/Hud.cs`**:

```csharp
using System.Linq;
using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

public partial class Hud : CanvasLayer
{
    private SimRunner _runner = null!;
    private SelectionController _sel = null!;
    private CommandController _cmd = null!;

    private Label _resources = null!;
    private Label _playerBadge = null!;
    private Label _selectionInfo = null!;
    private HBoxContainer _buttons = null!;
    private ProgressBar _queueBar = null!;

    public void Init(SimRunner runner, SelectionController sel, CommandController cmd)
    {
        _runner = runner; _sel = sel; _cmd = cmd;

        _playerBadge = new Label { Position = new Vector2(12, 8) };
        AddChild(_playerBadge);

        _resources = new Label { AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -260, OffsetTop = 8 };
        AddChild(_resources);

        var panel = new PanelContainer
        {
            AnchorTop = 1, AnchorBottom = 1, AnchorLeft = 0, AnchorRight = 1,
            OffsetTop = -86, OffsetLeft = 8, OffsetRight = -8, OffsetBottom = -8,
        };
        AddChild(panel);
        var row = new HBoxContainer();
        panel.AddChild(row);
        _selectionInfo = new Label { CustomMinimumSize = new Vector2(320, 0) };
        row.AddChild(_selectionInfo);
        _buttons = new HBoxContainer();
        row.AddChild(_buttons);
        _queueBar = new ProgressBar { CustomMinimumSize = new Vector2(140, 16), Visible = false, MaxValue = 1.0 };
        row.AddChild(_queueBar);

        _sel.SelectionChanged += RebuildButtons;
        _sel.PlayerSwitched += RebuildButtons;
        RebuildButtons();
    }

    public override void _Process(double delta)
    {
        var p = _runner.World.Players[_sel.ControlledPlayer];
        _resources.Text = $"Minerals {p.Minerals}   Supply {p.SupplyUsed}/{p.SupplyCap}" +
                          (_runner.Paused ? "   [PAUSED]" : "");
        _playerBadge.Text = $"Commanding: Player {_sel.ControlledPlayer + 1}";
        _playerBadge.Modulate = UnitView.PlayerColors[_sel.ControlledPlayer];
        UpdateSelectionInfo();
    }

    private void UpdateSelectionInfo()
    {
        var w = _runner.World;
        if (_sel.SelectedBuilding != 0 && w.GetBuilding(_sel.SelectedBuilding) is { } b)
        {
            var kind = b.Spec.IsDepot ? "Depot" : b.Spec.CanTrain ? "Barracks" : "Building";
            _selectionInfo.Text = $"{kind}  HP {b.Hp}/{b.Spec.MaxHp}" +
                (b.IsComplete ? $"  queue {b.Queue.Count}/{Building.MaxQueueLength}" : "  [constructing]");
            if (b.Queue.Count > 0)
            {
                _queueBar.Visible = true;
                var head = b.Queue[0];
                _queueBar.Value = 1.0 - (double)head.RemainingTicks / head.Spec.BuildTimeTicks;
            }
            else _queueBar.Visible = false;
            return;
        }
        _queueBar.Visible = false;
        if (_sel.SelectedUnits.Count == 0) { _selectionInfo.Text = ""; return; }
        var units = _sel.SelectedUnits.Select(w.GetUnit).Where(u => u is not null).ToList();
        if (units.Count == 1)
        {
            var u = units[0]!;
            var carry = u.Harvester is not null ? $"  carrying {u.CarriedMinerals}" : "";
            _selectionInfo.Text = $"Unit #{u.Id}  HP {u.Hp}{carry}";
        }
        else _selectionInfo.Text = $"{units.Count} units selected";
    }

    private void RebuildButtons()
    {
        foreach (var c in _buttons.GetChildren()) c.QueueFree();
        var w = _runner.World;
        var p = _sel.ControlledPlayer;

        bool workerSelected = _sel.SelectedUnits.Select(w.GetUnit).Any(u => u?.Harvester is not null);
        if (workerSelected)
        {
            AddButton($"Build Depot ({ReferenceSpecs.Depot.MineralCost})",
                () => _cmd.ArmBuildGhost(ReferenceSpecs.Depot));
            AddButton($"Build Barracks ({ReferenceSpecs.Barracks.MineralCost})",
                () => _cmd.ArmBuildGhost(ReferenceSpecs.Barracks));
        }

        if (_sel.SelectedBuilding != 0 && w.GetBuilding(_sel.SelectedBuilding) is { IsComplete: true, Spec.CanTrain: true } b)
        {
            AddButton($"Trooper ({ReferenceSpecs.Trooper.MineralCost})",
                () => _runner.Enqueue(new TrainCommand(p, b.Id, ReferenceSpecs.Trooper)));
            AddButton($"Outrider ({ReferenceSpecs.Outrider.MineralCost})",
                () => _runner.Enqueue(new TrainCommand(p, b.Id, ReferenceSpecs.Outrider)));
            AddButton($"Tank ({ReferenceSpecs.Tank.MineralCost})",
                () => _runner.Enqueue(new TrainCommand(p, b.Id, ReferenceSpecs.Tank)));
        }
    }

    private void AddButton(string text, System.Action onPress)
    {
        var btn = new Button { Text = text };
        btn.Pressed += onPress;
        _buttons.AddChild(btn);
    }
}
```

- [ ] **Step 2: Wire into `Main._Ready`:**

```csharp
        var hud = new Hud { Name = "Hud" };
        AddChild(hud);
        hud.Init(Runner, Selection, Commands);
```

- [ ] **Step 3: Build + run. Manual verification:**
- Minerals/supply shown top-right, change while harvesting/training. Tab updates the badge AND the resource readout to the other player.
- Fabber selected → build buttons; ghost follows mouse green/red; placing deducts minerals; construction fill animates; Fabber must be near the site (sim rejects otherwise — expected, it's the range rule).
- Barracks selected → train buttons; queue pips under the building; progress bar on head item; trained units pop out adjacent.
- Train past supply → sim silently rejects (button does nothing) — acceptable for sandbox.

- [ ] **Step 4: Full suite + commit**

```bash
dotnet test
git add godot
git commit -m "feat: HUD - resources, selection panel, build and train buttons"
```

---

### Task 12: Final pass — full manual playtest, README, memory

**Files:**
- Create: `godot/README.md`
- Modify: `.github/workflows/*` (only if a build job exists — add Godot csproj build; do NOT add engine-dependent steps)

- [ ] **Step 1: Full manual playtest checklist** (run `& $env:GODOT --path godot`):

1. Boot: 14 units, 4 buildings, 8 nodes, ridge. No errors in output.
2. Selection: click, box, shift-add, click-empty clears, Tab switches.
3. Move: group pathing around the ridge through gaps; arrival stops cleanly.
4. Attack: explicit attack chases without leash; attack-move engages at gap; mutual combat kills both ways; corpses play death anims; selection prunes dead.
5. Economy: 3 Fabbers harvesting (watch the cycle: node→gather pause→return→deposit); minerals climb; node depletes after enough trips and its cell opens up (units path through where it was).
6. Build: depot + barracks placement, ghost red on ridge/units/nodes, construction progress, completed depot raises supply cap.
7. Train: queue 5 (6th rejected), progress bar, spawn adjacency, supply enforcement.
8. Combat vs buildings: attack enemy barracks; it flashes out into fade; map cells under it become passable.
9. Tank: train one, verify slow/beefy/hard-hitting feel.
10. Pause (Space): world freezes, orders still queue, unpause executes them.
11. Performance: train ~30 units, big fight at a gap — should hold 60fps (sim is microseconds/tick; view is the only suspect if not).

Record tuning impressions (speeds, cooldowns, costs) in the README for the next session — DO NOT live-tune in this task.

- [ ] **Step 2: `godot/README.md`:**

```markdown
# LlmRts — Godot presentation layer

Playable sandbox over the deterministic C# sim core (`src/SimCore`).

## Run

Requires Godot 4.6 .NET (the winget install, NOT the Steam build — Steam's has no C# support):

    $env:GODOT = "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe"
    & $env:GODOT --path godot

## Controls

| Input | Action |
|---|---|
| Left click / drag | select / box-select (Shift adds) |
| Right click | context: move / attack / harvest |
| A + left click | attack-move |
| Tab | switch controlled player |
| Esc | cancel attack-move or build ghost |
| WASD / arrows / screen edge | pan camera |
| Mouse wheel | zoom steps |
| Space | pause sim (orders still queue) |

## Architecture

Sim-authoritative: `SimRunner` steps the sim at 10 ticks/s; `ViewSync` diffs
state by id; views interpolate at 60 fps. Floats exist only in `RenderMath`.
Spec: `docs/superpowers/specs/2026-06-10-godot-playable-slice-design.md`.

## Tuning notes (fill during playtesting)

- [ ] unit speeds:
- [ ] attack rhythm:
- [ ] costs/build times:
```

- [ ] **Step 3: CI check** — open `.github/workflows/` and IF a workflow runs `dotnet build`/`dotnet test` on the solution, confirm it still passes with the three new projects (Godot.NET.Sdk restores from NuGet without the engine; SpriteSlicer.Tests adds coverage). Run locally what CI runs (e.g. `dotnet test --configuration Release`).

- [ ] **Step 4: Full suite both configs + commit**

```bash
dotnet test
dotnet test --configuration Release
git add godot/README.md .github
git commit -m "docs: godot layer README with controls and tuning checklist"
```

- [ ] **Step 5: Update auto-memory** (`C:\Users\lssha\.claude\projects\C--Users-lssha-LYHOA\memory\llm-rts-project.md`): playable slice merged, plan ordering now 2c-fog/3-packs after playtest feedback, tuning notes location.

---

## Self-review notes (already applied)

- **Spec coverage:** controls/HUD ✅ (Tasks 9–11), view sync + interpolation ✅ (6–7), slicer ✅ (3–4), fallback art ✅ (7–8), test map ✅ (6), ReferenceSpecs/Tank ✅ (1), setup API ✅ (2), smoke check ✅ (5), manual checklist ✅ (12). Pause appears in spec architecture section → Task 6 + checklist 10.
- **Known intentional gaps vs spec:** destination-flag tick and attack-flash feedback (spec "Feedback" bullet) are folded into selection rings/health bars only — flag + flash are 20-line additions; deferred to playtest feedback rather than speculative polish. If they're missed, add to UnitView/CommandController.
- **Verify-against-codebase notes:** Tasks 1 (WeaponSpec ctor shape), 2 (PlaceBuilding return/completion fields), 6 (Fix.Raw visibility, MapGrid.Version name), 10 (FixVec ctor) — each flagged inline. The implementing engineer MUST check those before coding the step; the sim is the source of truth, not this plan.


