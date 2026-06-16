using Godot;
using SimCore.Packs;
using SimCore.Sim;
using System.Collections.Generic;

namespace LlmRts.Godot;

/// <summary>Start menu: pick your faction, the CPU's faction, and a difficulty, then Play.
/// On Play, stores the choice in MatchConfig and reloads the scene to start the match.</summary>
public partial class MenuScreen : CanvasLayer
{
    private IReadOnlyList<FactionEntry> _factions = System.Array.Empty<FactionEntry>();
    private int _human, _cpu;
    private AiDifficulty _difficulty = AiDifficulty.Easy;
    private OptionButton _humanPick = null!, _cpuPick = null!;
    private Label _diffLabel = null!;
    private Label _humanDesc = null!;

    /// <summary>Identity + playstyle blurbs shown under the faction picker (render-only).
    /// Keyed by FactionDef.Id; unknown packs get a generic line.</summary>
    private static readonly Dictionary<string, string> FactionBlurbs = new()
    {
        ["reference"] = "Vanguard — balanced human military. A dependable all-rounder with no special mechanic: solid infantry, tanks, and turrets. Forgiving to learn and strong in every matchup.",
        ["concord"]   = "The Concord — synthetic energy. Few, expensive, durable units shielded by regenerating energy; every loss stings, so disengage to recharge. Quality over quantity.",
        ["driftborn"] = "The Driftborn — nomad scavengers. Cheap, fast, fragile units and quick-building structures. Hit-and-run raiders that snowball early but fold against static defense.",
        ["mycel"]     = "The Mycel — fungal swarm. The cheapest, most numerous units, and they regenerate health out of combat. Overwhelm with numbers, then pull back wounded units to heal.",
    };

    private static string BlurbFor(string id) =>
        FactionBlurbs.TryGetValue(id, out var b) ? b : "A custom faction.";

    public override void _Ready()
    {
        Layer = 100; // above the world/HUD
        _factions = PackCatalog.Load(PacksDir());

        var panel = new PanelContainer();
        var box = new VBoxContainer();
        panel.AddChild(box);
        AddChild(panel);
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);

        box.AddChild(new Label { Text = "LlmRts — New Match", HorizontalAlignment = HorizontalAlignment.Center });

        box.AddChild(new Label { Text = "Your faction:" });
        _humanPick = MakeFactionPicker(i => { _human = i; UpdateDesc(); });
        box.AddChild(_humanPick);
        _humanDesc = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.Word,
            CustomMinimumSize = new Vector2(320, 0),
            Modulate = new Color(0.82f, 0.86f, 0.92f),
        };
        box.AddChild(_humanDesc);
        UpdateDesc();

        box.AddChild(new Label { Text = "CPU faction:" });
        _cpuPick = MakeFactionPicker(i => _cpu = i);
        box.AddChild(_cpuPick);

        box.AddChild(new Label { Text = "Difficulty:" });
        var diffRow = new HBoxContainer();
        foreach (var d in new[] { AiDifficulty.Easy, AiDifficulty.Medium, AiDifficulty.Hard })
        {
            var captured = d;
            var b = new Button { Text = d.ToString() };
            b.Pressed += () => { _difficulty = captured; _diffLabel.Text = "Difficulty: " + captured; };
            diffRow.AddChild(b);
        }
        box.AddChild(diffRow);
        _diffLabel = new Label { Text = "Difficulty: " + _difficulty };
        box.AddChild(_diffLabel);

        box.AddChild(new HSeparator());
        box.AddChild(new Label { Text = "Multiplayer (LAN — 2-human 1v1, Reference faction):" });

        var host = new Button { Text = "Host LAN game" };
        host.Pressed += () => { MatchConfig.SetNetwork(isHost: true, ip: ""); GetTree().ReloadCurrentScene(); };
        box.AddChild(host);

        var ipEdit = new LineEdit { Text = "127.0.0.1", CustomMinimumSize = new Vector2(160, 0) };
        box.AddChild(ipEdit);

        var join = new Button { Text = "Join" };
        join.Pressed += () => { MatchConfig.SetNetwork(isHost: false, ip: ipEdit.Text); GetTree().ReloadCurrentScene(); };
        box.AddChild(join);

        var play = new Button { Text = "Play" };
        play.Pressed += OnPlay;
        box.AddChild(play);
    }

    private OptionButton MakeFactionPicker(System.Action<int> onSelect)
    {
        var opt = new OptionButton();
        for (int i = 0; i < _factions.Count; i++) opt.AddItem(_factions[i].Name, i);
        opt.Selected = 0;
        opt.ItemSelected += id => onSelect((int)id);
        return opt;
    }

    private void UpdateDesc()
    {
        _humanDesc.Text = _factions.Count == 0 ? "" : BlurbFor(_factions[_human].Faction.Id);
    }

    private void OnPlay()
    {
        if (_factions.Count == 0) return;
        MatchConfig.Set(_factions[_human].Faction, _factions[_cpu].Faction, _difficulty);
        GetTree().ReloadCurrentScene();
    }

    // Walk up from the binary output dir to find the repo's packs/ (dev) or packs/ beside the
    // exe (shipped); fall back to BaseDirectory/packs so PackCatalog.Load just yields Reference.
    private static string PacksDir() =>
        PackCatalog.ResolvePacksDir(System.AppContext.BaseDirectory)
        ?? System.IO.Path.Combine(System.AppContext.BaseDirectory, "packs");
}
