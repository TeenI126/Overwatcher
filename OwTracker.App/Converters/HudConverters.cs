using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using OwTracker.App.Theme;
using OwTracker.Core.Models;

namespace OwTracker.App.Converters;

/// <summary>MatchOutcome → win/loss/draw brush.</summary>
public sealed class OutcomeToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        MatchOutcome.Win  => Palette.Win,
        MatchOutcome.Loss => Palette.Loss,
        _                 => Palette.Draw,
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>MatchOutcome → VICTORY / DEFEAT / DRAW.</summary>
public sealed class OutcomeToTextConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        MatchOutcome.Win  => "VICTORY",
        MatchOutcome.Loss => "DEFEAT",
        MatchOutcome.Draw => "DRAW",
        _                 => "—",
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>MatchOutcome → single letter W / L / D.</summary>
public sealed class OutcomeToLetterConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        MatchOutcome.Win  => "W",
        MatchOutcome.Loss => "L",
        MatchOutcome.Draw => "D",
        _                 => "·",
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Role string ("tank"/"damage"/"support") → role brush (muted for unknown).</summary>
public sealed class RoleToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => Palette.RoleBrush(value as string);
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Mode string → mode brush.</summary>
public sealed class ModeToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => Palette.ModeBrush(value as string);
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Win rate (0–1 double) → diverging heatmap background brush.</summary>
public sealed class WinRateToBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var wr = value is double d ? d : 0;
        var b = new SolidColorBrush(Palette.WinRateColor(wr));
        b.Freeze();
        return b;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Win rate (0–1 double) → readable foreground (dark on bright cells, light on cool).</summary>
public sealed class WinRateToForegroundConverter : IValueConverter
{
    private static readonly Brush Dark  = new SolidColorBrush(Palette.Hex("#1a1a17"));
    private static readonly Brush Light = new SolidColorBrush(Palette.Hex("#f4ede4"));
    static WinRateToForegroundConverter() { Dark.Freeze(); Light.Freeze(); }

    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var wr = value is double d ? d : 0;
        return Palette.Luminance(Palette.WinRateColor(wr)) > 0.56 ? Dark : Light;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Win rate (0–1) → integer percent string, no sign (e.g. 0.62 → "62").</summary>
public sealed class PercentConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var wr = value is double d ? d : 0;
        var s = (int)Math.Round(wr * 100) + (string.Equals(p as string, "sign", StringComparison.Ordinal) ? "%" : "");
        return s;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>TimeSpan → "m:ss" clock (used for game length / hero playtime).</summary>
public sealed class ClockConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        if (value is not TimeSpan ts) return "0:00";
        var total = (int)ts.TotalSeconds;
        return $"{total / 60}:{total % 60:00}";
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>True → Visible, anything else → Collapsed (the built-in, but usable with any object).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => v is Visibility.Visible;
}

/// <summary>Count/empty: 0 (or null/empty) → Visible (used to show empty-state text).</summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var n = value switch { int i => i, System.Collections.ICollection col => col.Count, _ => 1 };
        return n == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Double (0–1) → star <see cref="GridLength"/> for proportional bars. Parameter "rest"
/// returns (1 − value) so two columns split a track by ratio.</summary>
public sealed class DoubleToStarConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var v = value is double d ? d : 0;
        if (string.Equals(p as string, "rest", StringComparison.Ordinal)) v = 1 - v;
        return new GridLength(Math.Max(0.0001, v), GridUnitType.Star);
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Equality test against a parameter (e.g. enum) → bool. Used for rail active state.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => string.Equals(value?.ToString(), p?.ToString(), StringComparison.Ordinal);
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => v is true && p is not null ? Enum.Parse(t, p.ToString()!) : Binding.DoNothing;
}

/// <summary>Non-null → Visible; null → Collapsed.</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Colours a scrape-log line by keyword (new record → win, duplicate → amber, error → loss,
/// completed/detected → accent, else muted body grey).</summary>
public sealed class LogLineToBrushConverter : IValueConverter
{
    private static Brush B(string hex) { var b = new SolidColorBrush(Palette.Hex(hex)); b.Freeze(); return b; }
    private static readonly Brush Body = B("#aab3bd");
    private static readonly Brush New  = B("#ff7a18");
    private static readonly Brush Dup  = B("#c7903e");
    private static readonly Brush Err  = B("#d8463a");
    private static readonly Brush Ok   = B("#ff7a18");

    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var s = (value as string ?? string.Empty).ToLowerInvariant();
        if (s.Contains("fail") || s.Contains("error") || s.Contains("warning")) return Err;
        if (s.Contains("duplicate")) return Dup;
        if (s.Contains("new record") || s.Contains("→ new")) return New;
        if (s.Contains("completed") || s.Contains("detected") || s.Contains("done") || s.Contains("ready")) return Ok;
        return Body;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Equality test against a parameter → Visibility (Visible when equal).</summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => string.Equals(value?.ToString(), p?.ToString(), StringComparison.Ordinal)
            ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
