using System.Windows.Media;

namespace OwTracker.App.Theme;

/// <summary>
/// The HUD theme palette as <see cref="Color"/>/<see cref="Brush"/> values for code-side use
/// (converters, hero chips). The same hexes are mirrored as XAML brushes in <c>Themes/Hud.xaml</c>;
/// keep the two in sync. Values come from the design handoff §6 (theme <c>.ow--hud</c>).
/// </summary>
public static class Palette
{
    public static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
    private static Brush Frozen(string hex) { var b = new SolidColorBrush(Hex(hex)); b.Freeze(); return b; }

    public static readonly Brush Win   = Frozen("#ff7a18");
    public static readonly Brush Loss  = Frozen("#d8463a");
    public static readonly Brush Draw  = Frozen("#6c7280");
    public static readonly Brush Accent = Frozen("#ff7a18");
    public static readonly Brush Muted = Frozen("#808992");
    public static readonly Brush Text  = Frozen("#e7ebf0");

    // Role colours
    public static readonly Brush Tank    = Frozen("#5aa9e6");
    public static readonly Brush Damage  = Frozen("#e8743b");
    public static readonly Brush Support = Frozen("#5fd38d");

    public static Brush RoleBrush(string? role) => (role ?? string.Empty).ToLowerInvariant() switch
    {
        "tank"    => Tank,
        "damage"  => Damage,
        "support" => Support,
        _         => Muted,
    };

    // Mode (objective-type) colours
    public static Brush ModeBrush(string? mode) => (mode ?? string.Empty) switch
    {
        "Control"    => Frozen("#4ea6e6"),
        "Hybrid"     => Frozen("#b478e0"),
        "Escort"     => Frozen("#e8a13b"),
        "Push"       => Frozen("#54cf8a"),
        "Flashpoint" => Frozen("#ec7140"),
        "Clash"      => Frozen("#dc6086"),
        _            => Muted,
    };

    // Diverging win-rate scale (heatmap cell background) — interpolate by win rate 0→1.
    private static readonly (double P, Color C)[] Stops =
    {
        (0.20, Hex("#2e4258")), (0.40, Hex("#3a4150")), (0.50, Hex("#46464d")),
        (0.62, Hex("#7c5a30")), (0.76, Hex("#c87327")), (0.90, Hex("#ff8a2e")), (1.0, Hex("#ff9d44")),
    };

    public static Color WinRateColor(double t)
    {
        t = Math.Clamp(t, Stops[0].P, Stops[^1].P);
        for (var i = 0; i < Stops.Length - 1; i++)
        {
            var (p0, c0) = Stops[i];
            var (p1, c1) = Stops[i + 1];
            if (t >= p0 && t <= p1)
            {
                var f = (t - p0) / Math.Max(1e-9, p1 - p0);
                return Color.FromRgb(
                    (byte)Math.Round(c0.R + (c1.R - c0.R) * f),
                    (byte)Math.Round(c0.G + (c1.G - c0.G) * f),
                    (byte)Math.Round(c0.B + (c1.B - c0.B) * f));
            }
        }
        return Stops[^1].C;
    }

    public static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
}
