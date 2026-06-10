using System.Collections.Generic;
using SimCore.Math;

namespace SimCore.Sim;

public sealed class SimWorld
{
    public MapGrid Map { get; }
    public int Tick { get; private set; }
    public DeterministicRandom Rng { get; }

    private readonly List<Unit> _units = new(); // stable order — required for determinism
    private readonly Dictionary<int, Unit> _byId = new();
    private int _nextId = 1;

    public SimWorld(MapGrid map, ulong seed)
    {
        Map = map;
        Rng = new DeterministicRandom(seed);
    }

    public IReadOnlyList<Unit> Units => _units;
    public Unit? GetUnit(int id) => _byId.TryGetValue(id, out var u) ? u : null;

    public int SpawnUnit(int ownerId, FixVec pos, Fix speedPerTick, int hp)
    {
        var u = new Unit { Id = _nextId++, OwnerId = ownerId, Position = pos, SpeedPerTick = speedPerTick, Hp = hp };
        _units.Add(u);
        _byId[u.Id] = u;
        return u.Id;
    }

    /// <summary>Advance one tick. Commands are applied first, then systems run in fixed order.</summary>
    public void Step(IReadOnlyList<Command> commands)
    {
        foreach (var cmd in commands) Apply(cmd);
        MoveUnits();
        Tick++;
    }

    private void Apply(Command cmd)
    {
        switch (cmd)
        {
            case MoveCommand mv:
                foreach (var id in mv.UnitIds)
                {
                    var u = GetUnit(id);
                    if (u is null || u.OwnerId != mv.PlayerId) continue;
                    u.HasMoveOrder = true;
                    u.MoveTarget = mv.Target;
                    u.Path = null; // recomputed by pathfinding (Task 8)
                }
                break;
        }
    }

    private void MoveUnits()
    {
        foreach (var u in _units)
        {
            if (!u.HasMoveOrder) continue;
            var delta = u.MoveTarget - u.Position;
            var dist = delta.Length();
            if (dist <= u.SpeedPerTick)
            {
                u.Position = u.MoveTarget;
                u.HasMoveOrder = false;
            }
            else
            {
                u.Position += delta.Normalized() * u.SpeedPerTick;
            }
        }
    }
}
