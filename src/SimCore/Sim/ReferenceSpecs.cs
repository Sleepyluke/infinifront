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
        Harvester: new HarvesterSpec(CarryCapacity: 5, GatherTicks: 10), SightRange: 6);

    public static readonly UnitSpec Trooper = new(
        MaxHp: 45, Speed: Fix.FromFraction(1, 5), MineralCost: 50, SupplyCost: 1,
        BuildTimeTicks: 80,
        Weapon: new WeaponSpec(Damage: 6, Range: Fix.FromInt(4), CooldownTicks: 8), SightRange: 7);

    public static readonly UnitSpec Outrider = new(
        MaxHp: 60, Speed: Fix.FromFraction(1, 2), MineralCost: 75, SupplyCost: 2,
        BuildTimeTicks: 120,
        Weapon: new WeaponSpec(Damage: 4, Range: Fix.FromInt(3), CooldownTicks: 5), SightRange: 9);

    public static readonly UnitSpec Tank = new(
        MaxHp: 150, Speed: Fix.FromFraction(1, 8), MineralCost: 150, SupplyCost: 3,
        BuildTimeTicks: 200,
        Weapon: new WeaponSpec(Damage: 20, Range: Fix.FromInt(6), CooldownTicks: 20), SightRange: 7);

    // --- buildings (2x2 footprints) --------------------------------------
    public static readonly BuildingSpec Depot = new(
        MaxHp: 400, Width: 2, Height: 2, MineralCost: 100, BuildTimeTicks: 150,
        SupplyProvided: 8, IsDepot: true, SightRange: 9);

    public static readonly BuildingSpec Barracks = new(
        MaxHp: 350, Width: 2, Height: 2, MineralCost: 150, BuildTimeTicks: 200,
        CanTrain: true, SightRange: 8);
}
