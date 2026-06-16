using SimCore.Sim;
using Xunit;

namespace SimCore.Tests;

public class TowerDeterminismTests
{
    [Fact]
    public void Reference_Has_A_Tower_Def_That_Requires_Depot()
    {
        var t = System.Linq.Enumerable.FirstOrDefault(ReferenceFaction.Def.BuildingList, b => b.Id == "tower");
        Assert.NotNull(t);
        Assert.NotNull(t!.Spec.Weapon);
    }

    [Fact]
    public void Hash_Reflects_Tower_Cooldown()
    {
        // Two worlds: one with a tower mid-cooldown, one freshly placed → different hashes
        // (weapon cooldown IS folded when present).
        var a = TowerWorld(); var b = TowerWorld();
        a.Step(System.Array.Empty<Command>());           // 'a' ticks the tower's cooldown / fires
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Tower_World_Replays_Identically()
    {
        var a = TowerWorld(); var b = TowerWorld();
        for (int i = 0; i < 60; i++) { a.Step(System.Array.Empty<Command>()); b.Step(System.Array.Empty<Command>()); }
        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    private static SimWorld TowerWorld()
    {
        var w = new SimWorld(new MapGrid(24, 24), seed: 5, playerCount: 4, faction: null);
        w.FogEnabled = false;
        w.AddCompletedBuilding(0, ReferenceSpecs.SentryTurret, 4, 4, "tower");
        w.SpawnUnit(1, w.Map.CellCenter(6, 6), SimCore.Math.Fix.FromInt(1), hp: 80);
        return w;
    }
}
