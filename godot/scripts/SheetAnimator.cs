using Godot;

namespace LlmRts.Godot;

/// <summary>Contract sheet layout: 64px cells, rows idle-S/W/N (4f),
/// walk-S/W/N (6f), attack-S/W/N (6f), death-S (6f).
/// Animation names "idle-S" etc.; East renders West flipped (caller's job).</summary>
public static class SheetAnimator
{
    private const int Cell = 64;
    private static readonly (string Name, int Row, int Frames, float Fps, bool Loop)[] Layout =
    {
        ("idle-S", 0, 4, 4f, true), ("idle-W", 1, 4, 4f, true), ("idle-N", 2, 4, 4f, true),
        ("walk-S", 3, 6, 10f, true), ("walk-W", 4, 6, 10f, true), ("walk-N", 5, 6, 10f, true),
        ("attack-S", 6, 6, 12f, false), ("attack-W", 7, 6, 12f, false), ("attack-N", 8, 6, 12f, false),
        ("death-S", 9, 6, 8f, false),
    };

    public static SpriteFrames? Load(string unitKey)
    {
        var path = $"res://assets/units/{unitKey}.png";
        if (!ResourceLoader.Exists(path)) return null; // fallback: caller draws silhouette
        var tex = GD.Load<Texture2D>(path);
        // Only treat this as an animation SHEET if it matches the 6-col × 10-row, 64px grid.
        // A single-frame static sprite (e.g. a pack faction's ~256×230 art) is NOT a sheet, so
        // return null and let the caller render it as a plain static Sprite2D instead of slicing
        // it into garbage 64px cells. (This guards the def-id collision: reference units fabber/
        // trooper/outrider/tank ARE 384×640 sheets; pack units are not.)
        if (tex.GetWidth() < 6 * Cell || tex.GetHeight() < 10 * Cell) return null;
        var frames = new SpriteFrames();
        // Remove the default animation that SpriteFrames ships with so Play() calls
        // never accidentally target it.
        if (frames.HasAnimation("default"))
            frames.RemoveAnimation("default");
        foreach (var (name, row, count, fps, loop) in Layout)
        {
            frames.AddAnimation(name);
            frames.SetAnimationSpeed(name, fps);
            frames.SetAnimationLoop(name, loop);
            for (int f = 0; f < count; f++)
                frames.AddFrame(name, new AtlasTexture { Atlas = tex, Region = new Rect2(f * Cell, row * Cell, Cell, Cell) });
        }
        return frames;
    }
}
