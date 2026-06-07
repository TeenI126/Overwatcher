using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OwTracker.App.Converters;

/// <summary>
/// Maps <c>true → Collapsed</c> and <c>false → Visible</c> — the inverse of the built-in
/// <see cref="BooleanToVisibilityConverter"/>. Used to show placeholder / "none" text exactly
/// when the bound flag is false.
/// </summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}
