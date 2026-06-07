using System.Drawing;
using System.Drawing.Imaging;
using OwTracker.Core.Services.Interfaces;

namespace OwTracker.Core.Services;

/// <summary>
/// GDI screen capture of the OW window and ROI cropping (design §6.2). Screen-level only —
/// no access to the OW process. Windows-only (uses System.Drawing / GDI).
/// </summary>
public sealed class ScreenCapturer : IScreenCapturer
{
    public Bitmap? CaptureOwWindow()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return null;

        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            return null;

        var width = rect.Width;
        var height = rect.Height;
        if (width <= 0 || height <= 0)
            return null;

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public Bitmap CropRegion(Bitmap source, Rectangle roi)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Clamp the ROI to the source bounds so off-screen calibration values can't throw.
        var bounds = new Rectangle(0, 0, source.Width, source.Height);
        var clamped = Rectangle.Intersect(bounds, roi);
        if (clamped.Width <= 0 || clamped.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(roi), "ROI does not intersect the source image.");

        return source.Clone(clamped, source.PixelFormat);
    }

    public List<Bitmap> CropHeroPortraits(Bitmap source, HeroPortraitRegion region)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(region);

        var crops = new List<Bitmap>(region.RowCount);
        var first = region.FirstRow.Rectangle;

        for (var i = 0; i < region.RowCount; i++)
        {
            var roi = first with { Y = first.Y + (i * region.RowStride) };
            crops.Add(CropRegion(source, roi));
        }

        return crops;
    }
}
