using Godot;
using System.Collections.Generic;

namespace LlmRts.Godot;

/// <summary>Standalone in-engine preview of the UniRig-baked 8-facing sheets (run with F6 on
/// EightFacingDemo.tscn). Shows the crawler + aegis driving AnimatedSprite2D: auto-cycles through
/// all 8 facings, SPACE plays the attack at the current facing. Render-only; touches no sim.</summary>
public partial class EightFacingDemo : Node2D
{
    private sealed class Row
    {
        public AnimatedSprite2D Sprite = null!;
        public Label Label = null!;
        public string Name = "";
    }

    private readonly List<Row> _rows = new();
    private int _facing;
    private double _facTimer;

    public override void _Ready()
    {
        AddUnit("crawler", new Vector2(260, 340));
        AddUnit("aegis", new Vector2(640, 340));
        AddChild(new Label
        {
            Text = "UniRig 8-facing bake  —  SPACE: attack   (facing auto-cycles 0..7 each second)",
            Position = new Vector2(20, 20),
        });
    }

    private void AddUnit(string key, Vector2 pos)
    {
        var frames = EightFacingSheet.Load(key);
        if (frames is null)
        {
            GD.PushWarning($"EightFacingDemo: no 8-facing sheet for '{key}' (assets/units/anim/{key}8.png)");
            return;
        }
        var s = new AnimatedSprite2D
        {
            SpriteFrames = frames,
            Centered = true,
            Scale = new Vector2(4, 4),
            Position = pos,
            TextureFilter = TextureFilterEnum.Nearest,
        };
        AddChild(s);
        // After an attack finishes, drop back to the walk loop at the current facing.
        s.AnimationFinished += () =>
        {
            if (((string)s.Animation).StartsWith("attack"))
                s.Play($"walk-{_facing}");
        };
        s.Play("walk-0");
        var lbl = new Label { Position = pos + new Vector2(-70, 150) };
        AddChild(lbl);
        _rows.Add(new Row { Sprite = s, Label = lbl, Name = key });
    }

    public override void _Process(double delta)
    {
        _facTimer += delta;
        if (_facTimer >= 1.0)
        {
            _facTimer = 0;
            _facing = (_facing + 1) % 8;
            foreach (var r in _rows)
                if (!((string)r.Sprite.Animation).StartsWith("attack"))
                    r.Sprite.Play($"walk-{_facing}");
        }
        foreach (var r in _rows)
            r.Label.Text = $"{r.Name}   {r.Sprite.Animation}";
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Space })
            foreach (var r in _rows)
                r.Sprite.Play($"attack-{_facing}");
    }
}
