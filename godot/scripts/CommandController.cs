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
    private bool _patrolArmed;
    private BuildingDef? _ghostDef;     // non-null → placement mode

    public void Init(SimRunner runner, SelectionController sel, ViewSync view)
    {
        _runner = runner; _sel = sel; _view = view;
    }

    public void ArmBuildGhost(BuildingDef def) { _attackMoveArmed = false; _patrolArmed = false; _ghostDef = def; QueueRedraw(); }

    public override void _UnhandledInput(InputEvent e)
    {
        if (HelpOverlay.IsOpen) return;   // controls cheat-sheet is up → ignore gameplay input
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.A } when _ghostDef is null && _sel.SelectedUnits.Count > 0:
                _attackMoveArmed = true;
                _patrolArmed = false;
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.P } when _ghostDef is null && _sel.SelectedUnits.Count > 0:
                _patrolArmed = true;
                _attackMoveArmed = false;
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                _attackMoveArmed = false;
                _patrolArmed = false;
                _ghostDef = null;
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } when _attackMoveArmed:
                IssueAttackMove(GetGlobalMousePosition());
                _attackMoveArmed = false;
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } when _patrolArmed:
                IssuePatrol(GetGlobalMousePosition());
                _patrolArmed = false;
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } when _ghostDef is not null:
                TryPlaceGhost(GetGlobalMousePosition());
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                if (_ghostDef is not null) { _ghostDef = null; QueueRedraw(); }
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
        // Rally: right-click with NO units but an owned production building selected.
        if (_sel.SelectedUnits.Count == 0)
        {
            if (_sel.SelectedBuilding != 0)
            {
                var w2 = _runner.World;
                var p2 = _sel.ControlledPlayer;
                var bldg = w2.GetBuilding(_sel.SelectedBuilding);
                if (bldg is not null && bldg.OwnerId == p2)
                {
                    var (cx2, cy2) = RenderMath.PxToCell(worldPx);
                    bool insideFootprint = cx2 >= bldg.CellX && cx2 < bldg.CellX + bldg.Spec.Width
                                       && cy2 >= bldg.CellY && cy2 < bldg.CellY + bldg.Spec.Height;
                    if (insideFootprint)
                        _runner.Enqueue(new SetRallyCommand(p2, bldg.Id, default, Clear: true));
                    else
                        _runner.Enqueue(new SetRallyCommand(p2, bldg.Id, ToSim(worldPx)));
                }
            }
            return;
        }
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

    private void IssuePatrol(Vector2 worldPx) =>
        _runner.Enqueue(new PatrolCommand(_sel.ControlledPlayer, SelectedIds(), ToSim(worldPx)));

    private void TryPlaceGhost(Vector2 worldPx)
    {
        var def = _ghostDef!;
        var (cx, cy) = RenderMath.PxToCell(worldPx);
        var w = _runner.World;
        var worker = SelectedIds().Select(w.GetUnit).FirstOrDefault(u => u?.Harvester is not null);
        if (worker is null) { _ghostDef = null; QueueRedraw(); return; }
        _runner.Enqueue(new BuildCommand(_sel.ControlledPlayer, worker.Id, def.Id, cx, cy));
        _ghostDef = null;
        QueueRedraw();
    }

    public override void _Process(double delta) { if (_ghostDef is not null) QueueRedraw(); }

    public override void _Draw()
    {
        if (_ghostDef is null) return;
        var (cx, cy) = RenderMath.PxToCell(GetGlobalMousePosition());
        bool ok = FootprintFree(cx, cy, _ghostDef.Spec.Width, _ghostDef.Spec.Height);
        var rect = new Rect2(RenderMath.CellToPx(cx, cy),
            new Vector2(_ghostDef.Spec.Width * RenderMath.CellPx, _ghostDef.Spec.Height * RenderMath.CellPx));
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
