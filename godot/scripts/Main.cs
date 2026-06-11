using Godot;

namespace LlmRts.Godot;

public partial class Main : Node2D
{
    public SimRunner Runner { get; private set; } = null!;
    public ViewSync View { get; private set; } = null!;
    public SelectionController Selection { get; private set; } = null!;
    public CommandController Commands { get; private set; } = null!;

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

        var camera = new CameraRig { Name = "Camera" };
        AddChild(camera);

        Selection = new SelectionController { Name = "Selection" };
        AddChild(Selection);
        Selection.Init(viewSync, Runner);
        Runner.Ticked += Selection.PruneDead;

        // Order matters: Commands added AFTER Selection so it appears later in the tree.
        // Godot processes _UnhandledInput in REVERSE tree order (later siblings first),
        // so CommandController sees input before SelectionController. It consumes
        // left-clicks ONLY when armed (attack-move or ghost active); otherwise the event
        // falls through to Selection for normal drag/click selection.
        Commands = new CommandController { Name = "Commands" };
        AddChild(Commands);
        Commands.Init(Runner, Selection, viewSync);

        var hud = new Hud { Name = "Hud" };
        AddChild(hud);
        hud.Init(Runner, Selection, Commands);

        var minimap = new Minimap { Name = "Minimap" };
        hud.AddChild(minimap);
        minimap.Init(Runner, camera);

        GD.Print($"LlmRts boot OK units={Runner.World.Units.Count} buildings={Runner.World.Buildings.Count}");
    }
}
