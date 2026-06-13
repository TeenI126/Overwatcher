using OwTracker.Core.Models;

namespace OwTracker.Core.Repositories.Interfaces;

public interface IMatchRepository
{
    /// <summary>
    /// Inserts the match, or returns the existing one if a record with the same
    /// (MapName, MatchDatetime) already exists. Deduplication per design §7.
    /// When <paramref name="overwrite"/> is true, an existing row (and its child players/playtimes)
    /// is REPLACED with the freshly-scraped data — used by a deep back-fill to correct records
    /// re-read with improved OCR. The return is the same instance as <paramref name="record"/> only
    /// on insert; a different instance means it already existed (whether or not it was overwritten).
    /// </summary>
    Task<MatchRecord> UpsertAsync(MatchRecord record, bool overwrite = false, CancellationToken ct = default);

    Task<IReadOnlyList<MatchRecord>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// All matches eagerly loaded with every player and the local player's per-hero playtimes.
    /// Used by the card list / stats / sessions screens, which need hero playtimes (primary-hero
    /// attribution, hero stacks) that <see cref="GetAllAsync"/> deliberately omits.
    /// </summary>
    Task<IReadOnlyList<MatchRecord>> GetAllWithDetailsAsync(CancellationToken ct = default);

    Task<MatchRecord?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<int> CountAsync(CancellationToken ct = default);

    /// <summary>Deletes all match records (and their cascaded players/playtimes).
    /// Returns the number of matches deleted.</summary>
    Task<int> DeleteAllAsync(CancellationToken ct = default);
}
