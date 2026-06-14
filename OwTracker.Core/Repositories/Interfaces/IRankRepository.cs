using OwTracker.Core.Models;

namespace OwTracker.Core.Repositories.Interfaces;

/// <summary>Persists competitive-rank snapshots captured at the start of each scrape run.</summary>
public interface IRankRepository
{
    /// <summary>Inserts a snapshot (with its per-role children) and returns it.</summary>
    Task<RankSnapshot> AddAsync(RankSnapshot snapshot, CancellationToken ct = default);

    /// <summary>The most recently captured snapshot (with roles), or null if none exist.</summary>
    Task<RankSnapshot?> GetLatestAsync(CancellationToken ct = default);

    /// <summary>All snapshots (with roles), newest first.</summary>
    Task<IReadOnlyList<RankSnapshot>> GetAllAsync(CancellationToken ct = default);
}
