using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SpriteSlicer;

public static class Slicer
{
    /// <summary>Per-pixel distance to #FF00FF below tolerance → alpha 0.
    /// Tolerance is generous (Gemini PNGs carry compression fringe).
    /// Also clears magenta-dominant blend pixels (R and B both well above G) —
    /// these are antialiased sprite/background edges that would otherwise leave
    /// a magenta halo after downscaling.</summary>
    public static void ChromaKey(Image<Rgba32> img, int tolerance = 40)
    {
        img.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    int d = System.Math.Abs(p.R - 255) + p.G + System.Math.Abs(p.B - 255);
                    bool magentaBlend = p.R > p.G + 70 && p.B > p.G + 70;
                    if (d <= tolerance || magentaBlend) row[x] = new Rgba32(0, 0, 0, 0);
                }
            }
        });
    }

    /// <summary>Cuts a row rect into equal-width frames. When rowRect.Width is not
    /// divisible by frameCount, the remainder pixels at the right edge are truncated
    /// (each frame is Width / frameCount wide, integer division).</summary>
    public static List<Image<Rgba32>> SliceRow(Image<Rgba32> sheet, Rectangle rowRect, int frameCount)
    {
        var frames = new List<Image<Rgba32>>(frameCount);
        int fw = rowRect.Width / frameCount;
        for (int i = 0; i < frameCount; i++)
            frames.Add(sheet.Clone(c => c.Crop(new Rectangle(rowRect.X + i * fw, rowRect.Y, fw, rowRect.Height))));
        return frames;
    }

    /// <summary>Bounding box of non-transparent pixels, or null if empty.</summary>
    public static (int minX, int minY, int maxX, int maxY)? ContentBounds(Image<Rgba32> img)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        img.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    if (row[x].A > 0)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            }
        });
        return maxX < 0 ? null : (minX, minY, maxX, maxY);
    }

    /// <summary>Crops to content, scales (nearest-neighbor) to fit the cell with
    /// a small margin, centers horizontally, bottom-aligns to cellSize - baselinePx.
    /// Note: scales both up AND down — small content is enlarged and large content
    /// is shrunk, normalizing content size across frames of differing resolution.</summary>
    public static Image<Rgba32> RenderToCell(Image<Rgba32> frame, int cellSize, int baselinePx)
    {
        var cell = new Image<Rgba32>(cellSize, cellSize);
        var bounds = ContentBounds(frame);
        if (bounds is null) return cell; // empty frame → empty cell

        var (minX, minY, maxX, maxY) = bounds.Value;
        var content = frame.Clone(c => c.Crop(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)));

        int avail = cellSize - baselinePx - 2;
        double scale = System.Math.Min((double)(cellSize - 4) / content.Width, (double)avail / content.Height);
        content.Mutate(c => c.Resize(
            System.Math.Max(1, (int)(content.Width * scale)),
            System.Math.Max(1, (int)(content.Height * scale)),
            KnownResamplers.NearestNeighbor));

        int px = (cellSize - content.Width) / 2;
        int py = cellSize - baselinePx - content.Height;
        cell.Mutate(c => c.DrawImage(content, new Point(px, py), 1f));
        return cell;
    }
}
