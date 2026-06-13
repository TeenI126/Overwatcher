using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OwTracker.App.Theme;

namespace OwTracker.App.Controls;

/// <summary>
/// The small role-coloured square showing a hero's two-letter initials — the redesign's
/// <c>.hchip</c>. Used in the match-card hero stack, the heatmap row heads, the ranked lists and
/// the session hero strips. Unknown heroes fall back to a muted chip with "—".
/// </summary>
public sealed class HeroChip : Control
{
    static HeroChip()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(HeroChip), new FrameworkPropertyMetadata(typeof(HeroChip)));
    }

    public static readonly DependencyProperty HeroNameProperty = DependencyProperty.Register(
        nameof(HeroName), typeof(string), typeof(HeroChip),
        new PropertyMetadata(string.Empty, OnHeroChanged));

    public static readonly DependencyProperty RoleProperty = DependencyProperty.Register(
        nameof(Role), typeof(string), typeof(HeroChip),
        new PropertyMetadata(string.Empty, OnRoleChanged));

    public static readonly DependencyProperty ChipSizeProperty = DependencyProperty.Register(
        nameof(ChipSize), typeof(double), typeof(HeroChip),
        new PropertyMetadata(32.0, OnSizeChanged));

    public static readonly DependencyProperty InitialsProperty = DependencyProperty.Register(
        nameof(Initials), typeof(string), typeof(HeroChip), new PropertyMetadata("—"));

    public static readonly DependencyProperty RoleBrushProperty = DependencyProperty.Register(
        nameof(RoleBrush), typeof(Brush), typeof(HeroChip), new PropertyMetadata(Palette.Muted));

    public static readonly DependencyProperty FontSizePxProperty = DependencyProperty.Register(
        nameof(FontSizePx), typeof(double), typeof(HeroChip), new PropertyMetadata(12.0));

    public string HeroName { get => (string)GetValue(HeroNameProperty); set => SetValue(HeroNameProperty, value); }
    public string Role { get => (string)GetValue(RoleProperty); set => SetValue(RoleProperty, value); }
    public double ChipSize { get => (double)GetValue(ChipSizeProperty); set => SetValue(ChipSizeProperty, value); }

    // Computed, template-bound.
    public string Initials { get => (string)GetValue(InitialsProperty); private set => SetValue(InitialsProperty, value); }
    public Brush RoleBrush { get => (Brush)GetValue(RoleBrushProperty); private set => SetValue(RoleBrushProperty, value); }
    public double FontSizePx { get => (double)GetValue(FontSizePxProperty); private set => SetValue(FontSizePxProperty, value); }

    private static void OnHeroChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        => ((HeroChip)o).Initials = ToInitials((string)e.NewValue);

    private static void OnRoleChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        => ((HeroChip)o).RoleBrush = Palette.RoleBrush(e.NewValue as string);

    private static void OnSizeChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        => ((HeroChip)o).FontSizePx = Math.Max(8, (double)e.NewValue * 0.36);

    private static string ToInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "—";
        if (name == "D.Va") return "DV";
        if (name == "Lúcio") return "LÚ";
        var cleaned = new string(name.Where(ch => ch is not ('.' or ':')).ToArray());
        return cleaned.Length switch
        {
            0 => "—",
            1 => cleaned.ToUpperInvariant(),
            _ => cleaned[..2].ToUpperInvariant(),
        };
    }
}
