using System.Collections.Generic;
using SimCore.Math;

namespace SimCore.Sim;

public sealed partial class SimWorld
{
    public MapGrid Map { get; }
    public int Tick { get; private set; }
    public DeterministicRandom Rng { get; }

    private readonly List<Unit> _units = new(); // stable order — required for determinism
    private readonly Dictionary<int, Unit> _byId = new();
    private readonly Dictionary<(int, int), FlowField> _fieldCache = new(); // lookup only — never iterated
    private int _fieldCacheVersion; // Map.Version the cache was built against
    private int _nextId = 1;
    internal int NextIdForHashing => _nextId;

    private readonly PlayerState[] _players;
    public System.Collections.Generic.IReadOnlyList<PlayerState> Players => _players;

    public SimWorld(MapGrid map, ulong seed, int playerCount = 2)
    {
        Map = map;
        Rng = new DeterministicRandom(seed);
        _players = new PlayerState[playerCount];
        for (int i = 0; i < playerCount; i++) _players[i] = new PlayerState();
    }

    public IReadOnlyList<Unit> Units => _units;
    public Unit? GetUnit(int id) => _byId.TryGetValue(id, out var u) ? u : null;

    public int SpawnUnit(int ownerId, FixVec pos, Fix speedPerTick, int hp, Weapon? weapon = null)
    {
        // Legacy overload: clones the weapon so callers can never alias cooldown state.
        var clone = weapon is null ? null : new Weapon
        {
            Damage = weapon.Damage, Range = weapon.Range,
            CooldownTicks = weapon.CooldownTicks, CooldownRemaining = weapon.CooldownRemaining
        };
        return Spawn(ownerId, pos, speedPerTick, hp, supplyCost: 0, clone, harvester: null);
    }

    public int SpawnUnit(int ownerId, FixVec pos, UnitSpec spec) =>
        Spawn(ownerId, pos, spec.Speed, spec.MaxHp, spec.SupplyCost, spec.Weapon?.Instantiate(), spec.Harvester);

    private int Spawn(int ownerId, FixVec pos, Fix speedPerTick, int hp, int supplyCost, Weapon? weapon, HarvesterSpec? harvester)
    {
        EnsureOccupancy();
        var (cx, cy) = Map.WorldToCell(pos);
        if (OccupantAt(cx, cy) != 0) return 0; // cell occupied — reject without consuming id or supply

        var u = new Unit
        {
            Id = _nextId++, OwnerId = ownerId, Position = pos, SpeedPerTick = speedPerTick,
            Hp = hp, SupplyCost = supplyCost, Weapon = weapon, Harvester = harvester
        };
        _units.Add(u);
        _byId[u.Id] = u;
        _players[ownerId].SupplyUsed += supplyCost;
        ClaimCell(cx, cy, u.Id);
        return u.Id;
    }

    /// <summary>Version-scoped flow-field cache (persists across ticks for an unchanged map).
    /// Version-guarded so a mid-Step passability change can never serve a stale field.</summary>
    private FlowField GetField(int targetCellX, int targetCellY)
    {
        if (_fieldCacheVersion != Map.Version)
        {
            _fieldCache.Clear();
            _fieldCacheVersion = Map.Version;
        }
        if (!_fieldCache.TryGetValue((targetCellX, targetCellY), out var f))
        {
            f = FlowField.Compute(Map, targetCellX, targetCellY);
            _fieldCache[(targetCellX, targetCellY)] = f;
        }
        return f;
    }

    /// <summary>Advance one tick. Commands are applied first, then systems run in fixed order.</summary>
    public void Step(IReadOnlyList<Command> commands)
    {
        EnsureOccupancy();
        foreach (var cmd in commands) Apply(cmd);
        UpdateCombat();
        MoveUnits();
        UpdateHarvest();
        UpdateConstruction();
        UpdateProduction();
        RemoveDead();
        RemoveDeadBuildings();
        Tick++;
    }

    private void Apply(Command cmd)
    {
        switch (cmd)
        {
            case MoveCommand mv:
                var (tx, ty) = Map.WorldToCell(mv.Target);
                var field = GetField(tx, ty); // per-tick cache
                foreach (var id in mv.UnitIds)
                {
                    var u = GetUnit(id);
                    if (u is null || u.OwnerId != mv.PlayerId) continue;
                    u.IsAttackMoving = false;
                    u.AttackTargetId = 0;
                    u.HarvestPhase = HarvestPhase.None;
                    u.HasMoveOrder = true;
                    u.MoveTarget = mv.Target;
                    u.Path = field;
                    u.PathVersion = Map.Version;
                }
                break;
            case AttackCommand atk:
                foreach (var id in atk.UnitIds)
                {
                    var u = GetUnit(id);
                    if (u is null || u.OwnerId != atk.PlayerId || u.Weapon is null) continue;
                    var tu = GetUnit(atk.TargetId);
                    var tb = tu is null ? GetBuilding(atk.TargetId) : null;
                    if (tu is null && tb is null) continue;
                    if ((tu?.OwnerId ?? tb!.OwnerId) == atk.PlayerId) continue; // no friendly fire
                    u.AttackTargetId = atk.TargetId;
                    u.IsAttackMoving = false; // explicit attack order replaces any attack-move
                    u.HasMoveOrder = false;
                    u.Path = null;
                }
                break;
            case AttackMoveCommand am:
                var (amx, amy) = Map.WorldToCell(am.Target);
                var amField = GetField(amx, amy);
                foreach (var id in am.UnitIds)
                {
                    var u = GetUnit(id);
                    if (u is null || u.OwnerId != am.PlayerId) continue;
                    u.IsAttackMoving = u.Weapon is not null; // weaponless units treat this as a plain move
                    u.AttackMoveDest = am.Target;
                    u.AttackTargetId = 0;
                    u.HasMoveOrder = true;
                    u.MoveTarget = am.Target;
                    u.Path = amField;
                    u.PathVersion = Map.Version;
                }
                break;
            case BuildCommand bc:
                var builder = GetUnit(bc.WorkerUnitId);
                if (builder is null || builder.OwnerId != bc.PlayerId) break;
                if (_players[bc.PlayerId].Minerals < bc.Spec.MineralCost) break;
                var siteCenter = FootprintCenter(bc.CellX, bc.CellY, bc.Spec.Width, bc.Spec.Height);
                if ((builder.Position - siteCenter).LengthSquared() > Fix.FromInt(16)) break; // worker within 4 of site center
                if (!FootprintPlaceable(bc.CellX, bc.CellY, bc.Spec.Width, bc.Spec.Height)) break;
                _players[bc.PlayerId].Minerals -= bc.Spec.MineralCost;
                PlaceBuilding(bc.PlayerId, bc.Spec, bc.CellX, bc.CellY);
                break;
            case TrainCommand tc:
                var trainer = GetBuilding(tc.BuildingId);
                if (trainer is null || trainer.OwnerId != tc.PlayerId || !trainer.IsComplete || !trainer.Spec.CanTrain) break;
                if (trainer.Queue.Count >= Building.MaxQueueLength) break;
                var ps = _players[tc.PlayerId];
                if (ps.Minerals < tc.Spec.MineralCost) break;
                if (ps.SupplyUsed + tc.Spec.SupplyCost > ps.SupplyCap) break;
                ps.Minerals -= tc.Spec.MineralCost;
                ps.SupplyUsed += tc.Spec.SupplyCost; // reserve supply at enqueue
                trainer.Queue.Add(new TrainingItem { Spec = tc.Spec, RemainingTicks = tc.Spec.BuildTimeTicks });
                break;
            case HarvestCommand hc:
                if (GetNode(hc.NodeId) is null) break;
                foreach (var id in hc.UnitIds)
                {
                    var u = GetUnit(id);
                    if (u is null || u.OwnerId != hc.PlayerId || u.Harvester is null) continue;
                    u.HarvestPhase = HarvestPhase.MovingToNode;
                    u.HarvestNodeId = hc.NodeId;
                    u.AttackTargetId = 0;
                    u.IsAttackMoving = false;
                    u.HasMoveOrder = false; // UpdateHarvest issues the approach
                    u.Path = null;
                }
                break;
        }
    }

    // Per-tick set of unit ids that have already been moved by a head-on swap this tick.
    // Using a HashSet populated in spawn order (unit list iteration) — deterministic.
    private readonly System.Collections.Generic.HashSet<int> _swappedThisTick = new();

    private void MoveUnits()
    {
        _swappedThisTick.Clear();

        foreach (var u in _units)
        {
            if (!u.HasMoveOrder) continue;
            if (_swappedThisTick.Contains(u.Id)) continue; // already moved by a swap this tick

            var (cx, cy) = Map.WorldToCell(u.Position);
            var (ttx, tty) = Map.WorldToCell(u.MoveTarget);

            FixVec step;
            if (cx == ttx && cy == tty)
            {
                // final cell: home in on the exact target point
                step = u.MoveTarget - u.Position;
            }
            else
            {
                if (u.Path is null || u.PathVersion != Map.Version)
                {
                    var (rtx, rty) = Map.WorldToCell(u.MoveTarget);
                    u.Path = GetField(rtx, rty);
                    u.PathVersion = Map.Version;
                }
                var (dx, dy) = u.Path.DirectionAt(cx, cy);
                if (dx == 0 && dy == 0) { u.HasMoveOrder = false; u.Path = null; continue; } // unreachable
                step = Map.CellCenter(cx + dx, cy + dy) - u.Position;
            }

            var dist = step.Length();
            FixVec newPos;
            if (dist <= u.SpeedPerTick)
            {
                newPos = u.Position + step;
            }
            else
            {
                newPos = u.Position + step.Normalized() * u.SpeedPerTick;
            }

            // Cell-crossing check: if moving into a new cell, verify it is unoccupied.
            // Iteration over _units in spawn order = stable priority = deterministic.
            var (ncx, ncy) = Map.WorldToCell(newPos);
            if (ncx != cx || ncy != cy)
            {
                var occ = OccupantAt(ncx, ncy);
                if (occ != 0 && occ != u.Id)
                {
                    // Destination cell is occupied by another unit.

                    // --- Head-on swap rule (checked BEFORE arrival relaxation) ---
                    // If B (the blocker) has a move order and B's next-step cell is A's (u's)
                    // current cell, the two units are heading directly into each other.
                    // Allow the pass: swap their positions to their respective target cell centers
                    // this tick, so they pass through instead of deadlocking forever.
                    var blocker = _byId.TryGetValue(occ, out var bl) ? bl : null;
                    if (blocker is not null && blocker.HasMoveOrder && !_swappedThisTick.Contains(occ))
                    {
                        // Compute blocker's next-step cell from its flow field.
                        var (bcx, bcy) = Map.WorldToCell(blocker.Position);
                        var (bdx, bdy) = blocker.Path is not null
                            ? blocker.Path.DirectionAt(bcx, bcy)
                            : (0, 0);
                        if (bdx == 0 && bdy == 0 && blocker.Path is null)
                        {
                            // blocker in final-homing phase: target cell is its move-target cell
                            (bdx, bdy) = (0, 0); // will not trigger swap
                        }
                        var (bnx, bny) = (bcx + bdx, bcy + bdy);
                        if (bnx == cx && bny == cy)
                        {
                            // Head-on: A wants B's cell, B wants A's cell → swap.
                            // Both units move to the other's current cell center.
                            var aNewPos = Map.CellCenter(bcx, bcy);
                            var bNewPos = Map.CellCenter(cx, cy);

                            ReleaseCell(cx, cy, u.Id);
                            ReleaseCell(bcx, bcy, blocker.Id);
                            ClaimCell(bcx, bcy, u.Id);
                            ClaimCell(cx, cy, blocker.Id);

                            u.Position = aNewPos;
                            blocker.Position = bNewPos;

                            _swappedThisTick.Add(u.Id);
                            _swappedThisTick.Add(blocker.Id);

                            // Check arrival for u after the swap
                            var (ancx, ancy) = Map.WorldToCell(u.Position);
                            var (attx, atty) = Map.WorldToCell(u.MoveTarget);
                            if (ancx == attx && ancy == atty)
                            {
                                u.HasMoveOrder = false;
                                u.Path = null;
                            }
                            // Check arrival for blocker after the swap
                            var (bncx, bncy) = Map.WorldToCell(blocker.Position);
                            var (bttx, btty) = Map.WorldToCell(blocker.MoveTarget);
                            if (bncx == bttx && bncy == btty)
                            {
                                blocker.HasMoveOrder = false;
                                blocker.Path = null;
                            }
                            continue;
                        }
                    }

                    // --- Arrival relaxation ---
                    // When blocked and the contested cell is the move-target cell, treat as arrived.
                    // Also treat as arrived when the blocker is stationary (no move order) and
                    // the blocked unit's current cell is adjacent to the contested cell (Chebyshev 1).
                    var (dtx, dty) = Map.WorldToCell(u.MoveTarget);
                    bool contestedIsTarget = (ncx == dtx && ncy == dty);
                    bool blockerStationary = blocker is not null && !blocker.HasMoveOrder;
                    bool adjacentToContest = System.Math.Abs(cx - ncx) <= 1 && System.Math.Abs(cy - ncy) <= 1;
                    if (contestedIsTarget || (blockerStationary && adjacentToContest))
                    {
                        u.HasMoveOrder = false;
                        u.Path = null;
                    }
                    // Either way: hold position this tick (don't move into the occupied cell).
                    continue;
                }
                ReleaseCell(cx, cy, u.Id);
                ClaimCell(ncx, ncy, u.Id);
            }

            u.Position = newPos;
            if (dist <= u.SpeedPerTick && cx == ttx && cy == tty)
            {
                u.HasMoveOrder = false;
                u.Path = null;
            }
        }
    }

    /// <summary>Reverse-index removal preserves survivor order (list order = determinism contract).</summary>
    private void RemoveDead()
    {
        for (int i = _units.Count - 1; i >= 0; i--)
        {
            if (_units[i].Hp > 0) continue;
            var dead = _units[i];
            var (cx, cy) = Map.WorldToCell(dead.Position);
            ReleaseCell(cx, cy, dead.Id);
            _players[dead.OwnerId].SupplyUsed -= dead.SupplyCost;
            _byId.Remove(dead.Id);
            _units.RemoveAt(i);
        }
    }
}
