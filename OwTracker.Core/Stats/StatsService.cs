using System.Linq;
using OwTracker.Core.Models;
using OwTracker.Core.Services;

namespace OwTracker.Core.Stats;

/// <summary>
/// Pure aggregation over scraped matches — the data behind the Dashboard overview, the Hero×Map
/// analytics screen, and the derived Sessions screen. No EF/DB or presentation dependency: callers
/// load matches (with details) from the repository and hand the list in. Mirrors the prototype's
/// <c>data.js</c> derivations so win rates reconcile across screens.
///
/// A match is attributed to a single <b>primary hero</b> — the hero the local player spent the most
/// time on that match (falling back to the IsMe player's ending hero). Mode/type comes from
/// <see cref="MapRoster"/>; role from <see cref="HeroRoster"/>.
/// </summary>
public static class StatsService
{
    // ── Per-match derivations (shared with the card view-models) ──────────────────────────────

    /// <summary>The local player's record for a match (the IsMe player), or null.</summary>
    public static PlayerRecord? Me(MatchRecord m) =>
        m.MyStats ?? m.AllPlayers.FirstOrDefault(p => p.IsMe);

    /// <summary>My per-hero playtimes for a match, longest first (empty if not captured).</summary>
    public static IReadOnlyList<HeroPlaytime> MyPlaytimes(MatchRecord m)
    {
        var me = Me(m);
        if (me is null) return Array.Empty<HeroPlaytime>();
        return me.HeroPlaytimes.OrderByDescending(h => h.TimePlayed).ToList();
    }

    /// <summary>The hero this match is attributed to (most-played, else ending hero, else "").</summary>
    public static string PrimaryHero(MatchRecord m)
    {
        var pts = MyPlaytimes(m);
        if (pts.Count > 0) return pts[0].HeroName;
        return Me(m)?.EndingHero ?? string.Empty;
    }

    public static string PrimaryRole(MatchRecord m) => HeroRoster.RoleOf(PrimaryHero(m));

    /// <summary>Canonical objective mode ("Control"/"Hybrid"/…) for a match.</summary>
    public static string ModeOf(MatchRecord m) => MapRoster.ResolveMode(m.MapName, m.GameMode);

    /// <summary>Coarse rank tag for the card chip: COMPETITIVE / UNRANKED / QUICK PLAY.</summary>
    public static string RankTag(MatchRecord m)
    {
        var r = (m.RankingMode ?? string.Empty).ToUpperInvariant();
        if (r.Contains("COMP")) return "COMPETITIVE";
        if (r.Contains("STADI")) return "STADIUM";
        if (r.Contains("UNRANK")) return "UNRANKED";
        return "QUICK PLAY";
    }

    public static bool IsCompetitive(MatchRecord m) =>
        (m.RankingMode ?? string.Empty).ToUpperInvariant().Contains("COMP");

    public static bool IsStadium(MatchRecord m) =>
        (m.RankingMode ?? string.Empty).ToUpperInvariant().Contains("STADI");

    /// <summary>Game-mode label combining ranking tier and queue (matches the Mode filter).</summary>
    public static string GameModeLabel(MatchRecord m)
    {
        // Stadium is its own game mode (no role/open-queue split, and not Quick Play) — check it
        // before the comp/quick fallbacks so its matches bucket as "Stadium" rather than collapsing
        // into "Quick Play". Its QueueType OCR is unreliable bleed from the adjacent list row anyway.
        if (IsStadium(m)) return "Stadium";
        if (IsCompetitive(m))
            return (m.QueueType ?? string.Empty).ToUpperInvariant().Contains("OPEN")
                ? "Comp · Open Queue" : "Comp · Role Queue";
        return RankTag(m) == "UNRANKED" ? "Unranked" : "Quick Play";
    }

    /// <summary>The game-mode labels, in display order (for the Mode dropdown).</summary>
    public static readonly IReadOnlyList<string> GameModes = new[]
    {
        "Comp · Role Queue", "Comp · Open Queue", "Stadium", "Unranked", "Quick Play",
    };

    private static bool IsWin(MatchRecord m) => m.Outcome == MatchOutcome.Win;
    private static bool IsLoss(MatchRecord m) => m.Outcome == MatchOutcome.Loss;

    // ── Overall / today / streak / recent form ────────────────────────────────────────────────

    public static OverallSummary Overall(IReadOnlyList<MatchRecord> matches)
    {
        int w = 0, l = 0, d = 0; var time = TimeSpan.Zero;
        foreach (var m in matches)
        {
            if (IsWin(m)) w++; else if (IsLoss(m)) l++; else d++;
            time += m.GameLength;
        }
        var g = matches.Count;
        return new OverallSummary(g, w, l, d, g > 0 ? (double)w / g : 0, time);
    }

    public static DaySummary Today(IReadOnlyList<MatchRecord> matches, DateTime now)
    {
        int g = 0, w = 0, l = 0; var time = TimeSpan.Zero;
        foreach (var m in matches)
        {
            if (m.MatchDatetime.Date != now.Date) continue;
            g++; time += m.GameLength;
            if (IsWin(m)) w++; else if (IsLoss(m)) l++;
        }
        return new DaySummary(g, w, l, time);
    }

    /// <summary>Outcomes of the most-recent <paramref name="n"/> matches (newest first).</summary>
    public static IReadOnlyList<MatchOutcome> RecentForm(IReadOnlyList<MatchRecord> matches, int n) =>
        matches.OrderByDescending(m => m.MatchDatetime).Take(n).Select(m => m.Outcome).ToList();

    /// <summary>The streak at the head of the newest-first list.</summary>
    public static StreakInfo CurrentStreak(IReadOnlyList<MatchRecord> matches)
    {
        var ordered = matches.OrderByDescending(m => m.MatchDatetime).ToList();
        if (ordered.Count == 0) return new StreakInfo(MatchOutcome.Unknown, 0);
        var first = ordered[0].Outcome;
        if (first is MatchOutcome.Draw or MatchOutcome.Unknown) return new StreakInfo(first, 1);
        var len = 0;
        foreach (var m in ordered) { if (m.Outcome == first) len++; else break; }
        return new StreakInfo(first, len);
    }

    // ── Hero / map totals ───────────────────────────────────────────────────────────────────

    public static IReadOnlyList<HeroStat> HeroTotals(IReadOnlyList<MatchRecord> matches)
    {
        var acc = new Dictionary<string, (int g, int w, int l, int d, double sec)>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in matches)
        {
            var hero = PrimaryHero(m);
            if (string.IsNullOrEmpty(hero)) continue;
            acc.TryGetValue(hero, out var a);
            a.g++; a.sec += m.GameLength.TotalSeconds;
            if (IsWin(m)) a.w++; else if (IsLoss(m)) a.l++; else a.d++;
            acc[hero] = a;
        }
        return acc.Select(kv => new HeroStat(
                kv.Key, HeroRoster.RoleOf(kv.Key), kv.Value.g, kv.Value.w, kv.Value.l, kv.Value.d,
                kv.Value.g > 0 ? (double)kv.Value.w / kv.Value.g : 0,
                TimeSpan.FromSeconds(kv.Value.sec)))
            .OrderByDescending(h => h.Games).ThenBy(h => h.Name)
            .ToList();
    }

    public static IReadOnlyList<MapStat> MapTotals(IReadOnlyList<MatchRecord> matches)
    {
        var acc = new Dictionary<string, (int g, int w, int l, int d, double sec, string mode)>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in matches)
        {
            if (string.IsNullOrEmpty(m.MapName)) continue;
            acc.TryGetValue(m.MapName, out var a);
            a.g++; a.sec += m.GameLength.TotalSeconds; a.mode = ModeOf(m);
            if (IsWin(m)) a.w++; else if (IsLoss(m)) a.l++; else a.d++;
            acc[m.MapName] = a;
        }
        return acc.Select(kv => new MapStat(
                kv.Key, kv.Value.mode, kv.Value.g, kv.Value.w, kv.Value.l, kv.Value.d,
                kv.Value.g > 0 ? (double)kv.Value.w / kv.Value.g : 0,
                TimeSpan.FromSeconds(kv.Value.sec)))
            .OrderByDescending(m => m.Games).ThenBy(m => m.Map)
            .ToList();
    }

    public static HeroStat? TopHero(IReadOnlyList<MatchRecord> matches, int minGames) =>
        HeroTotals(matches).Where(h => h.Games >= minGames)
            .OrderByDescending(h => h.WinRate).ThenByDescending(h => h.Games).FirstOrDefault();

    public static MapStat? TopMap(IReadOnlyList<MatchRecord> matches, int minGames) =>
        MapTotals(matches).Where(m => m.Games >= minGames)
            .OrderByDescending(m => m.WinRate).ThenByDescending(m => m.Games).FirstOrDefault();

    public static HeroStat? MostPlayedHero(IReadOnlyList<MatchRecord> matches) =>
        HeroTotals(matches).OrderByDescending(h => h.Time).FirstOrDefault();

    // ── Hero × map matrix ─────────────────────────────────────────────────────────────────────

    public static Dictionary<(string Hero, string Map), HeroMapCell> BuildMatrix(IReadOnlyList<MatchRecord> matches)
    {
        var raw = new Dictionary<(string, string), (int g, int w, int l, int d, long e, long a, long dd)>();
        foreach (var m in matches)
        {
            var hero = PrimaryHero(m);
            if (string.IsNullOrEmpty(hero) || string.IsNullOrEmpty(m.MapName)) continue;
            var key = (hero, m.MapName);
            raw.TryGetValue(key, out var c);
            c.g++;
            if (IsWin(m)) c.w++; else if (IsLoss(m)) c.l++; else c.d++;
            var me = Me(m);
            if (me is not null) { c.e += me.Eliminations; c.a += me.Assists; c.dd += me.Deaths; }
            raw[key] = c;
        }
        return raw.ToDictionary(
            kv => (kv.Key.Item1, kv.Key.Item2),
            kv =>
            {
                var (g, w, l, d, e, a, dd) = kv.Value;
                return new HeroMapCell(kv.Key.Item1, kv.Key.Item2, g, w, l, d,
                    g > 0 ? (double)w / g : 0,
                    (e + a) / Math.Max(1.0, dd),
                    (double)e / Math.Max(1, g), (double)a / Math.Max(1, g), (double)dd / Math.Max(1, g));
            });
    }

    /// <summary>Heroes played on a given map, ranked by games then win rate.</summary>
    public static IReadOnlyList<RankedRow> MapHeroes(IReadOnlyList<MatchRecord> matches, string map)
    {
        var matrix = BuildMatrix(matches);
        return matrix.Values.Where(c => string.Equals(c.Map, map, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Games).ThenByDescending(c => c.WinRate)
            .Select((c, i) => new RankedRow(i + 1, c.Hero, HeroRoster.RoleOf(c.Hero), string.Empty,
                c.Games, c.Wins, c.Losses, c.Draws, c.WinRate, c.Kda, c.AvgElim, c.AvgDeath))
            .ToList();
    }

    /// <summary>Maps played with a given hero, ranked by games then win rate.</summary>
    public static IReadOnlyList<RankedRow> HeroMaps(IReadOnlyList<MatchRecord> matches, string hero)
    {
        var matrix = BuildMatrix(matches);
        return matrix.Values.Where(c => string.Equals(c.Hero, hero, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Games).ThenByDescending(c => c.WinRate)
            .Select((c, i) => new RankedRow(i + 1, c.Map, string.Empty, MapRoster.ModeOf(c.Map),
                c.Games, c.Wins, c.Losses, c.Draws, c.WinRate, c.Kda, c.AvgElim, c.AvgDeath))
            .ToList();
    }

    // ── Sessions (derived by gap-grouping) ─────────────────────────────────────────────────────

    /// <summary>
    /// Groups matches into sessions: a new session starts after a gap of <paramref name="gapMinutes"/>+
    /// minutes between the end of one match and the start of the next. Returns newest session first.
    /// </summary>
    public static IReadOnlyList<SessionInfo> BuildSessions(IReadOnlyList<MatchRecord> matches, int gapMinutes = 60)
    {
        var gap = TimeSpan.FromMinutes(gapMinutes);
        var chrono = matches.OrderBy(m => m.MatchDatetime).ToList();
        var groups = new List<List<MatchRecord>>();
        DateTime curEnd = default;
        foreach (var m in chrono)
        {
            var start = m.MatchDatetime;
            var end = m.MatchDatetime + m.GameLength;
            if (groups.Count == 0 || start - curEnd > gap)
                groups.Add(new List<MatchRecord>());
            groups[^1].Add(m);
            curEnd = end;
        }
        var total = groups.Count;
        var result = new List<SessionInfo>(total);
        for (var i = 0; i < total; i++)
            result.Add(Summarize(groups[i], total - i)); // number 1 = oldest
        result.Reverse(); // newest first
        return result;
    }

    private static SessionInfo Summarize(List<MatchRecord> games, int number)
    {
        int w = 0, l = 0, d = 0; var inGame = TimeSpan.Zero;
        int srSum = 0; var hasSr = false; var comp = false;
        var heroSec = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in games)
        {
            if (IsWin(m)) w++; else if (IsLoss(m)) l++; else d++;
            inGame += m.GameLength;
            if (IsCompetitive(m)) { comp = true; if (m.SrChange is int sr) { srSum += sr; hasSr = true; } }

            var pts = MyPlaytimes(m);
            if (pts.Count > 0)
                foreach (var pt in pts)
                    heroSec[pt.HeroName] = heroSec.GetValueOrDefault(pt.HeroName) + pt.TimePlayed.TotalSeconds;
            else
            {
                var hero = PrimaryHero(m);
                if (!string.IsNullOrEmpty(hero))
                    heroSec[hero] = heroSec.GetValueOrDefault(hero) + m.GameLength.TotalSeconds;
            }
        }

        var outcomes = games.Select(m => m.Outcome).ToList();

        // cumulative win-rate series
        var series = new List<double>(games.Count);
        var cw = 0;
        for (var i = 0; i < outcomes.Count; i++)
        {
            if (outcomes[i] == MatchOutcome.Win) cw++;
            series.Add((double)cw / (i + 1));
        }

        // tilt: 3+ loss skid, or win rate sags in the back half of a long session
        var maxLoss = 0; var run = 0;
        foreach (var o in outcomes) { if (o == MatchOutcome.Loss) { run++; maxLoss = Math.Max(maxLoss, run); } else run = 0; }
        var tilt = false; var tiltReason = string.Empty;
        if (maxLoss >= 3) { tilt = true; tiltReason = $"{maxLoss}-loss skid"; }
        else if (games.Count >= 6)
        {
            var half = games.Count / 2;
            var wr1 = outcomes.Take(half).Count(o => o == MatchOutcome.Win) / (double)half;
            var wr2 = outcomes.Skip(half).Count(o => o == MatchOutcome.Win) / (double)(games.Count - half);
            if (wr2 < wr1 - 0.34) { tilt = true; tiltReason = "win rate dropped"; }
        }

        var startTs = games[0].MatchDatetime;
        var endTs = games[^1].MatchDatetime + games[^1].GameLength;
        // Open window derived from matches alone: the span we actually observed games over.
        var openStartTs = startTs;
        var openEndTs = endTs;
        var openSpan = openEndTs - openStartTs;
        if (openSpan <= TimeSpan.Zero) openSpan = inGame;
        var open = openSpan;
        var activeRatio = open.TotalSeconds > 0 ? inGame.TotalSeconds / open.TotalSeconds : 0;

        var span = (openEndTs - openStartTs).TotalSeconds;
        if (span <= 0) span = Math.Max(1, inGame.TotalSeconds);
        var segs = games.Select(m => new ActivitySegment(
            ((m.MatchDatetime - openStartTs).TotalSeconds / span) * 100,
            (m.GameLength.TotalSeconds / span) * 100,
            m.Outcome, m.MapName, ModeOf(m), m.GameLength, m.MatchDatetime)).ToList();

        var heroes = heroSec.Select(kv => new HeroTime(kv.Key, HeroRoster.RoleOf(kv.Key), TimeSpan.FromSeconds(kv.Value)))
            .OrderByDescending(h => h.Time).ToList();

        var g = games.Count;
        return new SessionInfo(
            number, startTs, endTs, openStartTs, openEndTs,
            inGame, open, activeRatio,
            g, w, l, d, g > 0 ? (double)w / g : 0,
            hasSr ? srSum : null, comp, tilt, tiltReason,
            outcomes, series, segs, heroes);
    }
}
