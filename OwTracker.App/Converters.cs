using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OwTracker.App;

/// <summary>Maps a bool to one of two strings: "TrueText|FalseText" passed as the parameter.</summary>
public sealed class BoolToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "Yes|No").Split('|');
        var isTrue = value is true;
        return isTrue ? parts[0] : parts.Length > 1 ? parts[1] : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps a bool to a green (true) or grey (false) brush for status indicators.</summary>
public sealed class BoolToStatusBrushConverter : IValueConverter
{
    private static readonly Brush On = new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43));
    private static readonly Brush Off = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? On : Off;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
