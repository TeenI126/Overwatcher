using System.Windows;
using System.Windows.Media;

namespace OwTracker.App.Controls;

/// <summary>
/// Attached properties that clip a <see cref="FrameworkElement"/>'s corner(s) into the HUD's
/// signature notch — the CSS <c>clip-path: polygon(0 0, calc(100% - n) 0, 100% n, 100% 100%, 0 100%)</c>.
/// Set <see cref="TopRightProperty"/> (and/or <see cref="BottomLeftProperty"/>) to the notch size in
/// DIPs; the clip is recomputed on every size change.
/// </summary>
public static class NotchClip
{
    public static readonly DependencyProperty TopRightProperty = DependencyProperty.RegisterAttached(
        "TopRight", typeof(double), typeof(NotchClip),
        new PropertyMetadata(0.0, OnChanged));

    public static readonly DependencyProperty BottomLeftProperty = DependencyProperty.RegisterAttached(
        "BottomLeft", typeof(double), typeof(NotchClip),
        new PropertyMetadata(0.0, OnChanged));

    public static void SetTopRight(DependencyObject o, double v) => o.SetValue(TopRightProperty, v);
    public static double GetTopRight(DependencyObject o) => (double)o.GetValue(TopRightProperty);
    public static void SetBottomLeft(DependencyObject o, double v) => o.SetValue(BottomLeftProperty, v);
    public static double GetBottomLeft(DependencyObject o) => (double)o.GetValue(BottomLeftProperty);

    private static void OnChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not FrameworkElement fe) return;
        fe.SizeChanged -= OnSizeChanged;
        fe.SizeChanged += OnSizeChanged;
        Apply(fe);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => Apply((FrameworkElement)sender);

    private static void Apply(FrameworkElement fe)
    {
        double w = fe.ActualWidth, h = fe.ActualHeight;
        if (w <= 0 || h <= 0) { fe.Clip = null; return; }

        var tr = GetTopRight(fe);
        var bl = GetBottomLeft(fe);
        if (tr <= 0 && bl <= 0) { fe.Clip = null; return; }

        // Walk the outline clockwise from the top-left, notching the requested corners.
        var fig = new PathFigure { StartPoint = new Point(0, 0), IsClosed = true, IsFilled = true };
        if (tr > 0)
        {
            fig.Segments.Add(new LineSegment(new Point(w - tr, 0), false));
            fig.Segments.Add(new LineSegment(new Point(w, tr), false));
        }
        else
        {
            fig.Segments.Add(new LineSegment(new Point(w, 0), false));
        }
        fig.Segments.Add(new LineSegment(new Point(w, h), false));
        if (bl > 0)
        {
            fig.Segments.Add(new LineSegment(new Point(bl, h), false));
            fig.Segments.Add(new LineSegment(new Point(0, h - bl), false));
        }
        else
        {
            fig.Segments.Add(new LineSegment(new Point(0, h), false));
        }

        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        fe.Clip = geo;
    }
}
