using System.Collections.Generic;
using System.Linq;
using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

/// <summary>Diffs sim state by entity id after every tick; owns view node lifecycle.</summary>
public partial class ViewSync : Node2D
{
    private SimRunner _runner = null!;
    private readonly Dictionary<int, UnitView> _units = new();
    private readonly Dictionary<int, BuildingView> _buildings = new();
    private readonly Dictionary<int, NodeView> _nodes = new();

    public IReadOnlyDictionary<int, UnitView> Units => _units;
    public IReadOnlyDictionary<int, BuildingView> Buildings => _buildings;

    /// <summary>Spec→sheet heuristic keyed off distinguishing stats; contained here
    /// by design. Replace with proper spec ids when faction packs land (plan 3).</summary>
    private static string KeyOf(Unit u) =>
        u.Harvester is not null ? "fabber"
        : u.Weapon is null ? "trooper"
        : u.Weapon.CooldownTicks >= 20 ? "tank"
        : u.Weapon.CooldownTicks <= 5 ? "outrider"
        : "trooper";

    public void Init(SimRunner runner)
    {
        _runner = runner;
        YSortEnabled = true;
        runner.Ticked += OnTick;
        OnTick(); // initial population
    }

    private void OnTick()
    {
        var w = _runner.World;

        var live = new HashSet<int>();
        foreach (var u in w.Units)
        {
            live.Add(u.Id);
            if (!_units.TryGetValue(u.Id, out var v))
            {
                v = new UnitView();
                AddChild(v);
                v.Init(u, KeyOf(u), _runner);
                _units[u.Id] = v;
            }
            v.SyncTick(u);
            if (u.AttackTargetId != 0)
            {
                var tu = w.GetUnit(u.AttackTargetId);
                if (tu is not null) v.FaceToward(RenderMath.ToPx(tu.Position));
                else if (w.GetBuilding(u.AttackTargetId) is { } tb)
                    v.FaceToward(RenderMath.CellToPx(tb.CellX, tb.CellY));
            }
        }
        foreach (var id in _units.Keys.Where(id => !live.Contains(id)).ToList())
        {
            _units[id].PlayDeathAndFree(); // corpse node frees itself after anim
            _units.Remove(id);
        }

        var liveB = new HashSet<int>();
        foreach (var b in w.Buildings)
        {
            liveB.Add(b.Id);
            if (!_buildings.TryGetValue(b.Id, out var v))
            {
                v = new BuildingView();
                AddChild(v);
                v.Init(b);
                _buildings[b.Id] = v;
            }
            v.SyncTick(b);
        }
        foreach (var id in _buildings.Keys.Where(id => !liveB.Contains(id)).ToList())
        {
            _buildings[id].PlayDestructionAndFree();
            _buildings.Remove(id);
        }

        var liveN = new HashSet<int>();
        foreach (var n in w.Nodes)
        {
            liveN.Add(n.Id);
            if (!_nodes.TryGetValue(n.Id, out var v))
            {
                v = new NodeView();
                AddChild(v);
                v.Init(n);
                _nodes[n.Id] = v;
            }
        }
        foreach (var id in _nodes.Keys.Where(id => !liveN.Contains(id)).ToList())
        {
            _nodes[id].QueueFree();
            _nodes.Remove(id);
        }
    }
}
