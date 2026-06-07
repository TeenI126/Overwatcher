namespace OwTracker.Core.Models;

/// <summary>
/// Time the local user spent on a single hero within a match (from the Personal tab).
/// </summary>
public class HeroPlaytime
{
    public int Id { get; set; }
    public int PlayerRecordId { get; set; }
    public string HeroName { get; set; } = string.Empty;
    public TimeSpan TimePlayed { get; set; }

    // Navigation
    public PlayerRecord? Player { get; set; }
}
