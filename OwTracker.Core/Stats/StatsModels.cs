using OwTracker.Core.Models;

namespace OwTracker.Core.Stats;

/// <summary>Overall roll-up across every tracked match.</summary>
public sealed record OverallSummary(
    int Games, int Wins, int Losses, int Draws, double WinRate, TimeSpan Time);

/// <summary>W/L/games logged on a given calendar day.</summary>
public sealed record DaySummary(int Games, int Wins, int Losses, TimeSpan Time);

/// <summary>Current streak at the top of the (newest-first) match list.</summary>
public sealed record StreakInfo(MatchOutcome Type, int Length);

/// <summary>Per-hero aggregate over all matches (attributed by primary hero).</summary>
public sealed record HeroStat(
    string Name, string Role, int Games, int Wins, int Losses, int Draws,
    double WinRate, TimeSpan Time);

/// <summary>Per-map aggregate over all matches.</summary>
public sealed record MapStat(
    string Map, string Mode, int Games, int Wins, int Losses, int Draws,
    double WinRate, TimeSpan Total)
{
    public TimeSpan AvgLength => Games > 0 ? TimeSpan.FromSeconds(Total.TotalSeconds / Games) : TimeSpan.Zero;
}

/// <summary>One hero×map cell: record + averaged K/D/A for the ranked breakdowns.</summary>
public sealed record HeroMapCell(
    string Hero, string Map, int Games, int Wins, int Losses, int Draws,
    double WinRate, double Kda, double AvgElim, double AvgAssist, double AvgDeath);

/// <summary>A ranked row in the By-Map (heroes on a map) or By-Hero (maps for a hero) lists.</summary>
public sealed record RankedRow(
    int Rank, string Name, string Role, string Mode, int Games, int Wins, int Losses, int Draws,
    double WinRate, double Kda, double AvgElim, double AvgDeath);

/// <summary>A single match drawn on a session's activity timeline.</summary>
public sealed record ActivitySegment(
    double LeftPct, double WidthPct, MatchOutcome Outcome, string Map, string Mode,
    TimeSpan Length, DateTime Start);

/// <summary>Heroes played within a session, with clocked time.</summary>
public sealed record HeroTime(string Hero, string Role, TimeSpan Time);

/// <summary>A play session derived by grouping matches separated by long inactivity gaps.</summary>
public sealed record SessionInfo(
    int Number,
    DateTime StartTs, DateTime EndTs,
    DateTime OpenStartTs, DateTime OpenEndTs,
    TimeSpan InGame, TimeSpan Open, double ActiveRatio,
    int Games, int Wins, int Losses, int Draws, double WinRate,
    int? SrDelta, bool Comp, bool Tilt, string TiltReason,
    IReadOnlyList<MatchOutcome> Outcomes,
    IReadOnlyList<double> Series,
    IReadOnlyList<ActivitySegment> Segments,
    IReadOnlyList<HeroTime> Heroes);
