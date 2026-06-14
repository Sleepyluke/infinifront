# Faction Pack Serialization & Loading Implementation Plan (3d-1)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `FactionDef` round-trip to/from JSON so a faction pack on disk can become a playable in-engine faction, and export the in-code reference faction as the first data pack.

**Architecture:** A new `SimCore.Packs` class library (keeps `SimCore` JSON-free) holds a `JsonConverter<Fix>` (decimal-based, never `Fix.ToString`), plain DTO records mirroring the catalog, a `PackMapper` (DTO ↔ `FactionDef`, feeding the ctor ordered lists), and a `FactionPackLoader` that parses text → `FactionDef` and runs the existing `Validate()`, returning a result-with-errors. Tests live in the existing `SimCore.Tests` project.

**Tech Stack:** C# / .NET 8, `System.Text.Json` (in the shared framework — no NuGet package needed), xUnit.

**Source spec:** `docs/superpowers/specs/2026-06-13-pack-serialization-design.md`

---

## Conventions for every task

- **Run from the repo root** `C:\Users\lssha\llm-rts`.
- If `dotnet` is not found, prepend it to PATH first: PowerShell `$env:Path += ';C:\Program Files\dotnet'`, or bash `export PATH="$PATH:/c/Program Files/dotnet"`.
- Run tests with: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
- **Baseline:** 224 SimCore tests pass at the start of this plan. Each task adds tests; the count only grows.
- After each commit, confirm `git log --oneline -1` shows your commit (foreground-commit discipline).

## File Structure (created by this plan)

- `src/SimCore.Packs/SimCore.Packs.csproj` — new class library, references `SimCore`.
- `src/SimCore.Packs/FixJsonConverter.cs` — `JsonConverter<Fix>` (decimal exact).
- `src/SimCore.Packs/PackJson.cs` — shared `JsonSerializerOptions`.
- `src/SimCore.Packs/PackDtos.cs` — DTO records (the wire shape).
- `src/SimCore.Packs/PackMapper.cs` — DTO ↔ `FactionDef`.
- `src/SimCore.Packs/FactionPackLoader.cs` — `LoadFromJson` / `ToJson` + `PackLoadResult`.
- `tests/SimCore.Tests/Packs/FixJsonConverterTests.cs`
- `tests/SimCore.Tests/Packs/PackDtoSerializationTests.cs`
- `tests/SimCore.Tests/Packs/PackMapperTests.cs`
- `tests/SimCore.Tests/Packs/FactionPackLoaderTests.cs`
- `tests/SimCore.Tests/Packs/ReferencePackTests.cs`
- `tests/SimCore.Tests/Packs/FactionDefAssert.cs` — test-only deep-equal helper.
- `tests/SimCore.Tests/Packs/RepoPaths.cs` — test-only repo-root locator.
- `packs/reference/faction.json` — exported reference pack (generated, committed).
- Modified: `tests/SimCore.Tests/SimCore.Tests.csproj` (add ProjectReference), `LlmRts.sln` (add project).

---

## Task 1: Scaffold `SimCore.Packs` + `FixJsonConverter`

**Files:**
- Create: `src/SimCore.Packs/SimCore.Packs.csproj`
- Create: `src/SimCore.Packs/FixJsonConverter.cs`
- Modify: `tests/SimCore.Tests/SimCore.Tests.csproj` (add ProjectReference)
- Modify: `LlmRts.sln` (add project)
- Test: `tests/SimCore.Tests/Packs/FixJsonConverterTests.cs`

- [ ] **Step 1: Create the project file**

Create `src/SimCore.Packs/SimCore.Packs.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\SimCore\SimCore.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Register the project in the solution and test project**

Run (from repo root):

```bash
dotnet sln add src/SimCore.Packs/SimCore.Packs.csproj
```

Then add a ProjectReference to `tests/SimCore.Tests/SimCore.Tests.csproj` inside the existing `<ItemGroup>` that references `SimCore` (so it becomes):

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\SimCore\SimCore.csproj" />
    <ProjectReference Include="..\..\src\SimCore.Packs\SimCore.Packs.csproj" />
  </ItemGroup>
```

- [ ] **Step 3: Write the failing test**

Create `tests/SimCore.Tests/Packs/FixJsonConverterTests.cs`:

```csharp
using System.Text.Json;
using SimCore.Math;
using SimCore.Packs;

namespace SimCore.Tests.Packs;

public class FixJsonConverterTests
{
    private static JsonSerializerOptions Opts()
    {
        var o = new JsonSerializerOptions();
        o.Converters.Add(new FixJsonConverter());
        return o;
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(65536L)]      // 1
    [InlineData(32768L)]      // 0.5
    [InlineData(16384L)]      // 0.25
    [InlineData(13107L)]      // FromFraction(1,5) floor
    [InlineData(8192L)]       // 0.125
    [InlineData(-32768L)]     // -0.5
    [InlineData(1L)]          // smallest unit
    [InlineData(1L << 40)]    // large
    public void Roundtrips_raw_values_exactly(long raw)
    {
        var f = new Fix(raw);
        string json = JsonSerializer.Serialize(f, Opts());
        Fix back = JsonSerializer.Deserialize<Fix>(json, Opts());
        Assert.Equal(f, back);
    }

    [Fact]
    public void Quarter_serializes_as_clean_decimal()
    {
        string json = JsonSerializer.Serialize(Fix.FromFraction(1, 4), Opts());
        Assert.Equal("0.25", json);
    }

    [Fact]
    public void Whole_number_serializes_without_fraction()
    {
        string json = JsonSerializer.Serialize(Fix.FromInt(4), Opts());
        Assert.Equal("4", json);
    }

    [Fact]
    public void Parses_human_authored_decimal_number()
    {
        Fix f = JsonSerializer.Deserialize<Fix>("0.25", Opts());
        Assert.Equal(Fix.FromFraction(1, 4), f);
    }

    [Fact]
    public void Parses_one_fifth_to_same_raw_as_FromFraction()
    {
        Fix f = JsonSerializer.Deserialize<Fix>("0.2", Opts());
        Assert.Equal(Fix.FromFraction(1, 5), f); // both raw 13107
    }

    [Fact]
    public void Parses_decimal_supplied_as_json_string()
    {
        Fix f = JsonSerializer.Deserialize<Fix>("\"0.5\"", Opts());
        Assert.Equal(Fix.FromFraction(1, 2), f);
    }

    [Fact]
    public void Out_of_range_value_throws_JsonException()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Fix>("1e30", Opts()));
    }
}
```

- [ ] **Step 4: Run the test, expect failure**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
Expected: build/compile FAILS — `FixJsonConverter` does not exist yet.

- [ ] **Step 5: Implement the converter**

Create `src/SimCore.Packs/FixJsonConverter.cs`:

```csharp
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimCore.Math;

namespace SimCore.Packs;

/// <summary>Serializes <see cref="Fix"/> as a base-10 decimal. Every Q48.16 value is
/// exactly representable in decimal (1/2^16 terminates in base 10), so this round-trips
/// bit-for-bit using only <see cref="decimal"/> — no <c>double</c> anywhere.
/// NEVER serialize via Fix.ToString (it formats a lossy double).</summary>
public sealed class FixJsonConverter : JsonConverter<Fix>
{
    private const decimal Scale = 65536m; // 2^16 == 1 << Fix.FractionalBits

    public override Fix Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        decimal value;
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (!reader.TryGetDecimal(out value))
                throw new JsonException("Fix value is out of decimal range");
        }
        else if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
                throw new JsonException($"Fix value '{s}' is not a valid decimal");
        }
        else
        {
            throw new JsonException($"Fix expects a number or string, got {reader.TokenType}");
        }

        decimal scaled = value * Scale;
        if (scaled < long.MinValue || scaled > long.MaxValue)
            throw new JsonException($"Fix value {value} is out of Q48.16 range");
        long raw = (long)Math.Round(scaled, MidpointRounding.ToEven);
        return new Fix(raw);
    }

    public override void Write(Utf8JsonWriter writer, Fix value, JsonSerializerOptions options)
    {
        decimal d = (decimal)value.Raw / Scale;
        writer.WriteNumberValue(d);
    }
}
```

- [ ] **Step 6: Run the test, expect pass**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
Expected: PASS, total 224 + 14 = 238 (9 theory cases + 5 facts; exact total may differ — just confirm 0 failures and the count grew).

- [ ] **Step 7: Commit**

```bash
git add src/SimCore.Packs/ tests/SimCore.Tests/SimCore.Tests.csproj tests/SimCore.Tests/Packs/ LlmRts.sln
git commit -m "feat(packs): SimCore.Packs library + Fix JSON converter (decimal-exact)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 2: DTO records + shared `PackJson.Options`

**Files:**
- Create: `src/SimCore.Packs/PackDtos.cs`
- Create: `src/SimCore.Packs/PackJson.cs`
- Test: `tests/SimCore.Tests/Packs/PackDtoSerializationTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/SimCore.Tests/Packs/PackDtoSerializationTests.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using SimCore.Math;
using SimCore.Packs;
using SimCore.Sim;

namespace SimCore.Tests.Packs;

public class PackDtoSerializationTests
{
    [Fact]
    public void Enums_serialize_by_name()
    {
        var up = new UpgradeDto("u", 1, "lab", null, new[] { "trooper" },
            UpgradeStat.Damage, Fix.FromInt(2), 50, 100);
        string json = JsonSerializer.Serialize(up, PackJson.Options);
        Assert.Contains("\"Damage\"", json);
        Assert.DoesNotContain("\"stat\": 0", json);
    }

    [Fact]
    public void Mechanic_kind_serializes_by_name()
    {
        var m = new MechanicDto(MechanicKind.RegeneratingShields, 15, 1, 10);
        string json = JsonSerializer.Serialize(m, PackJson.Options);
        Assert.Contains("RegeneratingShields", json);
    }

    [Fact]
    public void Null_nested_dtos_are_omitted()
    {
        var u = new UnitDto("fabber", 1, "depot", null,
            40, Fix.FromFraction(1, 4), 50, 1, 100, 6, Weapon: null, Harvester: null);
        string json = JsonSerializer.Serialize(u, PackJson.Options);
        Assert.DoesNotContain("weapon", json);
        Assert.DoesNotContain("harvester", json);
    }

    [Fact]
    public void Building_false_and_zero_defaults_are_omitted_on_write()
    {
        var b = new BuildingDto("barracks", 1, null,
            350, 2, 2, 150, 200, SupplyProvided: 0, IsDepot: false, CanTrain: true, SightRange: 8);
        string json = JsonSerializer.Serialize(b, PackJson.Options);
        Assert.DoesNotContain("supplyProvided", json); // 0 omitted
        Assert.DoesNotContain("isDepot", json);        // false omitted
        Assert.Contains("canTrain", json);             // true written
    }

    [Fact]
    public void Building_defaults_are_restored_when_json_omits_them()
    {
        // JSON omits supplyProvided, isDepot, canTrain, sightRange entirely.
        string json = """
        { "id": "barracks", "tier": 1, "maxHp": 350, "width": 2, "height": 2,
          "mineralCost": 150, "buildTimeTicks": 200 }
        """;
        var b = JsonSerializer.Deserialize<BuildingDto>(json, PackJson.Options)!;
        Assert.Equal(0, b.SupplyProvided);
        Assert.False(b.IsDepot);
        Assert.False(b.CanTrain);
        Assert.Equal(8, b.SightRange); // ctor default honored
        Assert.Null(b.Requires);
    }

    [Fact]
    public void Property_names_are_camelCase()
    {
        var u = new UnitDto("fabber", 1, "depot", null,
            40, Fix.FromFraction(1, 4), 50, 1, 100, 6);
        string json = JsonSerializer.Serialize(u, PackJson.Options);
        Assert.Contains("\"maxHp\"", json);
        Assert.Contains("\"producedBy\"", json);
        Assert.DoesNotContain("\"MaxHp\"", json);
    }
}
```

- [ ] **Step 2: Run the test, expect failure**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
Expected: compile FAILS — `UnitDto`, `PackJson`, etc. do not exist.

- [ ] **Step 3: Implement the DTOs**

Create `src/SimCore.Packs/PackDtos.cs`:

```csharp
using System.Collections.Generic;
using SimCore.Math;
using SimCore.Sim;

namespace SimCore.Packs;

/// <summary>Wire shape for a faction pack. Kept separate from the runtime FactionDef/specs
/// so the on-disk format can evolve independently. Collections default to null so
/// System.Text.Json passes null for omitted members; the mapper coalesces to empty.</summary>
public sealed record FactionPackDto(
    string Id, string Name,
    IReadOnlyList<UnitDto>? Units = null,
    IReadOnlyList<BuildingDto>? Buildings = null,
    IReadOnlyList<UpgradeDto>? Upgrades = null,
    MechanicDto? Mechanic = null);

public sealed record UnitDto(
    string Id, int Tier, string ProducedBy, IReadOnlyList<string>? Requires,
    int MaxHp, Fix Speed, int MineralCost, int SupplyCost, int BuildTimeTicks,
    int SightRange = 7, WeaponDto? Weapon = null, HarvesterDto? Harvester = null);

public sealed record WeaponDto(int Damage, Fix Range, int CooldownTicks);

public sealed record HarvesterDto(int CarryCapacity, int GatherTicks);

public sealed record BuildingDto(
    string Id, int Tier, IReadOnlyList<string>? Requires,
    int MaxHp, int Width, int Height, int MineralCost, int BuildTimeTicks,
    int SupplyProvided = 0, bool IsDepot = false, bool CanTrain = false, int SightRange = 8);

public sealed record UpgradeDto(
    string Id, int Tier, string ResearchedAt, IReadOnlyList<string>? Requires,
    IReadOnlyList<string>? TargetUnitDefIds, UpgradeStat Stat, Fix Delta,
    int MineralCost, int ResearchTicks);

public sealed record MechanicDto(MechanicKind Kind, int MaxShield, int RegenPerTick, int RegenDelayTicks);
```

- [ ] **Step 4: Implement the shared options**

Create `src/SimCore.Packs/PackJson.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimCore.Packs;

/// <summary>Shared serializer options for faction packs: camelCase, indented, enums by name,
/// the decimal-exact Fix converter, and omit-defaults on write (so booleans/zeros that match
/// engine defaults stay out of the JSON and round-trip via the DTO ctor defaults).</summary>
public static class PackJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        };
        o.Converters.Add(new FixJsonConverter());
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}
```

- [ ] **Step 5: Run the test, expect pass**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
Expected: PASS, count grows by 6.

- [ ] **Step 6: Commit**

```bash
git add src/SimCore.Packs/PackDtos.cs src/SimCore.Packs/PackJson.cs tests/SimCore.Tests/Packs/PackDtoSerializationTests.cs
git commit -m "feat(packs): DTO records + shared JSON options (camelCase, enums by name, omit defaults)

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 3: `PackMapper` (DTO ↔ `FactionDef`) + test deep-equal helper

**Files:**
- Create: `src/SimCore.Packs/PackMapper.cs`
- Create: `tests/SimCore.Tests/Packs/FactionDefAssert.cs`
- Test: `tests/SimCore.Tests/Packs/PackMapperTests.cs`

- [ ] **Step 1: Create the test deep-equal helper**

`FactionDef` is a class with no structural equality, and `UnitDef`/`BuildingDef`/`UpgradeDef` records use *reference* equality on their `IReadOnlyList<string> Requires` member — so we cannot rely on `==`. This helper compares field-by-field, using sequence equality for the string lists and record value-equality for the `Spec`/`Mechanic` records (those contain only value types, `Fix`, and nested records, so `==` is correct for them).

Create `tests/SimCore.Tests/Packs/FactionDefAssert.cs`:

```csharp
using System.Linq;
using SimCore.Sim;

namespace SimCore.Tests.Packs;

internal static class FactionDefAssert
{
    public static void DeepEqual(FactionDef a, FactionDef b)
    {
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(a.Name, b.Name);
        Assert.Equal(a.Mechanic, b.Mechanic); // record value equality (null == null too)

        Assert.Equal(a.UnitList.Count, b.UnitList.Count);
        foreach (var (x, y) in a.UnitList.Zip(b.UnitList))
        {
            Assert.Equal(x.Id, y.Id);
            Assert.Equal(x.Tier, y.Tier);
            Assert.Equal(x.ProducedBy, y.ProducedBy);
            Assert.Equal(x.Requires, y.Requires);  // xUnit IEnumerable sequence compare
            Assert.Equal(x.Spec, y.Spec);          // UnitSpec record value equality
        }

        Assert.Equal(a.BuildingList.Count, b.BuildingList.Count);
        foreach (var (x, y) in a.BuildingList.Zip(b.BuildingList))
        {
            Assert.Equal(x.Id, y.Id);
            Assert.Equal(x.Tier, y.Tier);
            Assert.Equal(x.Requires, y.Requires);
            Assert.Equal(x.Spec, y.Spec);          // BuildingSpec record value equality
        }

        Assert.Equal(a.UpgradeList.Count, b.UpgradeList.Count);
        foreach (var (x, y) in a.UpgradeList.Zip(b.UpgradeList))
        {
            Assert.Equal(x.Id, y.Id);
            Assert.Equal(x.Tier, y.Tier);
            Assert.Equal(x.ResearchedAt, y.ResearchedAt);
            Assert.Equal(x.Requires, y.Requires);
            Assert.Equal(x.TargetUnitDefIds, y.TargetUnitDefIds);
            Assert.Equal(x.Stat, y.Stat);
            Assert.Equal(x.Delta, y.Delta);        // Fix value equality
            Assert.Equal(x.MineralCost, y.MineralCost);
            Assert.Equal(x.ResearchTicks, y.ResearchTicks);
        }
    }
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/SimCore.Tests/Packs/PackMapperTests.cs`:

```csharp
using System.Collections.Generic;
using SimCore.Math;
using SimCore.Packs;
using SimCore.Sim;

namespace SimCore.Tests.Packs;

public class PackMapperTests
{
    // A synthetic faction exercising every mappable field: weapon, harvester,
    // upgrades (Fix delta, enum, targets), and a mechanic — none of which the
    // bare ReferenceFaction has.
    private static FactionDef Rich()
    {
        var none = System.Array.Empty<string>();
        return new FactionDef(
            id: "rich", name: "Rich",
            units: new[]
            {
                new UnitDef("fab", 1, "depot", none,
                    new UnitSpec(40, Fix.FromFraction(1, 4), 50, 1, 100,
                        Harvester: new HarvesterSpec(5, 10), SightRange: 6)),
                new UnitDef("troop", 1, "rax", new[] { "depot" },
                    new UnitSpec(45, Fix.FromFraction(1, 5), 50, 1, 80,
                        Weapon: new WeaponSpec(6, Fix.FromInt(4), 8), SightRange: 7)),
            },
            buildings: new[]
            {
                new BuildingDef("depot", 1, none, new BuildingSpec(400, 2, 2, 100, 150, SupplyProvided: 8, IsDepot: true, SightRange: 9)),
                new BuildingDef("rax", 1, none, new BuildingSpec(350, 2, 2, 150, 200, CanTrain: true)),
            },
            upgrades: new[]
            {
                new UpgradeDef("dmg1", 1, "rax", none, new[] { "troop" },
                    UpgradeStat.Damage, Fix.FromInt(2), 75, 120),
                new UpgradeDef("spd1", 2, "rax", new[] { "dmg1" }, new[] { "*" },
                    UpgradeStat.Speed, Fix.FromFraction(1, 10), 100, 150),
            },
            mechanic: new MechanicDef(MechanicKind.RegeneratingShields, 15, 1, 10));
    }

    [Fact]
    public void Roundtrips_through_dto_deep_equal()
    {
        var def = Rich();
        var dto = PackMapper.ToDto(def);
        var back = PackMapper.ToFactionDef(dto);
        FactionDefAssert.DeepEqual(def, back);
    }

    [Fact]
    public void Maps_weapon_and_harvester()
    {
        var dto = PackMapper.ToDto(Rich());
        var fab = System.Linq.Enumerable.First(dto.Units!, u => u.Id == "fab");
        var troop = System.Linq.Enumerable.First(dto.Units!, u => u.Id == "troop");
        Assert.NotNull(fab.Harvester);
        Assert.Null(fab.Weapon);
        Assert.NotNull(troop.Weapon);
        Assert.Equal(6, troop.Weapon!.Damage);
        Assert.Equal(Fix.FromInt(4), troop.Weapon.Range);
    }

    [Fact]
    public void Maps_upgrade_enum_and_fix_delta()
    {
        var dto = PackMapper.ToDto(Rich());
        var spd = System.Linq.Enumerable.First(dto.Upgrades!, u => u.Id == "spd1");
        Assert.Equal(UpgradeStat.Speed, spd.Stat);
        Assert.Equal(Fix.FromFraction(1, 10), spd.Delta);
        Assert.Equal(new[] { "*" }, spd.TargetUnitDefIds);
    }

    [Fact]
    public void Maps_mechanic()
    {
        var dto = PackMapper.ToDto(Rich());
        Assert.NotNull(dto.Mechanic);
        Assert.Equal(MechanicKind.RegeneratingShields, dto.Mechanic!.Kind);
        Assert.Equal(15, dto.Mechanic.MaxShield);
    }

    [Fact]
    public void Null_mechanic_maps_to_null()
    {
        var def = new FactionDef("x", "X",
            new[] { new UnitDef("u", 1, "b", System.Array.Empty<string>(), new UnitSpec(10, Fix.One, 1, 1, 1)) },
            new[] { new BuildingDef("b", 1, System.Array.Empty<string>(), new BuildingSpec(10, 1, 1, 1, 1)) });
        var dto = PackMapper.ToDto(def);
        Assert.Null(dto.Mechanic);
        var back = PackMapper.ToFactionDef(dto);
        Assert.Null(back.Mechanic);
    }
}
```

- [ ] **Step 3: Run the test, expect failure**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
Expected: compile FAILS — `PackMapper` does not exist.

- [ ] **Step 4: Implement the mapper**

Create `src/SimCore.Packs/PackMapper.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using SimCore.Sim;

namespace SimCore.Packs;

/// <summary>Converts between the pack DTO wire shape and the engine FactionDef. The
/// FactionDef direction feeds the ctor ORDERED LISTS (the ctor builds its lookup dicts);
/// it never touches those dicts directly.</summary>
public static class PackMapper
{
    private static readonly IReadOnlyList<string> NoStrings = System.Array.Empty<string>();

    public static FactionPackDto ToDto(FactionDef f) => new(
        f.Id, f.Name,
        f.UnitList.Select(ToDto).ToList(),
        f.BuildingList.Select(ToDto).ToList(),
        f.UpgradeList.Select(ToDto).ToList(),
        f.Mechanic is null ? null : ToDto(f.Mechanic));

    public static FactionDef ToFactionDef(FactionPackDto d) => new(
        d.Id, d.Name,
        (d.Units ?? new List<UnitDto>()).Select(ToUnitDef),
        (d.Buildings ?? new List<BuildingDto>()).Select(ToBuildingDef),
        (d.Upgrades ?? new List<UpgradeDto>()).Select(ToUpgradeDef),
        d.Mechanic is null ? null : ToMechanicDef(d.Mechanic));

    // --- units ---
    private static UnitDto ToDto(UnitDef u) => new(
        u.Id, u.Tier, u.ProducedBy, u.Requires.ToList(),
        u.Spec.MaxHp, u.Spec.Speed, u.Spec.MineralCost, u.Spec.SupplyCost, u.Spec.BuildTimeTicks,
        u.Spec.SightRange,
        u.Spec.Weapon is null ? null
            : new WeaponDto(u.Spec.Weapon.Damage, u.Spec.Weapon.Range, u.Spec.Weapon.CooldownTicks),
        u.Spec.Harvester is null ? null
            : new HarvesterDto(u.Spec.Harvester.CarryCapacity, u.Spec.Harvester.GatherTicks));

    private static UnitDef ToUnitDef(UnitDto d) => new(
        d.Id, d.Tier, d.ProducedBy, (d.Requires ?? NoStrings).ToList(),
        new UnitSpec(d.MaxHp, d.Speed, d.MineralCost, d.SupplyCost, d.BuildTimeTicks,
            d.Weapon is null ? null : new WeaponSpec(d.Weapon.Damage, d.Weapon.Range, d.Weapon.CooldownTicks),
            d.Harvester is null ? null : new HarvesterSpec(d.Harvester.CarryCapacity, d.Harvester.GatherTicks),
            d.SightRange));

    // --- buildings ---
    private static BuildingDto ToDto(BuildingDef b) => new(
        b.Id, b.Tier, b.Requires.ToList(),
        b.Spec.MaxHp, b.Spec.Width, b.Spec.Height, b.Spec.MineralCost, b.Spec.BuildTimeTicks,
        b.Spec.SupplyProvided, b.Spec.IsDepot, b.Spec.CanTrain, b.Spec.SightRange);

    private static BuildingDef ToBuildingDef(BuildingDto d) => new(
        d.Id, d.Tier, (d.Requires ?? NoStrings).ToList(),
        new BuildingSpec(d.MaxHp, d.Width, d.Height, d.MineralCost, d.BuildTimeTicks,
            d.SupplyProvided, d.IsDepot, d.CanTrain, d.SightRange));

    // --- upgrades ---
    private static UpgradeDto ToDto(UpgradeDef g) => new(
        g.Id, g.Tier, g.ResearchedAt, g.Requires.ToList(),
        g.TargetUnitDefIds.ToList(), g.Stat, g.Delta, g.MineralCost, g.ResearchTicks);

    private static UpgradeDef ToUpgradeDef(UpgradeDto d) => new(
        d.Id, d.Tier, d.ResearchedAt, (d.Requires ?? NoStrings).ToList(),
        (d.TargetUnitDefIds ?? NoStrings).ToList(), d.Stat, d.Delta, d.MineralCost, d.ResearchTicks);

    // --- mechanic ---
    private static MechanicDto ToDto(MechanicDef m) => new(m.Kind, m.MaxShield, m.RegenPerTick, m.RegenDelayTicks);
    private static MechanicDef ToMechanicDef(MechanicDto d) => new(d.Kind, d.MaxShield, d.RegenPerTick, d.RegenDelayTicks);
}
```

- [ ] **Step 5: Run the test, expect pass**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
Expected: PASS, count grows by 5.

- [ ] **Step 6: Commit**

```bash
git add src/SimCore.Packs/PackMapper.cs tests/SimCore.Tests/Packs/FactionDefAssert.cs tests/SimCore.Tests/Packs/PackMapperTests.cs
git commit -m "feat(packs): PackMapper DTO <-> FactionDef + test deep-equal helper

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 4: `FactionPackLoader` (LoadFromJson / ToJson) + error handling

**Files:**
- Create: `src/SimCore.Packs/FactionPackLoader.cs`
- Test: `tests/SimCore.Tests/Packs/FactionPackLoaderTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/SimCore.Tests/Packs/FactionPackLoaderTests.cs`:

```csharp
using SimCore.Packs;
using SimCore.Sim;

namespace SimCore.Tests.Packs;

public class FactionPackLoaderTests
{
    [Fact]
    public void Roundtrips_reference_faction_through_json()
    {
        string json = FactionPackLoader.ToJson(ReferenceFaction.Def);
        var result = FactionPackLoader.LoadFromJson(json);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Faction);
        FactionDefAssert.DeepEqual(ReferenceFaction.Def, result.Faction!);
    }

    [Fact]
    public void Malformed_json_returns_error_not_throw()
    {
        var result = FactionPackLoader.LoadFromJson("{ not valid json ");
        Assert.Null(result.Faction);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Unknown_enum_name_returns_error()
    {
        // valid JSON but Stat is an unknown enum name
        string json = """
        { "id": "x", "name": "X",
          "units": [ { "id": "u", "tier": 1, "producedBy": "b",
                       "maxHp": 10, "speed": 1, "mineralCost": 1, "supplyCost": 1, "buildTimeTicks": 1 } ],
          "buildings": [ { "id": "b", "tier": 1, "maxHp": 10, "width": 1, "height": 1, "mineralCost": 1, "buildTimeTicks": 1 } ],
          "upgrades": [ { "id": "g", "tier": 1, "researchedAt": "b",
                          "targetUnitDefIds": ["u"], "stat": "Telekinesis", "delta": 1, "mineralCost": 1, "researchTicks": 1 } ] }
        """;
        var result = FactionPackLoader.LoadFromJson(json);
        Assert.Null(result.Faction);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Referentially_broken_pack_returns_def_plus_validate_errors()
    {
        // Parses fine, but the unit's producer building does not exist -> Validate() catches it.
        string json = """
        { "id": "x", "name": "X",
          "units": [ { "id": "u", "tier": 1, "producedBy": "ghost",
                       "maxHp": 10, "speed": 1, "mineralCost": 1, "supplyCost": 1, "buildTimeTicks": 1 } ],
          "buildings": [] }
        """;
        var result = FactionPackLoader.LoadFromJson(json);
        Assert.NotNull(result.Faction);           // structurally parseable -> def returned
        Assert.NotEmpty(result.Errors);           // Validate() flagged the dangling producer
        Assert.Contains(result.Errors, e => e.Contains("ghost"));
    }

    [Fact]
    public void Out_of_range_fix_returns_error()
    {
        string json = """
        { "id": "x", "name": "X",
          "units": [ { "id": "u", "tier": 1, "producedBy": "b",
                       "maxHp": 10, "speed": 1e30, "mineralCost": 1, "supplyCost": 1, "buildTimeTicks": 1 } ],
          "buildings": [ { "id": "b", "tier": 1, "maxHp": 10, "width": 1, "height": 1, "mineralCost": 1, "buildTimeTicks": 1 } ] }
        """;
        var result = FactionPackLoader.LoadFromJson(json);
        Assert.Null(result.Faction);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void ToJson_is_indented_and_human_readable()
    {
        string json = FactionPackLoader.ToJson(ReferenceFaction.Def);
        Assert.Contains("\n", json);              // indented (multi-line)
        Assert.Contains("\"reference\"", json);
    }
}
```

- [ ] **Step 2: Run the test, expect failure**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
Expected: compile FAILS — `FactionPackLoader` does not exist.

- [ ] **Step 3: Implement the loader**

Create `src/SimCore.Packs/FactionPackLoader.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;
using SimCore.Sim;

namespace SimCore.Packs;

/// <summary>Result of loading a pack: the faction (null only on a hard parse/map failure)
/// plus any errors. A structurally-parseable-but-invalid pack returns the faction AND its
/// Validate() errors, so a fix-it loop (3d-2) can surface them.</summary>
public sealed record PackLoadResult(FactionDef? Faction, IReadOnlyList<string> Errors);

/// <summary>Loads/saves faction packs as JSON. The only entry points the engine/UI should use.</summary>
public static class FactionPackLoader
{
    public static PackLoadResult LoadFromJson(string json)
    {
        FactionPackDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<FactionPackDto>(json, PackJson.Options);
        }
        catch (JsonException ex)
        {
            return new PackLoadResult(null, new[] { $"JSON parse error: {ex.Message}" });
        }

        if (dto is null)
            return new PackLoadResult(null, new[] { "pack JSON deserialized to null" });

        FactionDef def;
        try
        {
            def = PackMapper.ToFactionDef(dto);
        }
        catch (Exception ex)
        {
            return new PackLoadResult(null, new[] { $"pack mapping error: {ex.Message}" });
        }

        var errors = def.Validate();
        return new PackLoadResult(def, errors);
    }

    public static string ToJson(FactionDef faction) =>
        JsonSerializer.Serialize(PackMapper.ToDto(faction), PackJson.Options);
}
```

- [ ] **Step 4: Run the test, expect pass**

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
Expected: PASS, count grows by 6.

- [ ] **Step 5: Commit**

```bash
git add src/SimCore.Packs/FactionPackLoader.cs tests/SimCore.Tests/Packs/FactionPackLoaderTests.cs
git commit -m "feat(packs): FactionPackLoader (LoadFromJson/ToJson) with error-collecting result

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 5: Export reference faction as the first pack + regen-guard test

**Files:**
- Create: `tests/SimCore.Tests/Packs/RepoPaths.cs`
- Create: `tests/SimCore.Tests/Packs/ReferencePackTests.cs`
- Create (generated): `packs/reference/faction.json`

- [ ] **Step 1: Create the repo-root locator helper**

Tests run from `tests/SimCore.Tests/bin/<cfg>/net8.0`; this walks up to the directory containing the `.sln` so it can find `packs/`.

Create `tests/SimCore.Tests/Packs/RepoPaths.cs`:

```csharp
using System.IO;

namespace SimCore.Tests.Packs;

internal static class RepoPaths
{
    public static string Root()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("*.sln").Length == 0)
            dir = dir.Parent;
        if (dir is null)
            throw new DirectoryNotFoundException("repo root (directory containing a .sln) not found");
        return dir.FullName;
    }

    public static string Pack(string relative) => Path.Combine(Root(), "packs", relative);

    /// <summary>Normalize line endings so git's CRLF/LF conversion never breaks text equality.</summary>
    public static string Normalize(string s) => s.Replace("\r\n", "\n");
}
```

- [ ] **Step 2: Write the guard test (with bootstrap-on-env-var)**

The test asserts the committed `packs/reference/faction.json` exactly matches `ToJson(ReferenceFaction.Def)` and loads back deep-equal. When run with the env var `UPDATE_PACKS=1`, it (re)writes the file instead — that is how the file is first generated and later regenerated after intentional changes. CI never sets the var, so it acts purely as a drift guard.

Create `tests/SimCore.Tests/Packs/ReferencePackTests.cs`:

```csharp
using System;
using System.IO;
using SimCore.Packs;
using SimCore.Sim;

namespace SimCore.Tests.Packs;

public class ReferencePackTests
{
    private static string PackPath => RepoPaths.Pack("reference/faction.json");

    [Fact]
    public void Reference_pack_file_is_in_sync_with_serialization()
    {
        string expected = FactionPackLoader.ToJson(ReferenceFaction.Def);

        if (Environment.GetEnvironmentVariable("UPDATE_PACKS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PackPath)!);
            File.WriteAllText(PackPath, expected);
        }

        Assert.True(File.Exists(PackPath),
            $"missing {PackPath}; regenerate by running this test with UPDATE_PACKS=1");
        Assert.Equal(RepoPaths.Normalize(expected), RepoPaths.Normalize(File.ReadAllText(PackPath)));
    }

    [Fact]
    public void Reference_pack_file_loads_deep_equal_to_in_code_def()
    {
        Assert.True(File.Exists(PackPath),
            $"missing {PackPath}; regenerate by running ReferencePackTests with UPDATE_PACKS=1");
        var result = FactionPackLoader.LoadFromJson(File.ReadAllText(PackPath));
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Faction);
        FactionDefAssert.DeepEqual(ReferenceFaction.Def, result.Faction!);
    }
}
```

- [ ] **Step 3: Generate the pack file**

Run the guard test once with the env var set so it writes `packs/reference/faction.json`:

bash:
```bash
UPDATE_PACKS=1 dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q --filter "FullyQualifiedName~ReferencePackTests"
```
PowerShell:
```powershell
$env:UPDATE_PACKS=1; dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q --filter "FullyQualifiedName~ReferencePackTests"; Remove-Item Env:\UPDATE_PACKS
```
Expected: PASS, and `packs/reference/faction.json` now exists.

- [ ] **Step 4: Verify the file and the full suite (no env var)**

Confirm the file looks right (faction id `reference`, fabber/trooper/outrider/tank units, depot/barracks buildings, `"upgrades": []`, no `mechanic` key):

```bash
git status --short packs/
```

Then run the whole suite normally (no `UPDATE_PACKS`):

Run: `dotnet test tests/SimCore.Tests/SimCore.Tests.csproj --nologo -v q`
Expected: PASS — all tests green including both ReferencePackTests; count grows by 2.

- [ ] **Step 5: Commit**

```bash
git add packs/reference/faction.json tests/SimCore.Tests/Packs/RepoPaths.cs tests/SimCore.Tests/Packs/ReferencePackTests.cs
git commit -m "feat(packs): export reference faction as packs/reference/faction.json + drift-guard test

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Task 6: Full-suite + determinism gate; record plan-3d-2 inputs

**Files:**
- Modify: `docs/superpowers/plans/2026-06-13-pack-serialization.md` (this file — append the carry-forward section)

- [ ] **Step 1: Run the full solution test suite in Release**

This plan adds a new project and tests but does NOT touch `SimCore`, so the determinism golden hash must be unchanged.

Run: `dotnet test --configuration Release --nologo -v q`
Expected: PASS — every project. SimCore.Tests count = 224 + new tests (≈ 33 added across tasks 1–5). The determinism tests (`Trajectory_Hash_Matches_Golden_Constant`, the two replay tests) pass with the golden constant unchanged at `5141900307592480923UL`.

- [ ] **Step 2: Run the full suite in Debug**

Run: `dotnet test --configuration Debug --nologo -v q`
Expected: PASS — confirms Debug == Release determinism.

- [ ] **Step 3: Confirm SimCore was not modified**

```bash
git diff --stat master -- src/SimCore/
```
Expected: empty (no changes to the determinism core).

- [ ] **Step 4: Append the plan-3d-2 carry-forward section**

Append the following to the end of this plan document:

```markdown
---

## Plan-3d-2 Inputs (carry-forward)

3d-1 delivered serialization/loading. 3d-2 (validation & authoring) builds on it:

- **Validator location:** add the richer checks as a separate pass over `FactionDef`
  (e.g. a `PackValidator` in `SimCore.Packs`), NOT inside `FactionDef.Validate()`
  (that stays the structural/referential seed). `LoadFromJson` already returns the
  def + Validate() errors; 3d-2 layers additional warnings/errors on top.
- **Point-budget balance formula:** additive weighted power sum over the catalog
  (unit stat weights × counts, building/upgrade/mechanic contributions) with a
  cost-tolerance band that WARNS rather than hard-rejects (a faction outside the
  band is unusual, not illegal — gaps are identity).
- **Other 3d-2 checks:** tier monotonicity (a tier-N thing's prereqs resolve to
  tier ≤ N), producer-reachability (every unit's ProducedBy/Requires chain is
  buildable from t0), structural minimums (≥1 depot-or-producer, ≥1 trainable unit).
- **Machine-readable fix-it output:** each finding carries a code + target id +
  human message so the Faction Forge prompt can auto-repair.
- **Faction Forge prompt doc:** authoring guide + JSON schema-by-example using
  `packs/reference/faction.json` as the worked example.
- **Wire-format facts to honor:** Fix = decimal JSON number (the `FixJsonConverter`,
  decimal-exact; humans may write `0.2`, it maps to raw 13107). Enums by name.
  camelCase keys. Omitted booleans/zeros take engine defaults. Collections optional.
- **Determinism:** packs are setup-time; the converter is decimal-based so two
  players loading the same pack get a bit-identical FactionDef. A pack content hash
  for multiplayer verification is deferred to plan 5.
```

- [ ] **Step 5: Commit**

```bash
git add docs/superpowers/plans/2026-06-13-pack-serialization.md
git commit -m "docs: record plan-3d-2 inputs from 3d-1 pack serialization

Co-Authored-By: RuFlo <ruv@ruv.net>"
```

---

## Self-Review (author checklist — completed)

- **Spec coverage:** Fix converter (Task 1), DTO layer + options (Task 2), PackMapper feeding ctor lists (Task 3), FactionPackLoader + error handling + Validate-on-load (Task 4), reference pack export + round-trip (Task 5), new `SimCore.Packs` library (Task 1), no determinism change (Task 6). All spec "In scope" items map to a task.
- **Placeholders:** none — every code/test step has complete code; every command has an expected result.
- **Type consistency:** DTO field order matches `UnitSpec`/`BuildingSpec`/`UpgradeDef`/`MechanicDef` ctors verified against source. `PackMapper` method names and `FactionDefAssert.DeepEqual` / `RepoPaths.Pack` / `PackJson.Options` / `FactionPackLoader.{LoadFromJson,ToJson}` used consistently across tasks.
