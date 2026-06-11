using Godot;

namespace LlmRts.Godot;

public partial class CameraRig : Camera2D
{
    private const float PanSpeed = 900f;
    private const int EdgePx = 12;
    private static readonly float[] ZoomSteps = { 0.5f, 1f, 2f };
    private int _zoomIdx = 1;

    public override void _Ready()
    {
        MakeCurrent();
        Position = new Vector2(8 * RenderMath.CellPx, 8 * RenderMath.CellPx); // player 0 base
        ApplyZoom();
    }

    public override void _Process(double delta)
    {
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;

        var vp = GetViewport().GetVisibleRect().Size;
        var mouse = GetViewport().GetMousePosition();
        if (mouse.X <= EdgePx) dir.X -= 1;
        if (mouse.X >= vp.X - EdgePx) dir.X += 1;
        if (mouse.Y <= EdgePx) dir.Y -= 1;
        if (mouse.Y >= vp.Y - EdgePx) dir.Y += 1;

        if (dir != Vector2.Zero)
            Position += dir.Normalized() * PanSpeed * (float)delta / Zoom.X;

        var limit = TestMap.Size * RenderMath.CellPx;
        Position = new Vector2(Mathf.Clamp(Position.X, 0, limit), Mathf.Clamp(Position.Y, 0, limit));
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp && _zoomIdx < ZoomSteps.Length - 1) { _zoomIdx++; ApplyZoom(); }
            if (mb.ButtonIndex == MouseButton.WheelDown && _zoomIdx > 0) { _zoomIdx--; ApplyZoom(); }
        }
    }

    private void ApplyZoom() => Zoom = Vector2.One * ZoomSteps[_zoomIdx];
}
