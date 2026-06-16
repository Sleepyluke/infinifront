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
        b.Spec.SupplyProvided, b.Spec.IsDepot, b.Spec.CanTrain, b.Spec.SightRange,
        b.Spec.Weapon is null ? null
            : new WeaponDto(b.Spec.Weapon.Damage, b.Spec.Weapon.Range, b.Spec.Weapon.CooldownTicks));

    private static BuildingDef ToBuildingDef(BuildingDto d) => new(
        d.Id, d.Tier, (d.Requires ?? NoStrings).ToList(),
        new BuildingSpec(d.MaxHp, d.Width, d.Height, d.MineralCost, d.BuildTimeTicks,
            d.SupplyProvided, d.IsDepot, d.CanTrain, d.SightRange,
            d.Weapon is null ? null : new WeaponSpec(d.Weapon.Damage, d.Weapon.Range, d.Weapon.CooldownTicks)));

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
