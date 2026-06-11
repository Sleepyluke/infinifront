using Godot;

namespace LlmRts.Godot;

/// <summary>Bottom-left minimap: 4px per cell (160x160 for the 40x40 map).
/// Redrawn once per sim tick (not per frame). Left-click/drag jumps the camera.</summary>
public partial class Minimap : Control
{
    private new const int Scale = 4; // px per cell (hides Control.Scale intentionally)
    private SimRunner _runner = null!;
    private CameraRig _camera = null!;
    private bool _dragging;

    public void Init(SimRunner runner, CameraRig camera)
    {
        _runner = runner;
        _camera = camera;
        var side = TestMap.Size * Scale;
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

    private void JumpCamera(Vector2 local) =>
        _camera.Position = local / Scale * RenderMath.CellPx;

    public override void _Draw()
    {
        var w = _runner.World;
        var side = TestMap.Size * Scale;

        DrawRect(new Rect2(0, 0, side, side), new Color(0.30f, 0.27f, 0.23f));
        for (int y = 0; y < w.Map.Height; y++)
            for (int x = 0; x < w.Map.Width; x++)
                if (!w.Map.IsPassable(x, y))
                    DrawRect(new Rect2(x * Scale, y * Scale, Scale, Scale), new Color(0.15f, 0.14f, 0.12f));

        foreach (var n in w.Nodes)
            DrawRect(new Rect2(n.CellX * Scale, n.CellY * Scale, Scale, Scale), new Color(0.4f, 0.8f, 1f));
        foreach (var b in w.Buildings)
            DrawRect(new Rect2(b.CellX * Scale, b.CellY * Scale, b.Spec.Width * Scale, b.Spec.Height * Scale),
                UnitView.PlayerColors[b.OwnerId]);
        foreach (var u in w.Units)
        {
            var p = RenderMath.ToPx(u.Position) / RenderMath.CellPx * Scale;
            DrawRect(new Rect2(p.X - 1, p.Y - 1, 3, 3), UnitView.PlayerColors[u.OwnerId]);
        }

        // camera viewport rectangle
        var vp = _camera.GetViewportRect().Size / _camera.Zoom;
        var topLeft = (_camera.Position - vp / 2) / RenderMath.CellPx * Scale;
        DrawRect(new Rect2(topLeft, vp / RenderMath.CellPx * Scale), Colors.White, filled: false, width: 1);

        DrawRect(new Rect2(0, 0, side, side), Colors.Black, filled: false, width: 2);
    }
}
