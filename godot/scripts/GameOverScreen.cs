using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

/// <summary>Watches the match outcome; when Over, pauses the sim and shows Victory/Defeat/Draw
/// with Restart (same config) and Menu buttons.</summary>
public partial class GameOverScreen : CanvasLayer
{
    private SimRunner _runner = null!;
    private PanelContainer _panel = null!;
    private Label _result = null!;
    private bool _shown;

    public void Init(SimRunner runner)
    {
        _runner = runner;
        _runner.Ticked += Check;
    }

    public override void _Ready()
    {
        Layer = 90;
        _panel = new PanelContainer { Visible = false };
        var box = new VBoxContainer();
        _panel.AddChild(box);
        AddChild(_panel);
        _panel.SetAnchorsPreset(Control.LayoutPreset.Center);

        _result = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        box.AddChild(_result);

        var restart = new Button { Text = "Restart" };
        restart.Pressed += () => GetTree().ReloadCurrentScene(); // MatchConfig still set
        box.AddChild(restart);

        var menu = new Button { Text = "Menu" };
        menu.Pressed += () => { MatchConfig.Clear(); GetTree().ReloadCurrentScene(); };
        box.AddChild(menu);
    }

    private void Check()
    {
        if (_shown || _runner.World.Phase != MatchPhase.Over) return;
        _shown = true;
        _runner.Paused = true;
        int winner = _runner.World.WinnerId;
        _result.Text = winner == 0 ? "Victory!" : winner == 1 ? "Defeat" : "Draw";
        _panel.Visible = true;
    }
}
