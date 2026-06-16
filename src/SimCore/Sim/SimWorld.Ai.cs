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

    // Medium-tier tunables: bigger economy, rebuilds lost production, earlier/sustained attacks.
    private const int MediumWorkerCap = 10;
    private const int MediumSupplyBuffer = 3;
    private const int MediumAttackThreshold = 4;
    private const int MediumAttackInterval = 150;

    // Hard-tier tunables: strong economy, rebuilds, reactive attack (defend / commit-when-ahead).
    private const int HardWorkerCap = 14;
    private const int HardSupplyBuffer = 4;
    private const int HardMinArmy = 8;
    private const int HardAttackInterval = 120;
    private const int HardDefendRadius = 10; // cells

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
                case AiDifficulty.Medium: MediumDecide(p); break;
                case AiDifficulty.Hard:   HardDecide(p);   break;
                default: EasyDecide(p); break; // Easy
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
        BuildingDef? fallback = null;
        foreach (var b in f.BuildingList)
            if (b.Spec.SupplyProvided > 0)
            {
                if (!b.Spec.IsDepot && !b.Spec.CanTrain) return b;  // prefer the cheap supply-only building
                fallback ??= b;
            }
        return fallback;
    }

    /// <summary>Id of a complete, train-capable building of p that produces the given def id; 0 if none.</summary>
    private int ProducerBuildingId(int p, string producedByDefId)
    {
        foreach (var b in _buildings)
            if (b.OwnerId == p && b.IsComplete && b.Spec.CanTrain && b.DefId == producedByDefId) return b.Id;
        return 0;
    }

    /// <summary>True if p owns a supply-providing building still under construction. Guards the
    /// supply step so it queues one depot at a time instead of spamming a fresh depot every
    /// decision tick (each takes ~150 ticks to finish, far longer than the 10-tick cadence) —
    /// the spam drains minerals and crowds the harvest area, stalling the economy.</summary>
    private bool HasSupplyUnderConstruction(int p)
    {
        foreach (var b in _buildings)
            if (b.OwnerId == p && !b.IsComplete && b.Spec.SupplyProvided > 0) return true;
        return false;
    }

    private int CountUnits(int p, bool combat)
    {
        int c = 0;
        foreach (var u in _units)
            if (u.OwnerId == p && (combat ? u.Weapon is not null : u.Harvester is not null)) c++;
        return c;
    }

    /// <summary>Count of p's units of a given def that are already queued (in production) across all
    /// owned buildings. Added so the worker cap counts in-flight trains, not just completed units —
    /// otherwise the AI re-queues every decision while completed &lt; cap and overshoots once a batch
    /// lands (worse when minerals are tight, e.g. once army training competes for income).</summary>
    private int QueuedUnits(int p, string unitDefId)
    {
        int c = 0;
        foreach (var b in _buildings)
            if (b.OwnerId == p)
                foreach (var it in b.Queue)
                    if (it.UnitDefId == unitDefId) c++;
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

    /// <summary>Center of the first enemy building (lowest id, stable order); null if none.</summary>
    private FixVec? EnemyBaseCenter(int p)
    {
        foreach (var b in _buildings) if (!SameTeam(b.OwnerId, p)) return CenterOf(b);
        return null;
    }

    private int[] CombatUnitIds(int p)
    {
        var ids = new System.Collections.Generic.List<int>();
        foreach (var u in _units) if (u.OwnerId == p && u.Weapon is not null) ids.Add(u.Id);
        return ids.ToArray();
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

    /// <summary>Economy macro: train workers up to workerCap (counting in-flight trains), send
    /// idle workers to the nearest node, and build supply when within supplyBuffer of the cap.</summary>
    private void MacroEconomy(int playerId, int workerCap, int supplyBuffer)
    {
        var ps = _players[playerId];

        var wdef = WorkerDef(playerId);
        if (wdef is not null
            && CountUnits(playerId, combat: false) + QueuedUnits(playerId, wdef.Id) < workerCap)
        {
            int prod = ProducerBuildingId(playerId, wdef.ProducedBy);
            if (prod != 0) Apply(new TrainCommand(playerId, prod, wdef.Id));
        }

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

        var sdef = SupplyDef(playerId);
        if (sdef is not null && ps.SupplyUsed >= ps.SupplyCap - supplyBuffer
            && ps.Minerals >= sdef.Spec.MineralCost
            && !HasSupplyUnderConstruction(playerId)) // one at a time — don't spam depots while one is building
        {
            var bw = BuilderWorker(playerId);
            if (bw is not null)
            {
                var cell = FreeFootprintNear(bw.Position, sdef.Spec.Width, sdef.Spec.Height);
                if (cell is not null) Apply(new BuildCommand(playerId, bw.Id, sdef.Id, cell.Value.x, cell.Value.y));
            }
        }
    }

    /// <summary>Train the cheapest combat unit from its producer when minerals + supply allow.</summary>
    private void TrainCheapestCombat(int playerId)
    {
        var ps = _players[playerId];
        var cdef = CombatDef(playerId);
        if (cdef is null) return;
        int prod = ProducerBuildingId(playerId, cdef.ProducedBy);
        if (prod != 0 && ps.Minerals >= cdef.Spec.MineralCost
            && ps.SupplyUsed + cdef.Spec.SupplyCost <= ps.SupplyCap)
            Apply(new TrainCommand(playerId, prod, cdef.Id));
    }

    /// <summary>Attack-move the whole army at the enemy base when the army is big enough, on cadence.</summary>
    private void MaybeAttack(int playerId, int threshold, int interval)
    {
        if (CountUnits(playerId, combat: true) >= threshold && Tick % interval == 0)
        {
            var target = EnemyBaseCenter(playerId);
            if (target is not null)
            {
                var army = CombatUnitIds(playerId);
                if (army.Length > 0) Apply(new AttackMoveCommand(playerId, army, target.Value));
            }
        }
    }

    /// <summary>Easy tier: light macro + cheapest-combat army + occasional weak attack.</summary>
    private void EasyDecide(int playerId)
    {
        MacroEconomy(playerId, EasyWorkerCap, EasySupplyBuffer);
        TrainCheapestCombat(playerId);
        MaybeAttack(playerId, EasyAttackThreshold, EasyAttackInterval);
    }

    /// <summary>If the player owns no building (any construction state) that produces its combat
    /// unit, build that producer (when affordable, prereqs met, and a worker exists). The
    /// supply/worker producer (depot) is rebuilt by MacroEconomy's supply step instead.</summary>
    private void RebuildProduction(int playerId)
    {
        var cdef = CombatDef(playerId);
        if (cdef is null) return;
        foreach (var b in _buildings)
            if (b.OwnerId == playerId && b.DefId == cdef.ProducedBy) return; // already have/building one
        var bdef = FactionFor(playerId)?.GetBuilding(cdef.ProducedBy);
        if (bdef is null) return;
        var ps = _players[playerId];
        if (ps.Minerals < bdef.Spec.MineralCost) return;
        if (!PrerequisitesMet(playerId, bdef.Requires)) return;
        var bw = BuilderWorker(playerId);
        if (bw is null) return;
        var cell = FreeFootprintNear(bw.Position, bdef.Spec.Width, bdef.Spec.Height);
        if (cell is not null) Apply(new BuildCommand(playerId, bw.Id, bdef.Id, cell.Value.x, cell.Value.y));
    }

    /// <summary>Medium tier: bigger economy, rebuilds lost production, earlier/sustained attacks.</summary>
    private void MediumDecide(int playerId)
    {
        MacroEconomy(playerId, MediumWorkerCap, MediumSupplyBuffer);
        RebuildProduction(playerId);
        TrainCheapestCombat(playerId);
        MaybeAttack(playerId, MediumAttackThreshold, MediumAttackInterval);
    }

    /// <summary>Center of the first owned building (stable order) with an enemy combat unit within
    /// HardDefendRadius cells; null if none threatened.</summary>
    private FixVec? ThreatenedBuildingCenter(int playerId)
    {
        long radiusSq = Fix.FromInt(HardDefendRadius * HardDefendRadius).Raw;
        foreach (var b in _buildings)
        {
            if (b.OwnerId != playerId) continue;
            var c = CenterOf(b);
            foreach (var u in _units)
            {
                if (SameTeam(u.OwnerId, playerId) || u.Weapon is null) continue;
                if ((u.Position - c).LengthSquared().Raw <= radiusSq) return c;
            }
        }
        return null;
    }

    /// <summary>Count of all non-p units with a weapon (the enemy army; full-information).</summary>
    private int EnemyCombatCount(int playerId)
    {
        int c = 0;
        foreach (var u in _units) if (!SameTeam(u.OwnerId, playerId) && u.Weapon is not null) c++;
        return c;
    }

    /// <summary>Reactive attack: defend a threatened base; else commit only when at-or-ahead of the
    /// enemy army (and past a minimum), on cadence; otherwise keep massing.</summary>
    private void ReactiveAttack(int playerId)
    {
        var threat = ThreatenedBuildingCenter(playerId);
        if (threat is not null)
        {
            var defenders = CombatUnitIds(playerId);
            if (defenders.Length > 0) Apply(new AttackMoveCommand(playerId, defenders, threat.Value));
            return;
        }
        int own = CountUnits(playerId, combat: true);
        if (own >= HardMinArmy && own >= EnemyCombatCount(playerId) && Tick % HardAttackInterval == 0)
        {
            var target = EnemyBaseCenter(playerId);
            if (target is not null)
            {
                var army = CombatUnitIds(playerId);
                if (army.Length > 0) Apply(new AttackMoveCommand(playerId, army, target.Value));
            }
        }
    }

    /// <summary>Hard tier: strong economy, rebuild, reactive attack (defend / commit-when-ahead).</summary>
    private void HardDecide(int playerId)
    {
        MacroEconomy(playerId, HardWorkerCap, HardSupplyBuffer);
        RebuildProduction(playerId);
        TrainCheapestCombat(playerId);
        ReactiveAttack(playerId);
    }
}
