using Godot;

namespace LlmRts.Godot;

/// <summary>Bottom-left minimap: 4px per cell (160x160 for the 40x40 map).
/// Redrawn once per sim tick (not per frame). Left-click/drag jumps the camera.</summary>
public partial class Minimap : Control
{
    private const int PxPerCell = 4;
    private SimRunner _runner = null!;
    private CameraRig _camera = null!;
    private SelectionController _sel = null!;
    private bool _dragging;
    // Smooth camera rect: redraw only when camera moves or zooms
    private Vector2 _lastCamPos;
    private Vector2 _lastCamZoom;

    public void Init(SimRunner runner, CameraRig camera, SelectionController sel)
    {
        _runner = runner;
        _camera = camera;
        _sel = sel;
        var side = TestMap.Size * PxPerCell;
        CustomMinimumSize = new Vector2(side, side);
        // bottom-left anchor, sitting above the bottom toolbar
        AnchorTop = 1; AnchorBottom = 1;
        OffsetLeft = 8; OffsetTop = -side - 96; OffsetBottom = -96; OffsetRight = side + 8;
        runner.Ticked += QueueRedraw;
        MouseFilter = MouseFilterEnum.Stop; // swallow clicks so they don't reach world selection
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _dragging = mb.Pressed;
                if (mb.Pressed) JumpCamera(mb.Position);
                AcceptEvent();
                break;
            case InputEventMouseMotion mm when _dragging:
                JumpCamera(mm.Position);
                AcceptEvent();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_camera.Position != _lastCamPos || _camera.Zoom != _lastCamZoom)
        {
            _lastCamPos = _camera.Position;
            _lastCamZoom = _camera.Zoom;
            QueueRedraw();
        }
    }

    private void JumpCamera(Vector2 local) =>
        _camera.Position = local / PxPerCell * RenderMath.CellPx;

    public override void _Draw()
    {
        var w = _runner.World;
        var side = TestMap.Size * PxPerCell;
        int controlled = _sel.ControlledPlayer;

        DrawRect(new Rect2(0, 0, side, side), new Color(0.30f, 0.27f, 0.23f));
        for (int y = 0; y < w.Map.Height; y++)
            for (int x = 0; x < w.Map.Width; x++)
                if (!w.Map.IsPassable(x, y))
                    DrawRect(new Rect2(x * PxPerCell, y * PxPerCell, PxPerCell, PxPerCell), new Color(0.15f, 0.14f, 0.12f));

        foreach (var n in w.Nodes)
            DrawRect(new Rect2(n.CellX * PxPerCell, n.CellY * PxPerCell, PxPerCell, PxPerCell), new Color(0.4f, 0.8f, 1f));

        // Buildings: own always shown; enemy only when explored.
        foreach (var b in w.Buildings)
        {
            if (b.OwnerId != controlled && !w.IsExploredBy(controlled, b.CellX, b.CellY))
                continue;
            DrawRect(new Rect2(b.CellX * PxPerCell, b.CellY * PxPerCell, b.Spec.Width * PxPerCell, b.Spec.Height * PxPerCell),
                UnitView.PlayerColors[b.OwnerId]);
        }

        // Units: own always shown; enemy only when currently visible.
        foreach (var u in w.Units)
        {
            var (ucx, ucy) = w.Map.WorldToCell(u.Position);
            if (u.OwnerId != controlled && !w.IsVisibleTo(controlled, ucx, ucy))
                continue;
            var p = RenderMath.ToPx(u.Position) / RenderMath.CellPx * PxPerCell;
            DrawRect(new Rect2(p.X - 1, p.Y - 1, 3, 3), UnitView.PlayerColors[u.OwnerId]);
        }

        // Fog overlay: unexplored = near-black, explored-but-not-visible = dim.
        if (w.FogEnabled)
        {
            var fogBlack = new Color(0, 0, 0, 0.90f);
            var fogDim   = new Color(0, 0, 0, 0.45f);
            for (int y = 0; y < w.Map.Height; y++)
                for (int x = 0; x < w.Map.Width; x++)
                {
                    if (w.IsVisibleTo(controlled, x, y)) continue;
                    DrawRect(new Rect2(x * PxPerCell, y * PxPerCell, PxPerCell, PxPerCell),
                        w.IsExploredBy(controlled, x, y) ? fogDim : fogBlack);
                }
        }

        // camera viewport rectangle
        var vp = _camera.GetViewportRect().Size / _camera.Zoom;
        var topLeft = (_camera.Position - vp / 2) / RenderMath.CellPx * PxPerCell;
        DrawRect(new Rect2(topLeft, vp / RenderMath.CellPx * PxPerCell), Colors.White, filled: false, width: 1);

        DrawRect(new Rect2(0, 0, side, side), Colors.Black, filled: false, width: 2);
    }
}
