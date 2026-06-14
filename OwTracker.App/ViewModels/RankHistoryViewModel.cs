using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using OwTracker.App.Controls;
using OwTracker.App.Theme;
using OwTracker.Core.Repositories.Interfaces;
using OwTracker.Core.Services;

namespace OwTracker.App.ViewModels;

/// <summary>A role's row on the ticker tape: its current standing plus the Δ (in ladder tiers) since
/// the previous capture.</summary>
public sealed record RankTapeVm(RankCardVm Card, bool HasDelta, string DeltaText, Brush DeltaColor);

/// <summary>
/// Backs the Rank History screen: turns the stored <c>RankSnapshot</c> history into a "stock-ticker"
/// chart (per-role ladder-score lines over time, see <see cref="RankTicker"/>) plus a ticker tape of
/// each role's latest standing and movement since the previous capture.
/// </summary>
public sealed partial class RankHistoryViewModel : ObservableObject
{
    private readonly IRankRepository _rankRepo;

    public ObservableCollection<RankTapeVm> Tape { get; } = new();

    [ObservableProperty] private RankChartModel? _chart;
    [ObservableProperty] private bool   _hasData;
    [ObservableProperty] private bool   _hasTrend;       // ≥2 captures → a line, not just a dot
    [ObservableProperty] private int    _snapshotCount;
    [ObservableProperty] private string _rangeText = "";

    public RankHistoryViewModel(IRankRepository rankRepo) => _rankRepo = rankRepo;

    public async Task RefreshAsync()
    {
        var snaps = (await _rankRepo.GetAllAsync()).OrderBy(s => s.CapturedAt).ToList(); // oldest→newest
        Tape.Clear();
        SnapshotCount = snaps.Count;
        HasData  = snaps.Count > 0;
        HasTrend = snaps.Count >= 2;
        if (!HasData) { Chart = null; RangeText = ""; return; }

        var tMin = snaps[0].CapturedAt;
        var tMax = snaps[^1].CapturedAt;
        var span = (tMax - tMin).TotalSeconds;
        double NormX(DateTime t) => span <= 0 ? 1.0 : (t - tMin).TotalSeconds / span;

        // ── Per-role lines (skip readings with no ladder score: Unknown / unplaced) ──
        var lines = new List<RankLine>();
        double? minS = null, maxS = null;
        foreach (var role in RankRoster.Roles)
        {
            var dots = new List<RankDot>();
            foreach (var s in snaps)
            {
                var r = s.Roles.FirstOrDefault(x => x.Role == role);
                if (r is null) continue;
                var score = RankRoster.Score(r.Division, r.Tier, r.RankProgress);
                if (score is null) continue;
                dots.Add(new RankDot(NormX(s.CapturedAt), score.Value));
                minS = minS is null ? score.Value : Math.Min(minS.Value, score.Value);
                maxS = maxS is null ? score.Value : Math.Max(maxS.Value, score.Value);
            }
            if (dots.Count > 0)
                lines.Add(new RankLine { Role = role, Color = RoleColor(role), Dots = dots });
        }

        // ── Auto-fit Y to the enclosing division bands ──────────────────────
        if (minS is null)
        {
            Chart = null;   // every role unplaced/unknown — nothing to plot (tape still shows status)
        }
        else
        {
            var maxDiv = RankRoster.Divisions.Count - 1;
            var lo = Math.Clamp((int)Math.Floor(minS.Value / RankRoster.TiersPerDivision), 0, maxDiv);
            var hi = Math.Clamp((int)Math.Floor(maxS!.Value / RankRoster.TiersPerDivision), 0, maxDiv);

            var bands = new List<RankBand>();
            for (var di = lo; di <= hi + 1 && di <= maxDiv; di++)
                bands.Add(new RankBand(RankRoster.DivisionFloor(di), RankRoster.Divisions[di]));

            double yMin = RankRoster.DivisionFloor(lo);
            double yMax = Math.Min(RankRoster.MaxScore, RankRoster.DivisionFloor(Math.Min(hi + 1, maxDiv)) +
                                   (hi + 1 > maxDiv ? RankRoster.TiersPerDivision : 0));
            if (yMax <= yMin) yMax = yMin + RankRoster.TiersPerDivision;

            Chart = new RankChartModel { Lines = lines, Bands = bands, YMin = yMin, YMax = yMax };
        }

        RangeText = span <= 0
            ? $"1 capture · {tMin.ToLocalTime():MMM d, h:mm tt}"
            : $"{snaps.Count} captures · {tMin.ToLocalTime():MMM d} – {tMax.ToLocalTime():MMM d}";

        // ── Ticker tape: latest standing + Δ vs the previous capture ────────
        var latest = snaps[^1];
        var prev   = snaps.Count >= 2 ? snaps[^2] : null;
        foreach (var role in RankRoster.Roles)
        {
            var r = latest.Roles.FirstOrDefault(x => x.Role == role);
            if (r is null) continue;
            var card = RankCardVm.From(r);

            var hasDelta = false;
            var deltaText = "";
            Brush deltaCol = Palette.Muted;
            var cur  = RankRoster.Score(r.Division, r.Tier, r.RankProgress);
            var pr   = prev?.Roles.FirstOrDefault(x => x.Role == role);
            var prevScore = pr is null ? null : RankRoster.Score(pr.Division, pr.Tier, pr.RankProgress);
            if (cur is not null && prevScore is not null)
            {
                var d = cur.Value - prevScore.Value;
                hasDelta  = true;
                var arrow = d > 0.01 ? "▲" : d < -0.01 ? "▼" : "—";
                // Invariant "0.0" → "0.9" (not the machine locale's "0,9").
                deltaText = $"{arrow} {Math.Abs(d).ToString("0.0", CultureInfo.InvariantCulture)}";
                deltaCol  = d > 0.01 ? Palette.RankUp : d < -0.01 ? Palette.Loss : Palette.Muted;
            }
            Tape.Add(new RankTapeVm(card, hasDelta, deltaText, deltaCol));
        }
    }

    private static Brush RoleColor(string role) =>
        role.Equals("Open Queue", StringComparison.OrdinalIgnoreCase) ? Palette.Accent : Palette.RoleBrush(role);
}
