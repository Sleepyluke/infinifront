using SimCore.Math;

namespace SimCore.Sim;

public sealed partial class SimWorld
{
    // Easy-tier tunables (first-pass; tuned later).
    private const int AiDecisionInterval = 10;   // act roughly every 10 ticks
    private const int EasyWorkerCap = 8;
    private const int EasySupplyBuffer = 2;       // build supply when SupplyUsed >= SupplyCap - this
    private const int EasyAttackThreshold = 6;    // min combat units before attacking
    private const int EasyAttackInterval = 300;   // attack-move cadence (ticks)

    /// <summary>Mark a player as CPU-controlled at the given difficulty (setup-time).</summary>
    public void SetCpu(int playerId, AiDifficulty difficulty)
    {
        _players[playerId].Controller = PlayerController.Cpu;
        _players[playerId].Difficulty = difficulty;
    }

    /// <summary>Deterministic AI phase: each CPU player decides on a fixed cadence and issues
    /// commands through Apply. Integer/Fix only, no RNG (stable scans). Skips once the match is
    /// decided.</summary>
    private void UpdateAi()
    {
        if (Phase == MatchPhase.Over) return;
        if (Tick % AiDecisionInterval != 0) return;
        for (int p = 0; p < _players.Length; p++)
        {
            if (_players[p].Controller != PlayerController.Cpu) continue;
            switch (_players[p].Difficulty)
            {
                default: EasyDecide(p); break; // Medium/Hard fall back to Easy until 5d
            }
        }
    }

    // --- role resolution (from the acting player's faction catalog) ---
    private UnitDef? WorkerDef(int p)
    {
        var f = FactionFor(p);
        if (f is null) return null;
        foreach (var u in f.UnitList) if (u.Spec.Harvester is not null) return u;
        return null;
    }

    private UnitDef? CombatDef(int p)
    {
        var f = FactionFor(p);
        if (f is null) return null;
        UnitDef? best = null;
        foreach (var u in f.UnitList)
            if (u.Spec.Weapon is not null && (best is null || u.Spec.MineralCost < best.Spec.MineralCost))
                best = u;
        return best;
    }

    private BuildingDef? SupplyDef(int p)
    {
        var f = FactionFor(p);
        if (f is null) return null;
        foreach (var b in f.BuildingList) if (b.Spec.SupplyProvided > 0) return b;
        return null;
    }

    /// <summary>Id of a complete, train-capable building of p that produces the given def id; 0 if none.</summary>
    private int ProducerBuildingId(int p, string producedByDefId)
    {
        foreach (var b in _buildings)
            if (b.OwnerId == p && b.IsComplete && b.Spec.CanTrain && b.DefId == producedByDefId) return b.Id;
        return 0;
    }

    private int CountUnits(int p, bool combat)
    {
        int c = 0;
        foreach (var u in _units)
            if (u.OwnerId == p && (combat ? u.Weapon is not null : u.Harvester is not null)) c++;
        return c;
    }

    /// <summary>First idle worker of p (no harvest/move/attack order); null if none.</summary>
    private Unit? IdleWorker(int p)
    {
        foreach (var u in _units)
            if (u.OwnerId == p && u.Harvester is not null
                && u.HarvestPhase == HarvestPhase.None && !u.HasMoveOrder && u.AttackTargetId == 0)
                return u;
        return null;
    }

    /// <summary>A worker to place a building with: an idle one if available, else the first owned
    /// worker (stable order). A BuildCommand only needs the worker within range of the site at
    /// placement — it neither consumes nor interrupts the worker (construction proceeds on its
    /// own), so a harvesting worker can place supply without abandoning its route. Needed because
    /// the harvest step assigns every idle worker, leaving none idle by the supply step.</summary>
    private Unit? BuilderWorker(int p)
    {
        var idle = IdleWorker(p);
        if (idle is not null) return idle;
        foreach (var u in _units)
            if (u.OwnerId == p && u.Harvester is not null) return u;
        return null;
    }

    /// <summary>Nearest non-empty node to a position (squared distance; first wins ties); 0 if none.</summary>
    private int NearestNodeId(FixVec pos)
    {
        int best = 0;
        long bestSq = long.MaxValue;
        foreach (var n in _nodes)
        {
            if (n.Remaining <= 0) continue;
            long sq = (Map.CellCenter(n.CellX, n.CellY) - pos).LengthSquared().Raw;
            if (sq < bestSq) { bestSq = sq; best = n.Id; }
        }
        return best;
    }

    /// <summary>First placeable wxh footprint near pos (ring scan) within build range of pos; null if none.</summary>
    private (int x, int y)? FreeFootprintNear(FixVec pos, int w, int h)
    {
        var (cx, cy) = Map.WorldToCell(pos);
        for (int r = 1; r <= 4; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (!FootprintPlaceable(x, y, w, h)) continue;
                    if ((pos - FootprintCenter(x, y, w, h)).LengthSquared() <= Fix.FromInt(16)) return (x, y);
                }
        return null;
    }

    /// <summary>Easy tier economy: train workers, harvest, build supply. Filled further in Task 3.</summary>
    private void EasyDecide(int playerId)
    {
        var ps = _players[playerId];

        // 1. Train workers up to the cap.
        var wdef = WorkerDef(playerId);
        if (wdef is not null && CountUnits(playerId, combat: false) < EasyWorkerCap)
        {
            int prod = ProducerBuildingId(playerId, wdef.ProducedBy);
            if (prod != 0) Apply(new TrainCommand(playerId, prod, wdef.Id));
        }

        // 2. Send every idle worker to the nearest node.
        var idleIds = new System.Collections.Generic.List<int>();
        foreach (var u in _units)
            if (u.OwnerId == playerId && u.Harvester is not null
                && u.HarvestPhase == HarvestPhase.None && !u.HasMoveOrder && u.AttackTargetId == 0)
                idleIds.Add(u.Id);
        foreach (var id in idleIds)
        {
            var u = GetUnit(id);
            if (u is null) continue;
            int node = NearestNodeId(u.Position);
            if (node != 0) Apply(new HarvestCommand(playerId, new[] { id }, node));
        }

        // 3. Build a supply building when near the cap. Use any worker (idle preferred): placing a
        //    building does not interrupt harvesting, and at the worker cap every worker is busy.
        var sdef = SupplyDef(playerId);
        if (sdef is not null && ps.SupplyUsed >= ps.SupplyCap - EasySupplyBuffer
            && ps.Minerals >= sdef.Spec.MineralCost)
        {
            var bw = BuilderWorker(playerId);
            if (bw is not null)
            {
                var cell = FreeFootprintNear(bw.Position, sdef.Spec.Width, sdef.Spec.Height);
                if (cell is not null) Apply(new BuildCommand(playerId, bw.Id, sdef.Id, cell.Value.x, cell.Value.y));
            }
        }
    }
}
