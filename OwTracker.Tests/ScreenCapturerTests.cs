using System.Drawing;
using OwTracker.Core.Services;

namespace OwTracker.Tests;

public class ScreenCapturerTests
{
    [Fact]
    public void CropRegion_ReturnsRequestedSize()
    {
        using var source = new Bitmap(200, 100);
        var capturer = new ScreenCapturer();

        using var crop = capturer.CropRegion(source, new Rectangle(10, 20, 50, 40));

        Assert.Equal(50, crop.Width);
        Assert.Equal(40, crop.Height);
    }

    [Fact]
    public void CropRegion_ClampsToSourceBounds()
    {
        using var source = new Bitmap(100, 100);
        var capturer = new ScreenCapturer();

        // ROI extends past the edge; expect it clamped to the remaining area.
        using var crop = capturer.CropRegion(source, new Rectangle(80, 80, 50, 50));

        Assert.Equal(20, crop.Width);
        Assert.Equal(20, crop.Height);
    }

    [Fact]
    public void CropRegion_ThrowsWhenRoiOutsideSource()
    {
        using var source = new Bitmap(100, 100);
        var capturer = new ScreenCapturer();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => capturer.CropRegion(source, new Rectangle(200, 200, 10, 10)));
    }

    [Fact]
    public void CropHeroPortraits_SlicesRequestedRowCount()
    {
        using var source = new Bitmap(1000, 800);
        var capturer = new ScreenCapturer();
        var region = new HeroPortraitRegion
        {
            FirstRow = new RoiRect(100, 50, 64, 64),
            RowStride = 70,
            RowCount = 5
        };

        var crops = capturer.CropHeroPortraits(source, region);

        Assert.Equal(5, crops.Count);
        Assert.All(crops, c => Assert.Equal(64, c.Width));
        foreach (var c in crops) c.Dispose();
    }
}
