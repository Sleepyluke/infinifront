using Godot;

namespace LlmRts.Godot;

/// <summary>Loads an 8-facing, multi-clip sprite sheet baked by the local UniRig→render pipeline
/// (E:\sprite3d\pack_sheet.py). Sheet contract: 64px cells, 16 columns (frames, each clip resampled
/// to 16), rows = clipBlock*8 + facing. Block 0 = walk (facings 0-7), block 1 = attack (facings 0-7).
/// Builds animations named "walk-0".."walk-7" and "attack-0".."attack-7".
///
/// This is ADDITIVE and parallel to <see cref="SheetAnimator"/> (which is 3-facing): it only loads
/// when assets/units/anim/&lt;unitKey&gt;8.png exists, so it never disturbs existing units.</summary>
public static class EightFacingSheet
{
    private const int Cell = 64, Cols = 16, Fac = 8;

    private static readonly (string Name, int Block, float Fps, bool Loop)[] Clips =
    {
        ("walk", 0, 12f, true),
        ("attack", 1, 16f, false),
    };

    public static SpriteFrames? Load(string unitKey)
    {
        var path = $"res://assets/units/anim/{unitKey}8.png";
        if (!ResourceLoader.Exists(path)) return null;
        var tex = GD.Load<Texture2D>(path);
        // Validate the 16-col × (8*clips)-row, 64px grid; otherwise treat it as not-a-sheet.
        if (tex.GetWidth() < Cols * Cell || tex.GetHeight() < Clips.Length * Fac * Cell) return null;
        var frames = new SpriteFrames();
        if (frames.HasAnimation("default")) frames.RemoveAnimation("default");
        foreach (var (name, block, fps, loop) in Clips)
            for (int fa = 0; fa < Fac; fa++)
            {
                var anim = $"{name}-{fa}";
                frames.AddAnimation(anim);
                frames.SetAnimationSpeed(anim, fps);
                frames.SetAnimationLoop(anim, loop);
                int row = block * Fac + fa;
                for (int c = 0; c < Cols; c++)
                    frames.AddFrame(anim, new AtlasTexture { Atlas = tex, Region = new Rect2(c * Cell, row * Cell, Cell, Cell) });
            }
        return frames;
    }

    /// <summary>8-way facing 0..7 from a screen-space direction (Y down). Index increases to match
    /// the bake's pivot rotation (0,45,…315° about +Z). <see cref="FacingOffset"/> aligns the bake's
    /// pivot-0 to the in-game heading — nudge it if a unit reads rotated relative to its movement.</summary>
    public const int FacingOffset = 0;

    public static int FacingIndex(Vector2 dir)
    {
        if (dir.LengthSquared() < 1e-6f) return 0;
        float deg = Mathf.RadToDeg(Mathf.Atan2(dir.Y, dir.X));        // -180..180, screen space (Y down)
        int raw = (int)Mathf.Round(deg / 45f) + FacingOffset;
        return ((raw % Fac) + Fac) % Fac;
    }
}
