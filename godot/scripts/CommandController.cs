using System.Linq;
using Godot;
using SimCore.Math;
using SimCore.Sim;

namespace LlmRts.Godot;

/// <summary>Turns input + current selection into sim Commands. Owns the two
/// pending modes: attack-move (A key) and build-ghost placement.</summary>
public partial class CommandController : Node2D
{
    private SimRunner _runner = null!;
    private SelectionController _sel = null!;
    private ViewSync _view = null!;

    private bool _attackMoveArmed;
    private BuildingSpec? _ghostSpec;     // non-null → placement mode

    public void Init(SimRunner runner, SelectionController sel, ViewSync view)
    {
        _runner = runner; _sel = sel; _view = view;
    }

    public void ArmBuildGhost(BuildingSpec spec) { _ghostSpec = spec; QueueRedraw(); }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.A } when _ghostSpec is null && _sel.SelectedUnits.Count > 0:
                _attackMoveArmed = true;
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                _attackMoveArmed = false;
                _ghostSpec = null;
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } when _attackMoveArmed:
                IssueAttackMove(GetGlobalMousePosition());
                _attackMoveArmed = false;
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } when _ghostSpec is not null:
                TryPlaceGhost(GetGlobalMousePosition());
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                if (_ghostSpec is not null) { _ghostSpec = null; QueueRedraw(); }
                else ContextOrder(GetGlobalMousePosition());
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private int[] SelectedIds() => _sel.SelectedUnits.ToArray();

    /// <summary>World pixel → sim FixVec (sub-cell precision via FromFraction).</summary>
    private static FixVec ToSim(Vector2 px) => new(
        Fix.FromFraction((int)(px.X * 16), 16 * RenderMath.CellPx),
        Fix.FromFraction((int)(px.Y * 16), 16 * RenderMath.CellPx));

    private void ContextOrder(Vector2 worldPx)
    {
        if (_sel.SelectedUnits.Count == 0) return;
        var w = _runner.World;
        var p = _sel.ControlledPlayer;
        var ids = SelectedIds();

        // Priority 1: click near an enemy unit → attack it
        var enemy = _view.Units.Values.FirstOrDefault(v =>
            v.OwnerId != p && v.Position.DistanceTo(worldPx) < 24);
        if (enemy is not null) { _runner.Enqueue(new AttackCommand(p, ids, enemy.UnitId)); return; }

        var (cx, cy) = RenderMath.PxToCell(worldPx);

        // Priority 2: click on an enemy building → attack it
        var eb = w.Buildings.FirstOrDefault(b => b.OwnerId != p &&
            cx >= b.CellX && cx < b.CellX + b.Spec.Width && cy >= b.CellY && cy < b.CellY + b.Spec.Height);
        if (eb is not null) { _runner.Enqueue(new AttackCommand(p, ids, eb.Id)); return; }

        // Priority 3: click on a resource node → harvesters harvest, others move
        var node = w.Nodes.FirstOrDefault(n => n.CellX == cx && n.CellY == cy);
        if (node is not null)
        {
            var harvesters = ids.Where(id => w.GetUnit(id)?.Harvester is not null).ToArray();
            var rest = ids.Except(harvesters).ToArray();
            if (harvesters.Length > 0) _runner.Enqueue(new HarvestCommand(p, harvesters, node.Id));
            if (rest.Length > 0) _runner.Enqueue(new MoveCommand(p, rest, ToSim(worldPx)));
            return;
        }

        // Default: move all selected units to the click position
        _runner.Enqueue(new MoveCommand(p, ids, ToSim(worldPx)));
    }

    private void IssueAttackMove(Vector2 worldPx) =>
        _runner.Enqueue(new AttackMoveCommand(_sel.ControlledPlayer, SelectedIds(), ToSim(worldPx)));

    private void TryPlaceGhost(Vector2 worldPx)
    {
        var spec = _ghostSpec!;
        var (cx, cy) = RenderMath.PxToCell(worldPx);
        var w = _runner.World;
        var worker = SelectedIds().Select(w.GetUnit).FirstOrDefault(u => u?.Harvester is not null);
        if (worker is null) { _ghostSpec = null; QueueRedraw(); return; }
        _runner.Enqueue(new BuildCommand(_sel.ControlledPlayer, worker.Id, spec, cx, cy));
        _ghostSpec = null;
        QueueRedraw();
    }

    public override void _Process(double delta) { if (_ghostSpec is not null) QueueRedraw(); }

    public override void _Draw()
    {
        if (_ghostSpec is null) return;
        var (cx, cy) = RenderMath.PxToCell(GetGlobalMousePosition());
        bool ok = FootprintFree(cx, cy, _ghostSpec.Width, _ghostSpec.Height);
        var rect = new Rect2(RenderMath.CellToPx(cx, cy),
            new Vector2(_ghostSpec.Width * RenderMath.CellPx, _ghostSpec.Height * RenderMath.CellPx));
        DrawRect(rect, (ok ? Colors.Lime : Colors.Red) with { A = 0.35f });
        DrawRect(rect, ok ? Colors.Lime : Colors.Red, filled: false, width: 2);
    }

    /// <summary>View-side placement preview only — the sim's FootprintPlaceable
    /// remains authoritative at command time.</summary>
    private bool FootprintFree(int cx, int cy, int wCells, int hCells)
    {
        var map = _runner.World.Map;
        for (int y = cy; y < cy + hCells; y++)
            for (int x = cx; x < cx + wCells; x++)
                if (x < 0 || y < 0 || x >= map.Width || y >= map.Height || !map.IsPassable(x, y))
                    return false;
        return true;
    }
}
