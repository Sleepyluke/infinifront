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

    public SimWorld(MapGrid map, ulong seed)
    {
        Map = map;
        Rng = new DeterministicRandom(seed);
    }

    public IReadOnlyList<Unit> Units => _units;
    public Unit? GetUnit(int id) => _byId.TryGetValue(id, out var u) ? u : null;

    public int SpawnUnit(int ownerId, FixVec pos, Fix speedPerTick, int hp, Weapon? weapon = null)
    {
        var u = new Unit { Id = _nextId++, OwnerId = ownerId, Position = pos, SpeedPerTick = speedPerTick, Hp = hp, Weapon = weapon };
        _units.Add(u);
        _byId[u.Id] = u;
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
                    u.HasMoveOrder = false;
                    u.Path = null;
                }
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
            _byId.Remove(_units[i].Id);
            _units.RemoveAt(i);
        }
    }
}
