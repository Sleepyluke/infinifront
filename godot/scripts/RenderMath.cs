using Godot;
using SimCore.Math;

namespace LlmRts.Godot;

public static class RenderMath
{
    public const int CellPx = 64;
    private static readonly float FixScale = (float)Fix.FromInt(1).Raw;

    public static float ToF(Fix v) => (float)v.Raw / FixScale;
    public static Vector2 ToPx(FixVec v) => new(ToF(v.X) * CellPx, ToF(v.Y) * CellPx);
    public static Vector2 CellToPx(int cx, int cy) => new(cx * CellPx, cy * CellPx);
    public static (int cx, int cy) PxToCell(Vector2 px) =>
        ((int)System.Math.Floor(px.X / CellPx), (int)System.Math.Floor(px.Y / CellPx));

    /// <summary>Snap a direction vector to S/W/N/E. Ties prefer horizontal.</summary>
    public static string FacingOf(Vector2 dir)
    {
        if (dir.LengthSquared() < 0.0001f) return "S";
        return System.Math.Abs(dir.X) >= System.Math.Abs(dir.Y)
            ? (dir.X < 0 ? "W" : "E")
            : (dir.Y < 0 ? "N" : "S");
    }
}
