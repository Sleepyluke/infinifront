namespace SpriteSlicer;

public sealed record RectDef(int X, int Y, int W, int H);

public sealed record RowDef(string Anim, string Facing, int Frames, RectDef Rect);

/// <summary>Hand-authored description of where content lives in a raw AI sheet.
/// Output contract: rows idle-S,idle-W,idle-N,walk-S,walk-W,walk-N,
/// attack-S,attack-W,attack-N,death-S; frames 4,4,4,6,6,6,6,6,6,6; 64px cells.</summary>
public sealed record Sidecar(
    string Source,          // raw sheet path, relative to repo root
    string Output,          // output sheet path
    string IconOutput,      // output icon path
    RectDef Icon,           // icon source rect in the raw sheet
    int BaselinePx,         // feet offset from cell bottom
    List<RowDef> Rows);
