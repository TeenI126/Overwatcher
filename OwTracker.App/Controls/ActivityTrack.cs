using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using OwTracker.App.Theme;
using OwTracker.Core.Models;
using OwTracker.Core.Stats;

namespace OwTracker.App.Controls;

/// <summary>
/// A session's activity timeline (the <c>.trk</c>): the OW-open window drawn as a track with faint
/// hour gridlines, each match a colored segment (won/lost/draw) at its real position. Segment
/// positions come from <see cref="ActivitySegment.LeftPct"/>/<see cref="ActivitySegment.WidthPct"/>;
/// gridlines from the open window bounds.
/// </summary>
public sealed class ActivityTrack : FrameworkElement
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IReadOnlyList<ActivitySegment>), typeof(ActivityTrack),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OpenStartProperty = DependencyProperty.Register(
        nameof(OpenStart), typeof(DateTime), typeof(ActivityTrack),
        new FrameworkPropertyMetadata(default(DateTime), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OpenEndProperty = DependencyProperty.Register(
        nameof(OpenEnd), typeof(DateTime), typeof(ActivityTrack),
        new FrameworkPropertyMetadata(default(DateTime), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<ActivitySegment>? Segments
    {
        get => (IReadOnlyList<ActivitySegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }
    public DateTime OpenStart { get => (DateTime)GetValue(OpenStartProperty); set => SetValue(OpenStartProperty, value); }
    public DateTime OpenEnd { get => (DateTime)GetValue(OpenEndProperty); set => SetValue(OpenEndProperty, value); }

    private static readonly Brush TrackBg = Freeze("#11141a");
    private static readonly Pen GridPen = new(Freeze("#1d212a"), 1);
    static ActivityTrack() { GridPen.Freeze(); }
    private static Brush Freeze(string hex) { var b = new SolidColorBrush(Palette.Hex(hex)); b.Freeze(); return b; }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.DrawRectangle(TrackBg, null, new Rect(0, 0, w, h));

        var span = (OpenEnd - OpenStart).TotalSeconds;
        if (span > 0)
        {
            // hour gridlines aligned to the clock
            var firstHour = new DateTime(OpenStart.Year, OpenStart.Month, OpenStart.Day, OpenStart.Hour, 0, 0);
            if (firstHour < OpenStart) firstHour = firstHour.AddHours(1);
            for (var t = firstHour; t <= OpenEnd; t = t.AddHours(1))
            {
                var x = (t - OpenStart).TotalSeconds / span * w;
                dc.DrawLine(GridPen, new Point(x, 0), new Point(x, h));
            }
        }

        if (Segments is null) return;
        foreach (var s in Segments)
        {
            var x = s.LeftPct / 100.0 * w;
            var sw = Math.Max(2, s.WidthPct / 100.0 * w);
            var brush = s.Outcome switch
            {
                MatchOutcome.Win  => Palette.Win,
                MatchOutcome.Loss => Palette.Loss,
                _                 => Palette.Draw,
            };
            dc.DrawRectangle(brush, null, new Rect(x, 2, sw, Math.Max(0, h - 4)));
        }
    }
}
