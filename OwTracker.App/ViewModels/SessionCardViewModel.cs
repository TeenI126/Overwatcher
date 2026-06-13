using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using OwTracker.App.Theme;
using OwTracker.Core.Stats;

namespace OwTracker.App.ViewModels;

/// <summary>Display wrapper around a derived <see cref="SessionInfo"/> for the Sessions cards.</summary>
public sealed class SessionCardViewModel
{
    public SessionInfo Info { get; }

    public SessionCardViewModel(SessionInfo info)
    {
        Info = info;
        DayLabel = FormatDay(info.StartTs);
        TimeRange = $"{info.StartTs:h:mm tt} – {info.EndTs:h:mm tt}";
        WinRatePercent = (int)System.Math.Round(info.WinRate * 100);
        WinRateBrush = info.WinRate > 0.5 ? Palette.Win : info.WinRate < 0.5 ? Palette.Loss : Palette.Text;
        InGameText = FormatDur(info.InGame);
        OpenText = FormatDur(info.Open);
        ActiveRatioText = $"{(int)System.Math.Round(info.ActiveRatio * 100)}% active";
        HeroesTop = info.Heroes.Take(6).ToList();
        ExtraHeroes = System.Math.Max(0, info.Heroes.Count - 6);
        SrText = info.SrDelta is int sr ? $"{(sr >= 0 ? "+" : "")}{sr} SR" : "—";
        TiltBadge = string.IsNullOrEmpty(info.TiltReason) ? "Rough patch" : $"Rough patch · {info.TiltReason}";
    }

    public int Number => Info.Number;
    public string DayLabel { get; }
    public string TimeRange { get; }
    public int WinRatePercent { get; }
    public Brush WinRateBrush { get; }
    public int Wins => Info.Wins;
    public int Losses => Info.Losses;
    public int Draws => Info.Draws;
    public bool HasDraws => Info.Draws > 0;
    public int Games => Info.Games;
    public string GamesText => $"{Info.Games} games";
    public bool Comp => Info.Comp;
    public bool HasSr => Info.SrDelta.HasValue;
    public bool SrPositive => (Info.SrDelta ?? 0) >= 0;
    public string SrText { get; }
    public bool Tilt => Info.Tilt;
    public string TiltBadge { get; }

    public string InGameText { get; }
    public string OpenText { get; }
    public double ActiveRatio => Info.ActiveRatio;
    public string ActiveRatioText { get; }

    public IEnumerable<double> Series => Info.Series;
    public IReadOnlyList<ActivitySegment> Segments => Info.Segments;
    public System.DateTime OpenStart => Info.OpenStartTs;
    public System.DateTime OpenEnd => Info.OpenEndTs;

    public IReadOnlyList<HeroTime> HeroesTop { get; }
    public int ExtraHeroes { get; }
    public bool HasExtraHeroes => ExtraHeroes > 0;

    private static string FormatDur(System.TimeSpan t)
    {
        var h = (int)t.TotalHours;
        var m = (int)System.Math.Round((t.TotalSeconds - h * 3600) / 60);
        return h > 0 ? $"{h}h {m}m" : $"{m}m";
    }

    private static string FormatDay(System.DateTime dt)
    {
        var today = System.DateTime.Now.Date;
        if (dt.Date == today) return "Today";
        if (dt.Date == today.AddDays(-1)) return "Yesterday";
        return dt.ToString("ddd, MMM d");
    }
}
