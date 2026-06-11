using Godot;

namespace LlmRts.Godot;

public partial class Main : Node2D
{
    public SimRunner Runner { get; private set; } = null!;
    public ViewSync View { get; private set; } = null!;
    public SelectionController Selection { get; private set; } = null!;

    public override void _Ready()
    {
        Runner = new SimRunner { Name = "SimRunner" };
        Runner.Init(TestMap.Build());
        AddChild(Runner);

        var mapView = new MapView { Name = "MapView" };
        mapView.Init(Runner.World.Map);
        AddChild(mapView);

        var viewSync = new ViewSync { Name = "ViewSync" };
        AddChild(viewSync);
        viewSync.Init(Runner);
        View = viewSync;

        AddChild(new CameraRig { Name = "Camera" });

        Selection = new SelectionController { Name = "Selection" };
        AddChild(Selection);
        Selection.Init(viewSync, Runner);
        Runner.Ticked += Selection.PruneDead;

        GD.Print($"LlmRts boot OK units={Runner.World.Units.Count} buildings={Runner.World.Buildings.Count}");
    }
}
