using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using OwTracker.App.Theme;

namespace OwTracker.App.Controls;

/// <summary>One plotted rank reading: <paramref name="X"/> is normalised time (0 = oldest capture,
/// 1 = newest); <paramref name="Score"/> is the ladder score from <c>RankRoster.Score</c>.</summary>
public sealed record RankDot(double X, double Score);

/// <summary>One role's line on the ticker (a colour + its time-ordered points).</summary>
public sealed class RankLine
{
    public string Role { get; init; } = "";
    public Brush Color { get; init; } = Brushes.Gray;
    public IReadOnlyList<RankDot> Dots { get; init; } = Array.Empty<RankDot>();
}

/// <summary>A horizontal division gridline + its label, at a ladder score.</summary>
public sealed record RankBand(double Score, string Label);

/// <summary>Everything <see cref="RankTicker"/> needs to draw: the role lines, the visible Y range
/// (auto-fit to the data, snapped to division bands), and the division gridlines.</summary>
public sealed class RankChartModel
{
    public IReadOnlyList<RankLine> Lines { get; init; } = Array.Empty<RankLine>();
    public IReadOnlyList<RankBand> Bands { get; init; } = Array.Empty<RankBand>();
    public double YMin { get; init; }
    public double YMax { get; init; }
}

/// <summary>
/// A "stock-ticker" rank chart: time on X, the competitive ladder on Y (division bands labelled and
/// gridded), one colour-coded line per role with a dot per capture and an emphasised latest dot.
/// Auto-fits Y to the data's division range so the player's actual band fills the view. Custom
/// <see cref="OnRender"/> drawing, matching <see cref="Sparkline"/>/<see cref="ActivityTrack"/>.
/// </summary>
public sealed class RankTicker : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(RankChartModel), typeof(RankTicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public RankChartModel? Model
    {
        get => (RankChartModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private const double PadL = 84, PadR = 16, PadT = 12, PadB = 16;

    private static readonly Brush Bg       = Freeze("#0b0d11");
    private static readonly Pen   BandPen  = FreezePen("#171b22", 1);
    private static readonly Pen   FloorPen = FreezePen("#222732", 1);
    private static readonly Brush LabelBr  = Palette.Muted;

    private static Brush Freeze(string hex) { var b = new SolidColorBrush(Palette.Hex(hex)); b.Freeze(); return b; }
    private static Pen FreezePen(string hex, double t) { var p = new Pen(Freeze(hex), t); p.Freeze(); return p; }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.DrawRectangle(Bg, null, new Rect(0, 0, w, h));

        var m = Model;
        if (m is null || m.YMax <= m.YMin) return;

        double plotL = PadL, plotR = w - PadR, plotT = PadT, plotB = h - PadB;
        double plotW = Math.Max(1, plotR - plotL), plotH = Math.Max(1, plotB - plotT);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        double X(double x) => plotL + x * plotW;
        double Y(double s) => plotT + (m.YMax - s) / (m.YMax - m.YMin) * plotH;

        // ── Division gridlines + labels ────────────────────────────────────
        foreach (var band in m.Bands)
        {
            if (band.Score < m.YMin || band.Score > m.YMax) continue;
            var y = Y(band.Score);
            dc.DrawLine(BandPen, new Point(plotL, y), new Point(plotR, y));
            var ft = Text(band.Label, 10.5, LabelBr, dpi);
            dc.DrawText(ft, new Point(plotL - 10 - ft.Width, y - ft.Height / 2));
        }
        // Plot frame left edge.
        dc.DrawLine(FloorPen, new Point(plotL, plotT), new Point(plotL, plotB));

        // ── Role lines ─────────────────────────────────────────────────────
        foreach (var line in m.Lines)
        {
            if (line.Dots.Count == 0) continue;
            var pen = new Pen(line.Color, 2.0)
            {
                LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round
            };
            pen.Freeze();

            if (line.Dots.Count > 1)
            {
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(X(line.Dots[0].X), Y(line.Dots[0].Score)), false, false);
                    for (var i = 1; i < line.Dots.Count; i++)
                        ctx.LineTo(new Point(X(line.Dots[i].X), Y(line.Dots[i].Score)), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
            }

            // Intermediate dots small; latest dot emphasised (ringed).
            for (var i = 0; i < line.Dots.Count; i++)
            {
                var p = new Point(X(line.Dots[i].X), Y(line.Dots[i].Score));
                if (i == line.Dots.Count - 1)
                {
                    dc.DrawEllipse(Bg, pen, p, 4.0, 4.0);
                    dc.DrawEllipse(line.Color, null, p, 2.2, 2.2);
                }
                else
                {
                    dc.DrawEllipse(line.Color, null, p, 2.2, 2.2);
                }
            }
        }
    }

    private static FormattedText Text(string s, double size, Brush brush, double dpi) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush, dpi);
}
