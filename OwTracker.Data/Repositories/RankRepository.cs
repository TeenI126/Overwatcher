using Microsoft.EntityFrameworkCore;
using OwTracker.Core.Models;
using OwTracker.Core.Repositories.Interfaces;

namespace OwTracker.Data.Repositories;

public sealed class RankRepository : IRankRepository
{
    private readonly IDbContextFactory<OwTrackerDbContext> _contextFactory;

    public RankRepository(IDbContextFactory<OwTrackerDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<RankSnapshot> AddAsync(RankSnapshot snapshot, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        db.RankSnapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);
        return snapshot;
    }

    public async Task<RankSnapshot?> GetLatestAsync(CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.RankSnapshots
            .Include(s => s.Roles)
            .OrderByDescending(s => s.CapturedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<RankSnapshot>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.RankSnapshots
            .Include(s => s.Roles)
            .OrderByDescending(s => s.CapturedAt)
            .ToListAsync(ct);
    }
}
