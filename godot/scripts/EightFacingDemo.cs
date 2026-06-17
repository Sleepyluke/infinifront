using Godot;
using System.Collections.Generic;

namespace LlmRts.Godot;

/// <summary>Standalone in-engine preview of the UniRig-baked 8-facing sheets (run with F6 on
/// EightFacingDemo.tscn). Shows the crawler + aegis driving AnimatedSprite2D, auto-cycling all 8
/// facings. Keys: SPACE attack, D death, I idle, W walk. Render-only; touches no sim.</summary>
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
    private string _clip = "walk";   // current looping clip (idle/walk)

    public override void _Ready()
    {
        AddUnit("crawler", new Vector2(260, 340));
        AddUnit("aegis", new Vector2(640, 340));
        AddChild(new Label
        {
            Text = "UniRig 8-facing bake — SPACE attack · D death · I idle · W walk   (facing auto-cycles)",
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
        // A one-shot clip (attack) drops back to the current looping clip; death stays collapsed.
        s.AnimationFinished += () =>
        {
            var a = (string)s.Animation;
            if (a.StartsWith("attack")) PlayOn(s, $"{_clip}-{_facing}");
        };
        s.Play($"{_clip}-0");
        var lbl = new Label { Position = pos + new Vector2(-70, 155) };
        AddChild(lbl);
        _rows.Add(new Row { Sprite = s, Label = lbl, Name = key });
    }

    private static void PlayOn(AnimatedSprite2D s, string anim)
    {
        if (s.SpriteFrames.HasAnimation(anim)) s.Play(anim);
    }

    private bool Busy(AnimatedSprite2D s)
    {
        var a = (string)s.Animation;
        return (a.StartsWith("attack") || a.StartsWith("death")) && s.IsPlaying();
    }

    public override void _Process(double delta)
    {
        _facTimer += delta;
        if (_facTimer >= 1.0)
        {
            _facTimer = 0;
            _facing = (_facing + 1) % 8;
            foreach (var r in _rows)
                if (!Busy(r.Sprite))
                    PlayOn(r.Sprite, $"{_clip}-{_facing}");
        }
        foreach (var r in _rows)
            r.Label.Text = $"{r.Name}   {r.Sprite.Animation}";
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } k) return;
        switch (k.Keycode)
        {
            case Key.Space: foreach (var r in _rows) PlayOn(r.Sprite, $"attack-{_facing}"); break;
            case Key.D: foreach (var r in _rows) PlayOn(r.Sprite, $"death-{_facing}"); break;
            case Key.I: _clip = "idle"; foreach (var r in _rows) PlayOn(r.Sprite, $"idle-{_facing}"); break;
            case Key.W: _clip = "walk"; foreach (var r in _rows) PlayOn(r.Sprite, $"walk-{_facing}"); break;
        }
    }
}
