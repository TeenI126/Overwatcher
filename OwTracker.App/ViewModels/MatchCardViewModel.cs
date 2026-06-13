using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using OwTracker.Core.Models;
using OwTracker.Core.Services;
using OwTracker.Core.Stats;

namespace OwTracker.App.ViewModels;

/// <summary>One hero the local player used in a match, with clocked time.</summary>
public sealed record HeroPlay(string Name, string Role, TimeSpan Time);

/// <summary>A scoreboard row (one player) in the expanded match detail.</summary>
public sealed record ScoreRow(
    bool IsMe, string Hero, string Role,
    int Elim, int Assist, int Death, int Dmg, int Heal, int Mit);

/// <summary>
/// Wraps a <see cref="MatchRecord"/> (loaded with players + my hero playtimes) into the display
/// shape the Stored Games card needs: hero stack, mode/rank chips, the role-appropriate key stat,
/// and — built lazily on expand — the two-team scoreboard and my-heroes strip.
/// </summary>
public sealed partial class MatchCardViewModel : ObservableObject
{
    public MatchRecord Match { get; }

    public MatchCardViewModel(MatchRecord match)
    {
        Match = match;

        Mode    = StatsService.ModeOf(match);
        RankTag = StatsService.RankTag(match);
        GameModeLabel = StatsService.GameModeLabel(match);
        IsCompetitive = StatsService.IsCompetitive(match);

        var pts = StatsService.MyPlaytimes(match);
        Heroes = pts.Count > 0
            ? pts.Select(p => new HeroPlay(p.HeroName, HeroRoster.RoleOf(p.HeroName), p.TimePlayed)).ToList()
            : (string.IsNullOrEmpty(StatsService.PrimaryHero(match))
                ? new List<HeroPlay>()
                : new List<HeroPlay> { new(StatsService.PrimaryHero(match), StatsService.PrimaryRole(match), match.GameLength) });

        PrimaryRole = StatsService.PrimaryRole(match);
        HeroStack   = Heroes.Take(2).ToList();
        ExtraHeroes = Math.Max(0, Heroes.Count - 2);

        var me = StatsService.Me(match);
        Elim = me?.Eliminations ?? 0;
        Assist = me?.Assists ?? 0;
        Death = me?.Deaths ?? 0;

        (KeyStatValue, KeyStatLabel) = PrimaryRole switch
        {
            "support" => (me?.HealingDone ?? 0, "Healing"),
            "tank"    => (me?.DamageMitigated ?? 0, "Mitigated"),
            _         => (me?.DamageDealt ?? 0, "Damage"),
        };

        RelativeTime = FormatRelative(match.MatchDatetime);
        SearchKey = (match.MapName + " " + string.Join(" ", Heroes.Select(h => h.Name))).ToLowerInvariant();
    }

    public MatchOutcome Outcome => Match.Outcome;
    public string Map => (Match.MapName ?? string.Empty).ToUpperInvariant();
    public string Mode { get; }
    public string RankTag { get; }
    public string GameModeLabel { get; }
    public bool IsCompetitive { get; }
    public string PrimaryRole { get; }
    public string ScoreText => $"{Match.MyTeamScore}–{Match.EnemyTeamScore}";
    public TimeSpan Length => Match.GameLength;
    public int? Sr => Match.SrChange;
    public bool HasSr => Match.SrChange.HasValue;
    public bool SrPositive => (Match.SrChange ?? 0) >= 0;
    public string SrText => Match.SrChange is int sr ? $"{(sr >= 0 ? "+" : "")}{sr} SR" : "—";
    public string RelativeTime { get; }
    public string SearchKey { get; }

    public IReadOnlyList<HeroPlay> Heroes { get; }
    public IReadOnlyList<HeroPlay> HeroStack { get; }
    public int ExtraHeroes { get; }
    public bool HasExtraHeroes => ExtraHeroes > 0;

    public int Elim { get; }
    public int Assist { get; }
    public int Death { get; }
    public int KeyStatValue { get; }
    public string KeyStatLabel { get; }

    /// <summary>KDA score (E+A)/max(1,D) for the performance sort.</summary>
    public double Kda => (Elim + Assist) / Math.Max(1.0, Death);

    // ── Expand / scoreboard (built lazily) ──────────────────────────────────
    [ObservableProperty] private bool _isExpanded;

    private List<ScoreRow>? _myTeam;
    private List<ScoreRow>? _enemyTeam;
    private List<HeroPlay>? _myHeroes;

    public IReadOnlyList<ScoreRow> MyTeam => _myTeam ??= BuildTeam("My Team");
    public IReadOnlyList<ScoreRow> EnemyTeam => _enemyTeam ??= BuildTeam(notTeam: "My Team");
    public IReadOnlyList<HeroPlay> MyHeroes => _myHeroes ??= Heroes.ToList();
    public bool HasMyHeroes => MyHeroes.Count > 0;

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value) return;
        OnPropertyChanged(nameof(MyTeam));
        OnPropertyChanged(nameof(EnemyTeam));
        OnPropertyChanged(nameof(MyHeroes));
        OnPropertyChanged(nameof(HasMyHeroes));
    }

    private List<ScoreRow> BuildTeam(string? team = null, string? notTeam = null)
    {
        IEnumerable<PlayerRecord> players = Match.AllPlayers;
        players = team is not null
            ? players.Where(p => p.Team == team)
            : players.Where(p => p.Team != notTeam);

        return players.Select(p =>
        {
            var hero = p.IsMe && string.IsNullOrEmpty(p.EndingHero)
                ? StatsService.PrimaryHero(Match)
                : p.EndingHero;
            var display = string.IsNullOrWhiteSpace(hero) ? "—" : hero;
            return new ScoreRow(p.IsMe, display, HeroRoster.RoleOf(hero),
                p.Eliminations, p.Assists, p.Deaths, p.DamageDealt, p.HealingDone, p.DamageMitigated);
        }).ToList();
    }

    private static string FormatRelative(DateTime dt)
    {
        var now = DateTime.Now;
        var time = dt.ToString("h:mm tt");
        if (dt.Date == now.Date) return $"Today {time}";
        if (dt.Date == now.Date.AddDays(-1)) return $"Yesterday {time}";
        return $"{dt:MMM d} · {time}";
    }
}
