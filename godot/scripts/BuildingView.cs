using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

/// <summary>Silhouette-fallback building rendering: colored footprint,
/// construction progress shading, glyph, destruction fade-out.
/// Swap to textures later by checking ResourceLoader.Exists like SheetAnimator.</summary>
public partial class BuildingView : Node2D
{
    private bool _isDepot, _canTrain;
    private int _ownerId, _maxHp, _hp, _queueCount;
    private float _progress; // 0..1 construction
    private Vector2 _sizePx;
    private Texture2D? _sprite;                 // per-def building art, else null → box fallback
    private const float SpriteScale = 1.35f;    // building art overhangs its footprint a little

    public int BuildingId { get; private set; }

    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set { _selected = value; QueueRedraw(); }
    }

    private Vector2? _rallyPx; // null = no rally point

    public void Init(Building b)
    {
        BuildingId = b.Id;
        _ownerId = b.OwnerId;
        _isDepot = b.Spec.IsDepot;
        _canTrain = b.Spec.CanTrain;
        _maxHp = b.Spec.MaxHp;
        _sizePx = new Vector2(b.Spec.Width * RenderMath.CellPx, b.Spec.Height * RenderMath.CellPx);
        Position = RenderMath.CellToPx(b.CellX, b.CellY);
        var path = $"res://assets/buildings/{b.DefId}.png";
        if (ResourceLoader.Exists(path)) _sprite = ResourceLoader.Load<Texture2D>(path);
        SyncTick(b);
    }

    public void SyncTick(Building b)
    {
        _hp = b.Hp;
        _queueCount = b.Queue.Count;
        _progress = b.IsComplete ? 1f : (float)b.BuildProgress / b.Spec.BuildTimeTicks;
        _rallyPx = b.HasRally ? RenderMath.ToPx(b.RallyPoint) : (Vector2?)null;
        QueueRedraw();
    }

    public void PlayDestructionAndFree()
    {
        Modulate = new Color(0.2f, 0.18f, 0.16f);
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 0f, 0.8f);
        tween.TweenCallback(Callable.From(QueueFree));
    }

    public override void _Draw()
    {
        var team = UnitView.PlayerColors[_ownerId % UnitView.PlayerColors.Length];

        if (_sprite is not null)
        {
            // Team-color ground pad — ownership at a glance (the sprite's red accents are baked).
            DrawColoredPolygon(Ellipse(new Vector2(_sizePx.X / 2f, _sizePx.Y - 4f),
                                       _sizePx.X * 0.5f, _sizePx.X * 0.16f, 24), team with { A = 0.30f });
            float w = _sizePx.X * SpriteScale;
            float h = w * _sprite.GetHeight() / _sprite.GetWidth();
            // Bottom-centered over the footprint, allowed to overhang upward like an RTS structure.
            var rect = new Rect2((_sizePx.X - w) / 2f, _sizePx.Y - h, w, h);
            DrawTextureRect(_sprite, rect, false, new Color(1, 1, 1, 0.35f + 0.65f * _progress));
        }
        else
        {
            // construction: dim body fills bottom-up with progress
            DrawRect(new Rect2(Vector2.Zero, _sizePx), team with { A = 0.25f });
            var filledH = _sizePx.Y * _progress;
            DrawRect(new Rect2(0, _sizePx.Y - filledH, _sizePx.X, filledH), team with { A = 0.85f });
            DrawRect(new Rect2(Vector2.Zero, _sizePx), Colors.Black, filled: false, width: 2);
            var glyph = _isDepot ? "D" : _canTrain ? "B" : "?";
            DrawString(ThemeDB.FallbackFont, _sizePx / 2 + new Vector2(-8, 10), glyph,
                HorizontalAlignment.Center, -1, 28, Colors.White);
        }

        if (Selected)
            DrawRect(new Rect2(Vector2.Zero, _sizePx), Colors.Lime, filled: false, width: 2);

        if (_hp < _maxHp && _hp > 0)
        {
            float frac = (float)_hp / _maxHp;
            var c = frac > 0.66f ? Colors.Lime : frac > 0.33f ? Colors.Yellow : Colors.Red;
            DrawRect(new Rect2(4, -10, _sizePx.X - 8, 5), Colors.Black);
            DrawRect(new Rect2(4, -10, (_sizePx.X - 8) * frac, 5), c);
        }

        for (int i = 0; i < _queueCount; i++)
            DrawRect(new Rect2(4 + i * 10, _sizePx.Y + 4, 8, 8), Colors.White with { A = 0.8f });

        // Rally flag: dashed line from footprint centre to rally point, with a small flag.
        if (Selected && _rallyPx.HasValue)
        {
            // Convert world-px rally point to local coords (BuildingView draws in local space).
            var localRally = _rallyPx.Value - Position;
            var centre = _sizePx / 2;
            // Dashed line approximation: draw segments.
            var dir = localRally - centre;
            float len = dir.Length();
            if (len > 1f)
            {
                var step = dir.Normalized() * 8f;
                int segments = (int)(len / 8f);
                for (int i = 0; i < segments; i += 2)
                {
                    var a = centre + step * i;
                    var b2 = centre + step * System.Math.Min(i + 1, segments);
                    DrawLine(a, b2, Colors.Yellow with { A = 0.8f }, 1.5f);
                }
            }
            // Flag: small triangle at rally point in local coords.
            var flagPos = localRally;
            DrawColoredPolygon(new Vector2[]
            {
                flagPos + new Vector2(0, -14),
                flagPos + new Vector2(8, -10),
                flagPos + new Vector2(0, -6),
            }, Colors.Yellow with { A = 0.9f });
            DrawLine(flagPos + new Vector2(0, -14), flagPos, Colors.Yellow with { A = 0.9f }, 1.5f);
        }
    }

    private static Vector2[] Ellipse(Vector2 c, float rx, float ry, int n)
    {
        var p = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float t = i * Mathf.Tau / n;
            p[i] = c + new Vector2(Mathf.Cos(t) * rx, Mathf.Sin(t) * ry);
        }
        return p;
    }
}
