using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

public partial class UnitView : Node2D
{
    public int UnitId { get; private set; }
    public int OwnerId { get; private set; }
    public bool Selected { get; set; }

    private AnimatedSprite2D? _sprite;     // null → silhouette fallback
    private SimRunner _runner = null!;
    private Color _fallbackColor;
    private string _facing = "S";
    private Vector2 _prevPos, _currPos;
    private int _maxHp = 1, _hp = 1;
    private int _shieldHp;
    private bool _attacking;
    private bool _dying;
    private bool _isPatrolling;

    // One per player slot. Must cover the max players a match can have (4 — see MatchSetup corners),
    // or rendering a higher-id player's unit throws IndexOutOfRange and aborts the match build.
    public static readonly Color[] PlayerColors =
    {
        new(0.2f, 0.45f, 1f),    // P0 blue
        new(1f, 0.35f, 0.25f),   // P1 red
        new(0.3f, 0.85f, 0.4f),  // P2 green
        new(0.95f, 0.8f, 0.2f),  // P3 yellow
    };

    public void Init(Unit u, string unitKey, SimRunner runner)
    {
        _runner = runner;
        UnitId = u.Id;
        OwnerId = u.OwnerId;
        _hp = _maxHp = u.Hp;
        _fallbackColor = PlayerColors[u.OwnerId % PlayerColors.Length];   // wrap, never crash the match build
        var frames = SheetAnimator.Load(unitKey);
        if (frames is not null)
        {
            _sprite = new AnimatedSprite2D { SpriteFrames = frames, Centered = true };
            _sprite.AnimationFinished += () => _attacking = false;
            AddChild(_sprite);
            _sprite.Play("idle-S");
        }
        _prevPos = _currPos = RenderMath.ToPx(u.Position);
        Position = _currPos;
    }

    /// <summary>Called by ViewSync after every sim tick.</summary>
    public void SyncTick(Unit u)
    {
        _prevPos = _currPos;
        _currPos = RenderMath.ToPx(u.Position);
        _hp = u.Hp;
        if (u.Hp > _maxHp) _maxHp = u.Hp;
        _shieldHp = u.ShieldHp;

        var delta = _currPos - _prevPos;
        bool moving = delta.LengthSquared() > 0.01f;
        if (moving) _facing = RenderMath.FacingOf(delta);

        bool justFired = u.Weapon is not null && u.Weapon.CooldownRemaining == u.Weapon.CooldownTicks && u.AttackTargetId != 0;
        if (justFired) _attacking = true;

        _isPatrolling = u.IsPatrolling;

        PlayAnim(_attacking ? "attack" : moving ? "walk" : "idle");
    }

    /// <summary>Face an explicit world position (attack target). Called by ViewSync.</summary>
    public void FaceToward(Vector2 worldPx)
    {
        var d = worldPx - _currPos;
        if (d.LengthSquared() > 0.01f) _facing = RenderMath.FacingOf(d);
    }

    private void PlayAnim(string baseName)
    {
        if (_sprite is null) return;
        var storedFacing = _facing == "E" ? "W" : _facing;
        _sprite.FlipH = _facing == "E";
        var anim = $"{baseName}-{storedFacing}";
        if (_sprite.Animation != anim || !_sprite.IsPlaying())
            _sprite.Play(anim);
    }

    /// <summary>Detached corpse: plays death-S once, then frees.</summary>
    public void PlayDeathAndFree()
    {
        _prevPos = _currPos;
        Selected = false;
        _dying = true;
        if (_sprite is null) { QueueFree(); return; }
        _sprite.FlipH = false;
        _sprite.Play("death-S");
        _sprite.AnimationFinished += QueueFree;
    }

    public override void _Process(double delta)
    {
        Position = _prevPos.Lerp(_currPos, _runner.Alpha);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_sprite is null)
        {
            DrawCircle(Vector2.Zero, 20, _fallbackColor);
        }
        if (_dying) return;
        if (Selected)
            DrawArc(new Vector2(0, 18), 22, 0, Mathf.Tau, 32, Colors.Lime, 2);
        if (_hp < _maxHp && _hp > 0)
        {
            float frac = (float)_hp / _maxHp;
            var color = frac > 0.66f ? Colors.Lime : frac > 0.33f ? Colors.Yellow : Colors.Red;
            DrawRect(new Rect2(-16, -36, 32, 4), Colors.Black);
            DrawRect(new Rect2(-16, -36, 32 * frac, 4), color);
        }
        if (_shieldHp > 0 &&
            _runner.World.Faction?.Mechanic is { Kind: SimCore.Sim.MechanicKind.RegeneratingShields } m &&
            m.MaxShield > 0)
        {
            float shieldFrac = (float)_shieldHp / m.MaxShield;
            DrawRect(new Rect2(-16, -41, 32, 4), Colors.Black);
            DrawRect(new Rect2(-16, -41, 32 * shieldFrac, 4), Colors.Cyan);
        }

        // Patrol glyph: small cyan double-triangle above the unit when patrolling.
        if (_isPatrolling && Selected)
        {
            // Two small arrow triangles pointing left and right, side by side above unit.
            var top = new Vector2(0, -46);
            var cyan = new Color(0f, 1f, 1f, 0.9f);
            // Left-pointing triangle
            DrawColoredPolygon(new Vector2[]
            {
                top + new Vector2(-10, -5),
                top + new Vector2(-4, -9),
                top + new Vector2(-4, -1),
            }, cyan);
            // Right-pointing triangle
            DrawColoredPolygon(new Vector2[]
            {
                top + new Vector2(10, -5),
                top + new Vector2(4, -9),
                top + new Vector2(4, -1),
            }, cyan);
        }
    }
}
