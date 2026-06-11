using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LlmRts.Godot;

/// <summary>Owns "who is selected" and "which player am I". Draws the drag box.</summary>
public partial class SelectionController : Node2D
{
    public int ControlledPlayer { get; private set; }
    public readonly HashSet<int> SelectedUnits = new();
    public int SelectedBuilding { get; private set; } // 0 = none

    private ViewSync _view = null!;
    private SimRunner _runner = null!;
    private bool _dragging;
    private Vector2 _dragStart;

    public event System.Action? SelectionChanged;
    public event System.Action? PlayerSwitched;

    public void Init(ViewSync view, SimRunner runner) { _view = view; _runner = runner; }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Tab }:
                ControlledPlayer = 1 - ControlledPlayer;
                Clear();
                PlayerSwitched?.Invoke();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                _dragging = true;
                _dragStart = GetGlobalMousePosition();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                if (!_dragging) break;
                _dragging = false;
                var end = GetGlobalMousePosition();
                bool additive = Input.IsKeyPressed(Key.Shift);
                if (_dragStart.DistanceTo(end) < 8) ClickSelect(end, additive);
                else BoxSelect(new Rect2(_dragStart, Vector2.Zero).Expand(end), additive);
                QueueRedraw();
                GetViewport().SetInputAsHandled();
                break;
        }
        if (_dragging) QueueRedraw();
    }

    private void ClickSelect(Vector2 worldPx, bool additive)
    {
        if (!additive) Clear();
        var hit = _view.Units.Values
            .Where(v => v.OwnerId == ControlledPlayer && v.Position.DistanceTo(worldPx) < 24)
            .OrderBy(v => v.Position.DistanceTo(worldPx))
            .FirstOrDefault();
        if (hit is not null)
        {
            SelectedUnits.Add(hit.UnitId);
            SelectedBuilding = 0; // unit and building selection are mutually exclusive
        }
        else
        {
            var (cx, cy) = RenderMath.PxToCell(worldPx);
            var b = _runner.World.Buildings.FirstOrDefault(b =>
                b.OwnerId == ControlledPlayer &&
                cx >= b.CellX && cx < b.CellX + b.Spec.Width &&
                cy >= b.CellY && cy < b.CellY + b.Spec.Height);
            if (b is not null) SelectedBuilding = b.Id;
        }
        ApplyHighlights();
    }

    private void BoxSelect(Rect2 box, bool additive)
    {
        if (!additive) Clear();
        foreach (var v in _view.Units.Values)
            if (v.OwnerId == ControlledPlayer && box.HasPoint(v.Position))
                SelectedUnits.Add(v.UnitId);
        ApplyHighlights();
    }

    private void Clear()
    {
        SelectedUnits.Clear();
        SelectedBuilding = 0;
        ApplyHighlights();
    }

    private void ApplyHighlights()
    {
        foreach (var v in _view.Units.Values) v.Selected = SelectedUnits.Contains(v.UnitId);
        SelectionChanged?.Invoke();
    }

    /// <summary>Drop ids of entities that died; called each tick.</summary>
    public void PruneDead()
    {
        int before = SelectedUnits.Count;
        SelectedUnits.RemoveWhere(id => !_view.Units.ContainsKey(id));
        bool buildingDied = SelectedBuilding != 0 && _runner.World.GetBuilding(SelectedBuilding) is null;
        if (buildingDied) SelectedBuilding = 0;
        if (before != SelectedUnits.Count || buildingDied) SelectionChanged?.Invoke();
    }

    public override void _Draw()
    {
        if (_dragging)
        {
            var box = new Rect2(_dragStart, Vector2.Zero).Expand(GetGlobalMousePosition());
            DrawRect(box, Colors.Lime with { A = 0.15f });
            DrawRect(box, Colors.Lime, filled: false, width: 1);
        }
    }

    public override void _Process(double delta) { if (_dragging) QueueRedraw(); }
}
