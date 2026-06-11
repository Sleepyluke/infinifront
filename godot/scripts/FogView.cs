using Godot;

namespace LlmRts.Godot;

/// <summary>Fog overlay for the controlled player: black = unexplored,
/// half-dark = explored-but-not-visible, clear = visible. Tick-driven.</summary>
public partial class FogView : Node2D
{
    private SimRunner _runner = null!;
    private SelectionController _sel = null!;
    private bool _lastFogEnabled = true;

    public void Init(SimRunner runner, SelectionController sel)
    {
        _runner = runner;
        _sel = sel;
        ZIndex = 50; // above world, below HUD (CanvasLayer)
        runner.Ticked += QueueRedraw;
        sel.PlayerSwitched += QueueRedraw;
    }

    public override void _Process(double delta)
    {
        // Redraw when FogEnabled is toggled (e.g. F3) so the overlay clears immediately.
        if (_runner.World.FogEnabled != _lastFogEnabled)
        {
            _lastFogEnabled = _runner.World.FogEnabled;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var w = _runner.World;
        if (!w.FogEnabled) return;
        int p = _sel.ControlledPlayer;
        const int px = RenderMath.CellPx;
        var black = new Color(0, 0, 0, 0.95f);
        var dim   = new Color(0, 0, 0, 0.45f);
        for (int y = 0; y < w.Map.Height; y++)
            for (int x = 0; x < w.Map.Width; x++)
            {
                if (w.IsVisibleTo(p, x, y)) continue;
                DrawRect(new Rect2(x * px, y * px, px, px),
                    w.IsExploredBy(p, x, y) ? dim : black);
            }
    }
}
