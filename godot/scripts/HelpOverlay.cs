using Godot;

namespace LlmRts.Godot;

/// <summary>F1/H toggles a controls cheat-sheet over the match, with a small always-on hint so the
/// hotkeys are discoverable. Pure presentation — reads no sim state and issues no commands.</summary>
public partial class HelpOverlay : CanvasLayer
{
    private Control _root = null!;
    private Label _hint = null!;

    private const string ControlsText =
        "CONTROLS\n\n" +
        "Left-drag / click — select units      Shift+click — add to selection\n" +
        "Right-click — move · attack · harvest · set rally point\n" +
        "A + click — attack-move        P + click — patrol\n" +
        "1–9 — recall control group     Ctrl+1–9 — bind control group\n" +
        "Delete — self-destruct selection      Esc — cancel order / placement\n" +
        "WASD / arrows / screen edge — pan camera      mouse wheel — zoom\n" +
        "Build · Train · Research · Stance — buttons appear along the bottom\n" +
        "      when a worker, a production building, or armed units are selected\n\n" +
        "F1 / H — toggle this help        F3 — toggle fog (debug)\n" +
        "Tab — switch sides (debug · single-player only)\n\n" +
        "— press F1 or H to close —";

    public override void _Ready()
    {
        Layer = 50;   // above the HUD CanvasLayer (default layer 1)

        _root = new Control { Visible = false };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.74f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(center);

        var panel = new PanelContainer();
        center.AddChild(panel);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 28);
        panel.AddChild(margin);
        margin.AddChild(new Label { Text = ControlsText });

        // Always-on discoverability hint, top-centre (badge is top-left, resources top-right).
        _hint = new Label
        {
            Text = "[F1] Controls",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0.55f),
        };
        _hint.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _hint.GrowHorizontal = Control.GrowDirection.Both;
        _hint.OffsetTop = 8;
        AddChild(_hint);
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F1 or Key.H })
        {
            _root.Visible = !_root.Visible;
            _hint.Visible = !_root.Visible;   // hide the hint while the full panel is open
            GetViewport().SetInputAsHandled();
        }
    }
}
