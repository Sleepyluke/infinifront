using System.Collections.Generic;
using SimCore.Math;

namespace SimCore.Sim;

public sealed partial class SimWorld
{
    public MapGrid Map { get; }
    public int Tick { get; private set; }
    public DeterministicRandom Rng { get; }
    public FactionDef? Faction { get; }

    private readonly List<Unit> _units = new(); // stable order — required for determinism
    private readonly Dictionary<int, Unit> _byId = new();
    private readonly Dictionary<(int, int), FlowField> _fieldCache = new(); // lookup only — never iterated
    private int _fieldCacheVersion; // Map.Version the cache was built against
    private int _nextId = 1;
    internal int NextIdForHashing => _nextId;

    private readonly PlayerState[] _players;
    public System.Collections.Generic.IReadOnlyList<PlayerState> Players => _players;

    public SimWorld(MapGrid map, ulong seed, int playerCount = 2, FactionDef? faction = null)
    {
        Map = map;
        Rng = new DeterministicRandom(seed);
        Faction = faction;
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
        return Spawn(ownerId, pos, speedPerTick, hp, supplyCost: 0, clone, harvester: null, sightRange: 7);
    }

    public int SpawnUnit(int ownerId, FixVec pos, UnitSpec spec) =>
        Spawn(ownerId, pos, spec.Speed, spec.MaxHp, spec.SupplyCost, spec.Weapon?.Instantiate(), spec.Harvester, spec.SightRange);

    private int Spawn(int ownerId, FixVec pos, Fix speedPerTick, int hp, int supplyCost, Weapon? weapon, HarvesterSpec? harvester, int sightRange)
    {
        EnsureOccupancy();
        var (cx, cy) = Map.WorldToCell(pos);
        if (OccupantAt(cx, cy) != 0) return 0; // cell occupied — reject without consuming id or supply

        var u = new Unit
        {
            Id = _nextId++, OwnerId = ownerId, Position = pos, SpeedPerTick = speedPerTick,
            Hp = hp, SupplyCost = supplyCost, Weapon = weapon, Harvester = harvester,
            SightRange = sightRange
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

    /// <summary>Advance one tick. Vision is updated before commands are applied so that
    /// AttackCommand validation reads fresh grids (not stale/empty from the previous tick).
    /// Vision depends only on positions, which commands do not change during Apply.</summary>
    public void Step(IReadOnlyList<Command> commands)
    {
        EnsureOccupancy();
        UpdateVision();
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
                    u.IsPatrolling = false;
                    u.AttackTargetId = 0;
                    u.HasAnchor = false;
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
                    // Fog: reject explicit attacks on targets whose cell isn't visible to the issuing player.
                    if (FogEnabled)
                    {
                        var (vcx, vcy) = tu is not null
                            ? Map.WorldToCell(tu.Position)
                            : Map.WorldToCell(CenterOf(tb!));
                        if (!IsVisibleTo(atk.PlayerId, vcx, vcy)) continue;
                    }
                    u.AttackTargetId = atk.TargetId;
                    u.IsAttackMoving = false; // explicit attack order replaces any attack-move
                    u.IsPatrolling = false;
                    u.HasAnchor = false;
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
                    u.IsPatrolling = false;
                    u.AttackMoveDest = am.Target;
                    u.AttackTargetId = 0;
                    u.HasAnchor = false;
                    u.HasMoveOrder = true;
                    u.MoveTarget = am.Target;
                    u.Path = amField;
                    u.PathVersion = Map.Version;
                }
                break;
            case BuildCommand bc:
                var bdef = Faction?.GetBuilding(bc.BuildingDefId);
                if (bdef is null) break; // unknown def id (or no faction) — reject
                var builder = GetUnit(bc.WorkerUnitId);
                if (builder is null || builder.OwnerId != bc.PlayerId) break;
                if (!PrerequisitesMet(bc.PlayerId, bdef.Requires)) break;
                if (_players[bc.PlayerId].Minerals < bdef.Spec.MineralCost) break;
                var siteCenter = FootprintCenter(bc.CellX, bc.CellY, bdef.Spec.Width, bdef.Spec.Height);
                if ((builder.Position - siteCenter).LengthSquared() > Fix.FromInt(16)) break; // within 4 of site center
                if (!FootprintPlaceable(bc.CellX, bc.CellY, bdef.Spec.Width, bdef.Spec.Height)) break;
                _players[bc.PlayerId].Minerals -= bdef.Spec.MineralCost;
                PlaceBuilding(bc.PlayerId, bdef.Spec, bc.CellX, bc.CellY);
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
                    u.IsPatrolling = false;
                    u.HasAnchor = false;
                    u.HasMoveOrder = false; // UpdateHarvest issues the approach
                    u.Path = null;
                }
                break;
            case SetStanceCommand ss:
                foreach (var id in ss.UnitIds)
                {
                    var u = GetUnit(id);
                    if (u is null || u.OwnerId != ss.PlayerId) continue;
                    u.Stance = ss.Stance;
                    // Switching to Passive must break any active anchored (self-acquired) engagement:
                    // the player is saying "stop fighting", so we stand down immediately where we are.
                    // We do NOT call Disengage() here (unit's stance is now Passive so walking home
                    // is not appropriate either) — just clear the combat state explicitly.
                    // Explicit (command-ordered, anchorless) engagements are unaffected.
                    if (ss.Stance == Stance.Passive && u.HasAnchor)
                    {
                        u.AttackTargetId = 0;
                        u.HasAnchor = false;
                        // Anchored units' move orders are always chase artifacts (explicit orders clear anchors),
                        // so this is safe and matches "stand down where you are".
                        u.HasMoveOrder = false;
                        u.Path = null;
                    }
                    // SetStance does NOT cancel an active patrol — the unit keeps looping.
                    // For patrolling units, re-derive IsAttackMoving from the new stance so the
                    // engage mode matches immediately (e.g. Passive → stop engaging; AutoAttack → resume).
                    if (u.IsPatrolling)
                    {
                        u.IsAttackMoving = u.Weapon is not null && ss.Stance != Stance.Passive;
                        // If switching to Passive and the patroller has an active combat target,
                        // stand it down and re-issue the current patrol leg (PatrolB) so it
                        // doesn't strand mid-fight with no move order. Mirrors TryPassivePatrolSwap.
                        if (ss.Stance == Stance.Passive && u.AttackTargetId != 0)
                        {
                            u.AttackTargetId = 0;
                            var (pdx, pdy) = Map.WorldToCell(u.PatrolB);
                            u.HasMoveOrder = true;
                            u.MoveTarget = u.PatrolB;
                            u.Path = GetField(pdx, pdy);
                            u.PathVersion = Map.Version;
                        }
                    }
                }
                break;
            case PatrolCommand pc:
                var (pax, pay) = Map.WorldToCell(pc.Target);
                var patrolField = GetField(pax, pay);
                foreach (var id in pc.UnitIds)
                {
                    var u = GetUnit(id);
                    if (u is null || u.OwnerId != pc.PlayerId) continue;
                    u.PatrolA = u.Position;  // current position is leg A
                    u.PatrolB = pc.Target;   // target is leg B
                    u.IsPatrolling = true;
                    // Armed non-passive patrol uses attack-move so the unit engages enemies en route.
                    // Passive or weaponless patrol is a plain looping move.
                    u.IsAttackMoving = u.Weapon is not null && u.Stance != Stance.Passive;
                    u.AttackMoveDest = pc.Target;
                    u.AttackTargetId = 0;
                    u.HasAnchor = false;
                    u.HarvestPhase = HarvestPhase.None;
                    u.HasMoveOrder = true;
                    u.MoveTarget = pc.Target;
                    u.Path = patrolField;
                    u.PathVersion = Map.Version;
                }
                break;
            case SetRallyCommand sr:
                var srBuilding = GetBuilding(sr.BuildingId);
                if (srBuilding is null || srBuilding.OwnerId != sr.PlayerId) break;
                if (sr.Clear)
                    srBuilding.HasRally = false;
                else
                {
                    srBuilding.HasRally = true;
                    srBuilding.RallyPoint = sr.Target;
                }
                break;
            case DestroyCommand dc:
                foreach (var id in dc.Ids)
                {
                    var u = GetUnit(id);
                    if (u is not null && u.OwnerId == dc.PlayerId)
                    {
                        u.Hp = 0;
                        continue;
                    }
                    var b = GetBuilding(id);
                    if (b is not null && b.OwnerId == dc.PlayerId)
                    {
                        b.Hp = 0;
                    }
                }
                break;
        }
    }

    /// <summary>True if the player owns ≥1 complete building of every required def id.
    /// A placed building is matched to its def by reference-equality of its Spec instance
    /// (PlaceBuilding stores the def's Spec). Holds because Apply passes bdef.Spec to PlaceBuilding.</summary>
    private bool PrerequisitesMet(int playerId, IReadOnlyList<string> requires)
    {
        if (requires.Count == 0) return true;
        if (Faction is null) return false;
        foreach (var reqId in requires)
        {
            var reqDef = Faction.GetBuilding(reqId);
            if (reqDef is null) return false;
            bool owned = false;
            foreach (var b in _buildings)
                if (b.OwnerId == playerId && b.IsComplete && ReferenceEquals(b.Spec, reqDef.Spec))
                {
                    owned = true;
                    break;
                }
            if (!owned) return false;
        }
        return true;
    }

    // Per-tick set of unit ids that have already been moved by a head-on swap this tick.
    // Using a HashSet populated in spawn order (unit list iteration) — deterministic.
    private readonly System.Collections.Generic.HashSet<int> _swappedThisTick = new();

    /// <summary>For passive/weaponless patrollers (IsPatrolling &amp;&amp; !IsAttackMoving): when the
    /// move order is cleared (arrival or arrival-relaxation from a blocked endpoint), swap legs
    /// and issue the next leg. This mirrors the leg-swap done for armed patrollers in
    /// UpdateCombat's attack-move arrival branch.</summary>
    private void TryPassivePatrolSwap(Unit u)
    {
        if (!u.IsPatrolling || u.IsAttackMoving) return;
        // Swap legs and issue the next leg.
        (u.PatrolA, u.PatrolB) = (u.PatrolB, u.PatrolA);
        var (ndx, ndy) = Map.WorldToCell(u.PatrolB);
        u.HasMoveOrder = true;
        u.MoveTarget = u.PatrolB;
        u.Path = GetField(ndx, ndy);
        u.PathVersion = Map.Version;
    }

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
                if (dx == 0 && dy == 0)
                {
                    u.HasMoveOrder = false;
                    u.Path = null;
                    TryPassivePatrolSwap(u); // unreachable endpoint — swap to other leg
                    continue;
                }
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
                    var blocker = _byId.TryGetValue(occ, out var bl) ? bl : null;

                    if (TryHeadOnSwap(u, blocker, cx, cy, ncx, ncy))
                        continue;

                    if (ShouldRelaxArrival(u, blocker, cx, cy, ncx, ncy))
                    {
                        u.HasMoveOrder = false;
                        u.Path = null;
                        TryPassivePatrolSwap(u); // blocked endpoint — swap to other leg
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
                TryPassivePatrolSwap(u); // normal arrival — swap to other leg
            }
        }
    }

    /// <summary>Head-on swap: if the blocker's next-step cell is u's current cell, the two units
    /// are heading directly into each other. Swap them to avoid permanent deadlock.
    /// Returns true if the swap was performed (caller should continue to next unit).</summary>
    private bool TryHeadOnSwap(Unit u, Unit? blocker, int cx, int cy, int ncx, int ncy)
    {
        if (blocker is null || !blocker.HasMoveOrder || _swappedThisTick.Contains(blocker.Id))
            return false;

        var (bcx, bcy) = Map.WorldToCell(blocker.Position);
        var (bdx, bdy) = blocker.Path is not null
            ? blocker.Path.DirectionAt(bcx, bcy)
            : (0, 0);
        // No swap when blocker has no flow direction (unreachable / final-homing with no step).
        var (bnx, bny) = (bcx + bdx, bcy + bdy);
        if (bnx != cx || bny != cy)
            return false;

        // Head-on: A wants B's cell, B wants A's cell → swap.
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
        if (ancx == attx && ancy == atty) { u.HasMoveOrder = false; u.Path = null; TryPassivePatrolSwap(u); }

        // Check arrival for blocker after the swap
        var (bncx, bncy) = Map.WorldToCell(blocker.Position);
        var (bttx, btty) = Map.WorldToCell(blocker.MoveTarget);
        if (bncx == bttx && bncy == btty) { blocker.HasMoveOrder = false; blocker.Path = null; TryPassivePatrolSwap(blocker); }

        return true;
    }

    /// <summary>Arrival relaxation: a unit blocked from its next cell may treat itself as
    /// arrived early under two specific conditions only — never mid-path.
    ///
    /// Rule A — contested cell IS the move-target cell: the unit can't reach its exact
    ///   destination because it's occupied, so stopping one cell away is correct.
    ///
    /// Rule B — shared-destination group arrival: the blocker is stationary AND its
    ///   current cell is within Chebyshev 1 of the blocked unit's target cell, meaning
    ///   the blocker has already settled at (or very near) the same goal. The blocked
    ///   unit is one step away from a cell that is itself right beside the target —
    ///   continuing to push would just shuffle units around the target cluster.
    ///
    /// Crucially, Rule B requires the blocker to be physically at the target cluster
    /// (Chebyshev 1 of the target), NOT merely that the contested cell is Chebyshev 1
    /// of u's current cell (which is always true and caused the original bug).
    /// This prevents any idle unit mid-path from silently killing a move order.</summary>
    private bool ShouldRelaxArrival(Unit u, Unit? blocker, int cx, int cy, int ncx, int ncy)
    {
        var (dtx, dty) = Map.WorldToCell(u.MoveTarget);

        // Rule A: blocked cell is the exact target cell.
        if (ncx == dtx && ncy == dty) return true;

        // Rule B: blocker is stationary and physically at the target cluster.
        if (blocker is not null && !blocker.HasMoveOrder)
        {
            var (blockerCx, blockerCy) = Map.WorldToCell(blocker.Position);
            bool blockerAtTargetCluster =
                System.Math.Abs(blockerCx - dtx) <= 1 &&
                System.Math.Abs(blockerCy - dty) <= 1;
            if (blockerAtTargetCluster) return true;
        }

        return false;
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
