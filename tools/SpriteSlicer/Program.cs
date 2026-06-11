using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SpriteSlicer;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: SpriteSlicer <sidecar.json> [more sidecars...] (paths relative to repo root)");
    return 1;
}

// canonical output row order and frame counts (sprite sheet contract)
var contract = new (string Anim, string Facing, int Frames)[]
{
    ("idle","S",4), ("idle","W",4), ("idle","N",4),
    ("walk","S",6), ("walk","W",6), ("walk","N",6),
    ("attack","S",6), ("attack","W",6), ("attack","N",6),
    ("death","S",6),
};
const int Cell = 64;
const int SheetW = 6 * Cell;

foreach (var sidecarPath in args)
{
    var sc = JsonSerializer.Deserialize<Sidecar>(File.ReadAllText(sidecarPath),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException($"unreadable sidecar {sidecarPath}");
    if (sc.BaselinePx < 0 || sc.BaselinePx >= Cell - 2)
        throw new InvalidDataException($"{sidecarPath}: baselinePx {sc.BaselinePx} out of range [0, {Cell - 3}]");

    using var raw = Image.Load<Rgba32>(sc.Source);
    var iconRect = new Rectangle(sc.Icon.X, sc.Icon.Y, sc.Icon.W, sc.Icon.H);
    if (iconRect.X < 0 || iconRect.Y < 0 || iconRect.Right > raw.Width || iconRect.Bottom > raw.Height)
        throw new InvalidDataException($"{sidecarPath}: icon rect {iconRect} outside {raw.Width}x{raw.Height}");

    using var sheet = new Image<Rgba32>(SheetW, contract.Length * Cell);

    for (int r = 0; r < contract.Length; r++)
    {
        var (anim, facing, frames) = contract[r];
        var row = sc.Rows.FirstOrDefault(x => x.Anim == anim && x.Facing == facing)
            ?? throw new InvalidDataException($"{sidecarPath}: missing row {anim}-{facing}");
        if (row.Frames < 1 || row.Frames > frames)
            throw new InvalidDataException($"{sidecarPath}: {anim}-{facing} has {row.Frames} frames, contract allows 1..{frames}");
        var rect = new Rectangle(row.Rect.X, row.Rect.Y, row.Rect.W, row.Rect.H);
        if (rect.X < 0 || rect.Y < 0 || rect.Right > raw.Width || rect.Bottom > raw.Height)
            throw new InvalidDataException($"{sidecarPath}: {anim}-{facing} rect {rect} outside {raw.Width}x{raw.Height}");

        var cut = Slicer.SliceRow(raw, rect, row.Frames);
        while (cut.Count < frames) cut.Add(cut[^1].Clone()); // pad short rows by repeating last frame
        for (int f = 0; f < cut.Count; f++)
        {
            Slicer.ChromaKey(cut[f]);
            using var cell = Slicer.RenderToCell(cut[f], Cell, sc.BaselinePx);
            int fx = f * Cell, ry = r * Cell;
            sheet.Mutate(c => c.DrawImage(cell, new Point(fx, ry), 1f));
            cut[f].Dispose();
        }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(sc.Output)!);
    sheet.SaveAsPng(sc.Output);

    using var icon = raw.Clone(c => c.Crop(iconRect));
    Slicer.ChromaKey(icon);
    icon.Mutate(c => c.Resize(64, 64, KnownResamplers.NearestNeighbor));
    Directory.CreateDirectory(Path.GetDirectoryName(sc.IconOutput)!);
    icon.SaveAsPng(sc.IconOutput);

    Console.WriteLine($"OK {sc.Source} -> {sc.Output} + {sc.IconOutput}");
}
return 0;
