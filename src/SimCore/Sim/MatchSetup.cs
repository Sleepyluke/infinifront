using SimCore.Math;

namespace SimCore.Sim;

/// <summary>One player slot for <see cref="MatchSetup.BuildMatch"/>: a faction, a controller
/// (Human/Cpu), a difficulty (only meaningful for Cpu), and a team id.</summary>
public sealed record MatchSlot(FactionDef Faction, PlayerController Controller, AiDifficulty Difficulty, int Team);

/// <summary>Builds a standard 1v1 match world (player 0 = human, player 1 = CPU) from two
/// factions + a difficulty. Starting bases are role-resolved from each player's OWN faction, so
/// it works for any pack. Deterministic; SimCore-only (no Godot).</summary>
public static class MatchSetup
{
    public const int MapSize = 40;

    private readonly record struct Corner(int DepotX, int DepotY, int RaxX, int RaxY, int NodeX, int NodeY, int WorkerX, int WorkerY);

    // Up to 4 start corners on the 40x40 map. 0/1 match the legacy 1v1 placements exactly.
    private static readonly Corner[] Corners =
    {
        new(4, 4, 8, 4, 2, 8, 6, 8),        // top-left
        new(34, 34, 30, 34, 37, 28, 33, 28),// bottom-right
        new(4, 34, 8, 34, 2, 28, 6, 28),    // bottom-left
        new(34, 4, 30, 4, 37, 8, 33, 8),    // top-right
    };

    /// <summary>Build a 2–4 player match from slots. One role-resolved base per corner; CPU + team
    /// set per slot. Deterministic from the seed; identical on every peer (the lockstep contract).</summary>
    public static SimWorld BuildMatch(System.Collections.Generic.IReadOnlyList<MatchSlot> slots, ulong seed)
    {
        if (slots.Count < 2 || slots.Count > Corners.Length)
            throw new System.ArgumentException($"BuildMatch supports 2..{Corners.Length} slots", nameof(slots));
        var factions = new FactionDef?[slots.Count];
        for (int i = 0; i < slots.Count; i++) factions[i] = slots[i].Faction;
        var w = new SimWorld(BuildMap(), seed, factions);
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (s.Controller == PlayerController.Cpu) w.SetCpu(i, s.Difficulty);
            w.SetTeam(i, s.Team);
            var c = Corners[i];
            PlaceBase(w, i, s.Faction, c.DepotX, c.DepotY, c.RaxX, c.RaxY, c.NodeX, c.NodeY, c.WorkerX, c.WorkerY);
        }
        return w;
    }

    public static SimWorld BuildStandard1v1(FactionDef humanFaction, FactionDef cpuFaction,
                                            AiDifficulty difficulty, ulong seed) =>
        BuildMatch(new[]
        {
            new MatchSlot(humanFaction, PlayerController.Human, AiDifficulty.Easy, Team: 0),
            new MatchSlot(cpuFaction, PlayerController.Cpu, difficulty, Team: 1),
        }, seed);

    /// <summary>Networked 1v1: BOTH players are human (no CPU). Same map + base placement as the
    /// standard 1v1, minus SetCpu. Deterministic from the seed; identical on every peer.</summary>
    public static SimWorld BuildVersus1v1(FactionDef p0Faction, FactionDef p1Faction, ulong seed) =>
        BuildMatch(new[]
        {
            new MatchSlot(p0Faction, PlayerController.Human, AiDifficulty.Easy, Team: 0),
            new MatchSlot(p1Faction, PlayerController.Human, AiDifficulty.Easy, Team: 1),
        }, seed);

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
