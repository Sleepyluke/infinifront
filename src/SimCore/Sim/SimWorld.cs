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
        var u = new Unit
        {
            Id = _nextId++, OwnerId = ownerId, Position = pos, SpeedPerTick = speedPerTick,
            Hp = hp, SupplyCost = supplyCost, Weapon = weapon, Harvester = harvester
        };
        _units.Add(u);
        _byId[u.Id] = u;
        _players[ownerId].SupplyUsed += supplyCost;
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
        foreach (var cmd in commands) Apply(cmd);
        UpdateCombat();
        MoveUnits();
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
                    var t = GetUnit(atk.TargetId);
                    if (t is null || t.OwnerId == atk.PlayerId) continue;
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
        }
    }

    private void MoveUnits()
    {
        foreach (var u in _units)
        {
            if (!u.HasMoveOrder) continue;

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
            if (dist <= u.SpeedPerTick)
            {
                u.Position += step;
                if (cx == ttx && cy == tty) { u.HasMoveOrder = false; u.Path = null; }
            }
            else
            {
                u.Position += step.Normalized() * u.SpeedPerTick;
            }
        }
    }

    /// <summary>Reverse-index removal preserves survivor order (list order = determinism contract).</summary>
    private void RemoveDead()
    {
        for (int i = _units.Count - 1; i >= 0; i--)
        {
            if (_units[i].Hp > 0) continue;
            _players[_units[i].OwnerId].SupplyUsed -= _units[i].SupplyCost;
            _byId.Remove(_units[i].Id);
            _units.RemoveAt(i);
        }
    }
}
