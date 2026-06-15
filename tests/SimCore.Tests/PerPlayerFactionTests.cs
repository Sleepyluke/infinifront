using System.Collections.Generic;
using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class PerPlayerFactionTests
{
    private static readonly string[] None = System.Array.Empty<string>();
    private static readonly List<Command> Empty = new();

    // Minimal faction: one depot-style building "base_<id>" + one unit "u_<id>", optional mechanic.
    private static FactionDef OneUnitFaction(string id, MechanicDef? mechanic = null)
    {
        var depot = new BuildingSpec(100, 2, 2, 50, 5, IsDepot: true);
        var unitSpec = new UnitSpec(30, Fix.One, 50, 1, 5);
        return new FactionDef(id, id,
            units: new[] { new UnitDef("u_" + id, 1, "base_" + id, None, unitSpec) },
            buildings: new[] { new BuildingDef("base_" + id, 1, None, depot) },
            upgrades: System.Array.Empty<UpgradeDef>(),
            mechanic: mechanic);
    }

    [Fact]
    public void FactionFor_Returns_Each_Players_Faction()
    {
        var a = OneUnitFaction("A");
        var b = OneUnitFaction("B");
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, new FactionDef?[] { a, b });
        Assert.Same(a, w.FactionFor(0));
        Assert.Same(b, w.FactionFor(1));
        Assert.Same(a, w.Faction); // alias = player 0
    }

    [Fact]
    public void Shared_Faction_Ctor_Applies_To_All_Players()
    {
        var a = OneUnitFaction("A");
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, faction: a); // playerCount default 2
        Assert.Same(a, w.FactionFor(0));
        Assert.Same(a, w.FactionFor(1));
    }

    [Fact]
    public void Build_Resolves_Against_Acting_Players_Faction()
    {
        var a = OneUnitFaction("A");
        var b = OneUnitFaction("B");
        var w = new SimWorld(new MapGrid(30, 30), seed: 1, new FactionDef?[] { a, b });
        w.Players[0].Minerals = 1000;
        w.Players[1].Minerals = 1000;
        int w0 = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.One, 30);
        int w1 = w.SpawnUnit(1, w.Map.CellCenter(20, 20), Fix.One, 30);

        // Player 0 builds A's building (in range of worker at (5,5)).
        w.Step(new List<Command> { new BuildCommand(0, w0, "base_A", 6, 5) });
        Assert.Contains(w.Buildings, x => x.OwnerId == 0 && x.DefId == "base_A");

        // Player 1 builds B's building (FactionFor(1) == B).
        w.Step(new List<Command> { new BuildCommand(1, w1, "base_B", 21, 20) });
        Assert.Contains(w.Buildings, x => x.OwnerId == 1 && x.DefId == "base_B");

        // Player 0 tries B's def id (base_B) IN RANGE of its worker — rejected: A's catalog lacks it.
        int before = w.Buildings.Count;
        w.Step(new List<Command> { new BuildCommand(0, w0, "base_B", 6, 7) });
        Assert.Equal(before, w.Buildings.Count);
    }

    [Fact]
    public void Shields_Are_Per_Owner_Faction()
    {
        var shielded = new MechanicDef(MechanicKind.RegeneratingShields, MaxShield: 10, RegenPerTick: 5, RegenDelayTicks: 2);
        var a = OneUnitFaction("A", shielded); // player 0: regenerating shields
        var b = OneUnitFaction("B", null);     // player 1: no mechanic
        var w = new SimWorld(new MapGrid(20, 20), seed: 1, new FactionDef?[] { a, b });

        int u0 = w.SpawnUnit(0, w.Map.CellCenter(3, 3), Fix.One, 100);
        int u1 = w.SpawnUnit(1, w.Map.CellCenter(15, 15), Fix.One, 100);

        // Initial shields come from each owner's faction.
        Assert.Equal(10, w.GetUnit(u0)!.ShieldHp);
        Assert.Equal(0, w.GetUnit(u1)!.ShieldHp);

        // Drain both shields, then step: only the shields-faction unit regenerates.
        w.GetUnit(u0)!.ShieldHp = 0; w.GetUnit(u0)!.TicksSinceDamaged = 0;
        w.GetUnit(u1)!.ShieldHp = 0; w.GetUnit(u1)!.TicksSinceDamaged = 0;
        for (int i = 0; i < 5; i++) w.Step(Empty);

        Assert.Equal(10, w.GetUnit(u0)!.ShieldHp); // regenerated to MaxShield
        Assert.Equal(0, w.GetUnit(u1)!.ShieldHp);  // owner has no mechanic → never regenerates
    }
}
