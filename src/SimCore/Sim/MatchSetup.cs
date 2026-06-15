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
        var w = new SimWorld(BuildMap(), seed, new FactionDef?[] { humanFaction, cpuFaction });
        w.SetCpu(1, difficulty);
        PlaceBase(w, 0, humanFaction, depotX: 4, depotY: 4, raxX: 8, raxY: 4, nodeX: 2, nodeY: 8, workerX: 6, workerY: 8);
        PlaceBase(w, 1, cpuFaction, depotX: 34, depotY: 34, raxX: 30, raxY: 34, nodeX: 37, nodeY: 28, workerX: 33, workerY: 28);
        return w;
    }

    /// <summary>Networked 1v1: BOTH players are human (no CPU). Same map + base placement as the
    /// standard 1v1, minus SetCpu. Deterministic from the seed; identical on every peer.</summary>
    public static SimWorld BuildVersus1v1(FactionDef p0Faction, FactionDef p1Faction, ulong seed)
    {
        var w = new SimWorld(BuildMap(), seed, new FactionDef?[] { p0Faction, p1Faction });
        PlaceBase(w, 0, p0Faction, depotX: 4, depotY: 4, raxX: 8, raxY: 4, nodeX: 2, nodeY: 8, workerX: 6, workerY: 8);
        PlaceBase(w, 1, p1Faction, depotX: 34, depotY: 34, raxX: 30, raxY: 34, nodeX: 37, nodeY: 28, workerX: 33, workerY: 28);
        return w;
    }

    private static MapGrid BuildMap()
    {
        var map = new MapGrid(MapSize, MapSize);
        // Rock ridge at x=20 with gaps at y=8..11 and y=28..31 (matches the legacy sandbox).
        for (int y = 0; y < MapSize; y++)
            if (y < 8 || (y > 11 && y < 28) || y > 31) map.SetPassable(20, y, false);
        return map;
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
