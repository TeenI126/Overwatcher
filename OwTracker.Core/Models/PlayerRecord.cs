namespace OwTracker.Core.Models;

/// <summary>
/// One player's stats within a match. For the local user (<see cref="IsMe"/> == true),
/// <see cref="HeroPlaytimes"/> is populated from the Personal tab; otherwise it is empty.
/// </summary>
public class PlayerRecord
{
    public int Id { get; set; }
    public int MatchRecordId { get; set; }
    public bool IsMe { get; set; }
    public string Team { get; set; } = string.Empty;       // "My Team" | "Enemy Team"

    // Teams tab — all players
    public string EndingHero { get; set; } = string.Empty; // Hero they ended the game on
    public int Eliminations { get; set; }
    public int Assists { get; set; }
    public int Deaths { get; set; }
    public int DamageDealt { get; set; }
    public int HealingDone { get; set; }
    public int DamageMitigated { get; set; }

    // Personal tab — my record only (IsMe == true)
    public List<HeroPlaytime> HeroPlaytimes { get; set; } = new();

    // Navigation
    public MatchRecord? Match { get; set; }
}
