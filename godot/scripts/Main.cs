using Godot;
using SimCore.Net;
using SimCore.Packs;
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
    private CameraRig _camera = null!;

    public override void _Ready()
    {
        Runner = new SimRunner { Name = "SimRunner" };
        Runner.Init(MatchConfig.IsNetworked
            ? MatchSetup.BuildVersus1v1(ReferenceFaction.Def, ReferenceFaction.Def, NetSession.MatchSeed)
            : MatchConfig.Configured
                ? MatchSetup.BuildStandard1v1(MatchConfig.Human, MatchConfig.Cpu, MatchConfig.Difficulty, seed: 42)
                : TestMap.Build());
        AddChild(Runner);

        var mapView = new MapView { Name = "MapView" };
        AddChild(mapView);
        mapView.Init(Runner);   // reads the world live so it can skip building/node cells (and survive world swaps)

        var viewSync = new ViewSync { Name = "ViewSync" };
        AddChild(viewSync);
        viewSync.Init(Runner);
        View = viewSync;

        var camera = new CameraRig { Name = "Camera" };
        AddChild(camera);
        _camera = camera;

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

        if (MatchConfig.IsNetworked)
        {
            StartNetworked();
        }
        else if (!MatchConfig.Configured)
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

    private void StartNetworked()
    {
        Runner.Paused = true;
        var factions = PackCatalog.Load(PacksDir());

        var net = new NetSession { Name = "Net" };
        AddChild(net);

        var lobby = new LobbyScreen { Name = "Lobby" };
        lobby.Init(net, MatchConfig.IsHost, factions);
        AddChild(lobby);

        net.MatchStarting += (slots, seed) =>
        {
            GD.Print($"NET: MatchStarting fired ({slots.Count} slots, seed {seed})");
            lobby.QueueFree();
            BuildNetworkedMatch(net, slots, seed, factions);
        };
        net.PeerDropped += () => { Runner.Paused = true; GD.Print("Peer dropped"); };

        if (MatchConfig.IsHost) net.Host(); else net.Join(MatchConfig.Ip);
    }

    private void BuildNetworkedMatch(NetSession net, System.Collections.Generic.IReadOnlyList<LobbySlot> slots, ulong seed, System.Collections.Generic.IReadOnlyList<FactionEntry> factions)
    {
        // Resolve each slot's faction id -> FactionDef (fallback to reference) and map to MatchSlot.
        var matchSlots = new System.Collections.Generic.List<MatchSlot>(slots.Count);
        var humanIds = new System.Collections.Generic.List<int>();
        long myPeer = net.Multiplayer.GetUniqueId();
        int localSlot = -1;
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            var faction = ResolveFaction(factions, s.FactionId);
            var controller = s.Kind == SlotKind.Cpu ? PlayerController.Cpu : PlayerController.Human;
            matchSlots.Add(new MatchSlot(faction, controller, s.Difficulty, s.Team));
            if (s.Kind == SlotKind.Human) humanIds.Add(i);
            if (s.Kind == SlotKind.Human && s.OccupantPeerId == myPeer) localSlot = i;
        }

        string slotDump = "";
        for (int i = 0; i < slots.Count; i++) slotDump += $" {i}:{slots[i].Kind}/occ{slots[i].OccupantPeerId}/t{slots[i].Team}";
        GD.Print($"NET START: myPeer={myPeer} localSlot={localSlot} humanIds=[{string.Join(",", humanIds)}] slots=[{slotDump} ]");

        // No Human slot is occupied by THIS peer (e.g. the host re-flipped/removed our seat before
        // Start, or our claim hadn't landed). Entering with a fallback slot would silently drive
        // another player's units with NO desync to flag it — so halt loudly instead of guessing.
        if (localSlot < 0)
        {
            GD.PrintErr($"Lobby start aborted: peer {myPeer} owns no Human slot in the start config.");
            Runner.Paused = true;
            return;
        }

        var world = MatchSetup.BuildMatch(matchSlots, seed);
        Runner.Init(world);
        Selection.ControlledPlayer = localSlot;

        // Frame the local player's base — the camera otherwise sits at player 0's corner (looks black
        // through fog for any other player until you pan there).
        foreach (var b in world.Buildings)
            if (b.OwnerId == localSlot)
            {
                _camera.CenterOn(new Vector2((b.CellX + b.Spec.Width / 2f) * RenderMath.CellPx,
                                             (b.CellY + b.Spec.Height / 2f) * RenderMath.CellPx));
                break;
            }

        // Bring the match LIVE before touching the views, so a render glitch can never abort a
        // started match (a 4-player render crash silently aborting this is exactly what bit us).
        var coord = new SimCore.Net.LockstepCoordinator(localSlot, humanIds, NetSession.InputDelay);
        Runner.InitNetworked(world, coord, net, localSlot);
        Runner.Paused = false;
        try { View.ForceSync(); } catch (System.Exception ex) { GD.PrintErr($"ForceSync after start (non-fatal): {ex.Message}"); }

        var gameOver = new GameOverScreen { Name = "GameOver" };
        AddChild(gameOver);
        gameOver.Init(Runner);
    }

    private static FactionDef ResolveFaction(System.Collections.Generic.IReadOnlyList<FactionEntry> factions, string id)
    {
        foreach (var f in factions) if (f.Faction.Id == id) return f.Faction;
        return ReferenceFaction.Def;
    }

    // Walk up from the binary output dir to find the repo's packs/ (dev) or packs/ beside the
    // exe (shipped); fall back to BaseDirectory/packs so PackCatalog.Load just yields Reference.
    private static string PacksDir() =>
        PackCatalog.ResolvePacksDir(System.AppContext.BaseDirectory)
        ?? System.IO.Path.Combine(System.AppContext.BaseDirectory, "packs");

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
