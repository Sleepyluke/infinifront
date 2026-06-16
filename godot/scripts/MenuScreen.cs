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
        _humanPick = MakeFactionPicker(i => _human = i);
        box.AddChild(_humanPick);

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
