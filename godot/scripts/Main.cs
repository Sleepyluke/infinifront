using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

public partial class Main : Node2D
{
    public SimRunner Runner { get; private set; } = null!;
    public ViewSync View { get; private set; } = null!;
    public SelectionController Selection { get; private set; } = null!;
    public CommandController Commands { get; private set; } = null!;
    private FogView _fogView = null!;
    private Minimap _minimap = null!;

    public override void _Ready()
    {
        Runner = new SimRunner { Name = "SimRunner" };
        Runner.Init(MatchConfig.Configured
            ? MatchSetup.BuildStandard1v1(MatchConfig.Human, MatchConfig.Cpu, MatchConfig.Difficulty, seed: 42)
            : TestMap.Build());
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

        // Wire controlled-player provider into ViewSync now that Selection exists.
        viewSync.ControlledPlayerProvider = () => Selection.ControlledPlayer;
        // Also refresh entity visibility on player switch.
        Selection.PlayerSwitched += viewSync.ForceSync;

        // Order matters: Commands added AFTER Selection so it appears later in the tree.
        // Godot processes _UnhandledInput in REVERSE tree order (later siblings first),
        // so CommandController sees input before SelectionController. It consumes
        // left-clicks ONLY when armed (attack-move or ghost active); otherwise the event
        // falls through to Selection for normal drag/click selection.
        Commands = new CommandController { Name = "Commands" };
        AddChild(Commands);
        Commands.Init(Runner, Selection, viewSync);

        // FogView sits above the world but below the HUD (which is a CanvasLayer).
        _fogView = new FogView { Name = "FogView" };
        AddChild(_fogView);
        _fogView.Init(Runner, Selection);

        var hud = new Hud { Name = "Hud" };
        AddChild(hud);
        hud.Init(Runner, Selection, Commands);

        _minimap = new Minimap { Name = "Minimap" };
        hud.AddChild(_minimap);
        _minimap.Init(Runner, camera, Selection);

        GD.Print($"LlmRts boot OK units={Runner.World.Units.Count} buildings={Runner.World.Buildings.Count}");

        if (!MatchConfig.Configured)
        {
            Runner.Paused = true;                 // hold the sim behind the menu
            AddChild(new MenuScreen { Name = "Menu" });
        }
        else
        {
            var gameOver = new GameOverScreen { Name = "GameOver" };
            AddChild(gameOver);
            gameOver.Init(Runner);
        }
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 })
        {
            Runner.World.FogEnabled = !Runner.World.FogEnabled;
            // Force immediate visual refresh of entity visibility;
            // when paused, ViewSync.ForceSync re-applies Visible flags.
            View.ForceSync();
            // FogView._Process detects the flag change and queues its own redraw,
            // but QueueRedraw here keeps minimap in sync too.
            _minimap.QueueRedraw();
            GetViewport().SetInputAsHandled();
        }
    }
}
