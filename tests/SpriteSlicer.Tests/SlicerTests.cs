using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SpriteSlicer;
using Xunit;

public class SlicerTests
{
    private static Image<Rgba32> Filled(int w, int h, Rgba32 color)
    {
        var img = new Image<Rgba32>(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                img[x, y] = color;
        return img;
    }

    private static readonly Rgba32 Magenta = new(255, 0, 255, 255);
    private static readonly Rgba32 Green = new(0, 200, 0, 255);

    [Fact]
    public void ChromaKey_Clears_Magenta_Keeps_Content()
    {
        var img = Filled(10, 10, Magenta);
        img[5, 5] = Green;
        Slicer.ChromaKey(img);
        Assert.Equal(0, img[0, 0].A);
        Assert.Equal(255, img[5, 5].A);
    }

    [Fact]
    public void ChromaKey_Tolerance_Catches_Compression_Fringe()
    {
        var img = Filled(4, 1, new Rgba32(247, 12, 244, 255)); // near-magenta
        Slicer.ChromaKey(img);
        Assert.Equal(0, img[0, 0].A);
    }

    [Fact]
    public void SliceRow_Produces_FrameCount_Equal_Cells()
    {
        // Non-zero rect origin + one distinct marker pixel per frame region:
        // catches offset bugs where frames are cut from the wrong place.
        var sheet = Filled(400, 200, Magenta);
        var rect = new Rectangle(50, 40, 300, 100); // 3 frames of 100x100
        var markers = new[]
        {
            new Rgba32(10, 20, 30, 255),
            new Rgba32(40, 50, 60, 255),
            new Rgba32(70, 80, 90, 255),
        };
        for (int i = 0; i < 3; i++)
            sheet[rect.X + i * 100 + 50, rect.Y + 50] = markers[i]; // center of each frame region

        var frames = Slicer.SliceRow(sheet, rect, 3);

        Assert.Equal(3, frames.Count);
        Assert.All(frames, f => Assert.Equal(100, f.Width));
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(markers[i], frames[i][50, 50]);
            // marker from neighboring frames must NOT appear in this frame
            for (int j = 0; j < 3; j++)
                if (j != i)
                    Assert.NotEqual(markers[j], frames[i][50, 50]);
        }
    }

    [Fact]
    public void RenderToCell_Centers_Content_On_Baseline()
    {
        // 100x100 frame, content = 20x40 green block at left edge
        var frame = Filled(100, 100, new Rgba32(0, 0, 0, 0));
        for (int y = 30; y < 70; y++)
            for (int x = 0; x < 20; x++)
                frame[x, y] = Green;
        var cell = Slicer.RenderToCell(frame, cellSize: 64, baselinePx: 8);
        Assert.Equal(64, cell.Width);
        Assert.Equal(64, cell.Height);
        var (minX, minY, maxX, maxY) = Slicer.ContentBounds(cell)!.Value;
        Assert.Equal(64 - 8 - 1, maxY);                       // bottom on baseline
        Assert.True(System.Math.Abs((minX + maxX) / 2 - 31) <= 1); // centered ±1px
    }

    [Fact]
    public void ContentBounds_Returns_Null_For_Empty_Frame()
    {
        var empty = Filled(10, 10, new Rgba32(0, 0, 0, 0));
        Assert.Null(Slicer.ContentBounds(empty));
    }
}
