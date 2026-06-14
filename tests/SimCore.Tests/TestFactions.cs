using SimCore.Math;
using SimCore.Sim;

/// <summary>Shared FactionDef for tests that exercise id-based build/train commands.
/// Stat values match the per-test fixtures they replace (BuildingTests.Depot etc.).</summary>
public static class TestFactions
{
    private static readonly string[] None = System.Array.Empty<string>();

    public static readonly BuildingSpec DepotSpec =
        new(MaxHp: 100, Width: 2, Height: 2, MineralCost: 100, BuildTimeTicks: 10,
            SupplyProvided: 8, IsDepot: true);
    public static readonly BuildingSpec BarracksSpec =
        new(MaxHp: 150, Width: 2, Height: 2, MineralCost: 150, BuildTimeTicks: 5, CanTrain: true);
    public static readonly BuildingSpec HutSpec =
        new(MaxHp: 30, Width: 2, Height: 2, MineralCost: 50, BuildTimeTicks: 1);

    public static readonly UnitSpec MarineSpec =
        new(MaxHp: 40, Speed: Fix.FromFraction(1, 2), MineralCost: 50, SupplyCost: 1,
            BuildTimeTicks: 8, Weapon: new WeaponSpec(6, Fix.FromInt(2), 5));
    public static readonly UnitSpec TankSpec =
        new(MaxHp: 150, Speed: Fix.FromFraction(1, 8), MineralCost: 150, SupplyCost: 3,
            BuildTimeTicks: 12, Weapon: new WeaponSpec(20, Fix.FromInt(6), 20));

    /// <summary>Standard catalog: depot/barracks/hut (no reqs), marine (produced_by barracks),
    /// tank (produced_by barracks, requires depot — tier 2).</summary>
    public static readonly FactionDef Standard = new(
        id: "test", name: "Test",
        units: new[]
        {
            new UnitDef("marine", 1, "barracks", None, MarineSpec),
            new UnitDef("tank",   2, "barracks", new[] { "depot" }, TankSpec),
        },
        buildings: new[]
        {
            new BuildingDef("depot",    1, None, DepotSpec),
            new BuildingDef("barracks", 1, None, BarracksSpec),
            new BuildingDef("hut",      1, None, HutSpec),
        });
}
