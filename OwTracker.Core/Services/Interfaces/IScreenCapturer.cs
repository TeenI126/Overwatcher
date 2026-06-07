using System.Drawing;

namespace OwTracker.Core.Services.Interfaces;

public interface IScreenCapturer
{
    /// <summary>Captures the current foreground window (assumed to be OW). Null if unavailable.</summary>
    Bitmap? CaptureOwWindow();

    /// <summary>Returns a new bitmap containing the given region of <paramref name="source"/>.</summary>
    Bitmap CropRegion(Bitmap source, Rectangle roi);

    /// <summary>Slices the ending-hero portraits out of a Teams-tab capture.</summary>
    List<Bitmap> CropHeroPortraits(Bitmap source, HeroPortraitRegion region);
}
