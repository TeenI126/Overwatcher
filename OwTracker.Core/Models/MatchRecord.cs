namespace OwTracker.Core.Models;

/// <summary>
/// A single scraped Overwatch match. Deduplicated on (MapName, MatchDatetime).
/// </summary>
public class MatchRecord
{
    public int Id { get; set; }
    public string MapName { get; set; } = string.Empty;
    public DateTime MatchDatetime { get; set; }
    public TimeSpan GameLength { get; set; }
    public int MyTeamScore { get; set; }
    public int EnemyTeamScore { get; set; }
    public MatchOutcome Outcome { get; set; } = MatchOutcome.Unknown;

    /// <summary>Map/objective type: ESCORT, CONTROL, PUSH, FLASHPOINT, HYBRID.</summary>
    public string GameMode { get; set; } = string.Empty;

    /// <summary>Competitive tier: COMPETITIVE, UNRANKED, ARCADE, STADIUM.</summary>
    public string RankingMode { get; set; } = string.Empty;

    /// <summary>Queue format: ROLE QUEUE, OPEN QUEUE, QUICK PLAY.</summary>
    public string QueueType { get; set; } = string.Empty;

    /// <summary>
    /// Competitive SR / rank change for this match (e.g. +24 / −18), or null when not captured.
    /// Not yet populated by the scraper — the report screen does display it, so OCR can fill this
    /// later; the UI renders "—" while null.
    /// </summary>
    public int? SrChange { get; set; }

    public DateTime ScrapedAt { get; set; }

    // Navigation
    public PlayerRecord? MyStats { get; set; }
    public List<PlayerRecord> AllPlayers { get; set; } = new();
}

public enum MatchOutcome
{
    Win,
    Loss,
    Draw,
    Unknown
}
