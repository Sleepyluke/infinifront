using System.Collections.Generic;
using System.Text.Json.Serialization;
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
    // Always write SightRange even when 0: its ctor default is non-zero (7), so under the
    // global WhenWritingDefault policy a 0 would be omitted and wrongly restored to 7 on read.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int SightRange = 7,
    WeaponDto? Weapon = null, HarvesterDto? Harvester = null);

public sealed record WeaponDto(int Damage, Fix Range, int CooldownTicks);

public sealed record HarvesterDto(int CarryCapacity, int GatherTicks);

public sealed record BuildingDto(
    string Id, int Tier, IReadOnlyList<string>? Requires,
    int MaxHp, int Width, int Height, int MineralCost, int BuildTimeTicks,
    int SupplyProvided = 0, bool IsDepot = false, bool CanTrain = false,
    // Always write SightRange even when 0 (ctor default is 8); see UnitDto.SightRange.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int SightRange = 8,
    WeaponDto? Weapon = null);

public sealed record UpgradeDto(
    string Id, int Tier, string ResearchedAt, IReadOnlyList<string>? Requires,
    IReadOnlyList<string>? TargetUnitDefIds,
    // Always write Stat even when it is the zero member (Damage): the upgrade's target stat
    // is meaningful, so the global WhenWritingDefault must not silently drop it.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] UpgradeStat Stat,
    Fix Delta, int MineralCost, int ResearchTicks);

public sealed record MechanicDto(
    // Always write Kind, including MechanicKind.None (zero), so a mechanic block never
    // serializes to an empty/kind-less object under the global WhenWritingDefault policy.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] MechanicKind Kind,
    int MaxShield, int RegenPerTick, int RegenDelayTicks);
