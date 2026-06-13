using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using OwTracker.App.Theme;

namespace OwTracker.App.Controls;

/// <summary>
/// Tiny cumulative win-rate sparkline (the session card's <c>.spark</c>): a dashed 50% baseline,
/// a polyline through the series (0–1 values), and a dot on the final point. Line colour is win
/// orange when the series ends ≥ 50%, else loss red.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IEnumerable<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<double>? Series
    {
        get => (IEnumerable<double>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    private static readonly Pen BaselinePen = MakeBaseline();
    private static Pen MakeBaseline()
    {
        var p = new Pen(new SolidColorBrush(Palette.Hex("#1d212a")), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
        p.Freeze();
        return p;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var data = Series?.ToList();
        double w = ActualWidth, h = ActualHeight;
        if (data is null || data.Count == 0 || w <= 0 || h <= 0) return;

        double Y(double v) => h - 3 - v * (h - 6);
        double X(int i) => data.Count == 1 ? w / 2 : (double)i / (data.Count - 1) * w;

        dc.DrawLine(BaselinePen, new Point(0, Y(0.5)), new Point(w, Y(0.5)));

        var last = data[^1];
        var col = last >= 0.5 ? Palette.Win : Palette.Loss;
        var pen = new Pen(col, 1.8) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(X(0), Y(data[0])), false, false);
            for (var i = 1; i < data.Count; i++)
                ctx.LineTo(new Point(X(i), Y(data[i])), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
        dc.DrawEllipse(col, null, new Point(X(data.Count - 1), Y(last)), 2.6, 2.6);
    }
}
