using OwTracker.Core.Models;

namespace OwTracker.Core.Repositories.Interfaces;

public interface IMatchRepository
{
    /// <summary>
    /// Inserts the match, or returns the existing one if a record with the same
    /// (MapName, MatchDatetime) already exists. Deduplication per design §7.
    /// </summary>
    Task<MatchRecord> UpsertAsync(MatchRecord record, CancellationToken ct = default);

    Task<IReadOnlyList<MatchRecord>> GetAllAsync(CancellationToken ct = default);

    Task<MatchRecord?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<int> CountAsync(CancellationToken ct = default);

    /// <summary>Deletes all match records (and their cascaded players/playtimes).
    /// Returns the number of matches deleted.</summary>
    Task<int> DeleteAllAsync(CancellationToken ct = default);
}
