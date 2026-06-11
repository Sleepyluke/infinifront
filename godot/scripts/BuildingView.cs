using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

public partial class BuildingView : Node2D
{
    private Building _b = null!;
    public int BuildingId => _b.Id;
    public void Init(Building b) { _b = b; Position = RenderMath.CellToPx(b.CellX, b.CellY); }
    public void SyncTick(Building b) { _b = b; QueueRedraw(); }
    public void PlayDestructionAndFree() => QueueFree();
    public override void _Draw() =>
        DrawRect(new Rect2(0, 0, _b.Spec.Width * RenderMath.CellPx, _b.Spec.Height * RenderMath.CellPx),
            UnitView.PlayerColors[_b.OwnerId] with { A = 0.5f });
}
