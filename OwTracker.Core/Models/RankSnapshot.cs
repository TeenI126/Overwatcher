namespace OwTracker.Core.Models;

/// <summary>
/// The local player's competitive rank, captured once at the start of a scrape run from the
/// PLAY → COMPETITIVE → PROGRESS screen. Game reports don't show the player's rank, so this is the
/// only place to read it; the rank shifts after every competitive game (per the queued role / open
/// queue), so each scrape snapshots the current standing.
///
/// One snapshot holds the four role cards (Tank / Damage / Support / Open Queue) as child
/// <see cref="RoleRank"/> rows — mirrors the <c>MatchRecord</c> → <c>PlayerRecord</c> parent/child
/// shape and stays flexible if OW's role set changes.
/// </summary>
public class RankSnapshot
{
    public int Id { get; set; }

    /// <summary>When this snapshot was captured (UTC).</summary>
    public DateTime CapturedAt { get; set; }

    public List<RoleRank> Roles { get; set; } = new();
}
