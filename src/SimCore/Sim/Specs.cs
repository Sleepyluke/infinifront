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
    WeaponSpec? Weapon = null, HarvesterSpec? Harvester = null, int SightRange = 7);

public sealed record BuildingSpec(
    int MaxHp, int Width, int Height, int MineralCost, int BuildTimeTicks,
    int SupplyProvided = 0, bool IsDepot = false, bool CanTrain = false, int SightRange = 8,
    WeaponSpec? Weapon = null);
