using Microsoft.EntityFrameworkCore;
using OwTracker.Core.Models;
using OwTracker.Core.Repositories.Interfaces;

namespace OwTracker.Data.Repositories;

public sealed class MatchRepository : IMatchRepository
{
    private readonly IDbContextFactory<OwTrackerDbContext> _contextFactory;

    public MatchRepository(IDbContextFactory<OwTrackerDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<MatchRecord> UpsertAsync(MatchRecord record, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var existing = await db.MatchRecords
            .FirstOrDefaultAsync(
                m => m.MapName == record.MapName && m.MatchDatetime == record.MatchDatetime,
                ct);

        if (existing is not null)
            return existing;

        db.MatchRecords.Add(record);
        await db.SaveChangesAsync(ct);
        return record;
    }

    public async Task<IReadOnlyList<MatchRecord>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.MatchRecords
            .Include(m => m.AllPlayers)
            .OrderByDescending(m => m.MatchDatetime)
            .ToListAsync(ct);
    }

    public async Task<MatchRecord?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.MatchRecords
            .Include(m => m.AllPlayers)
            .ThenInclude(p => p.HeroPlaytimes)
            .FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.MatchRecords.CountAsync(ct);
    }

    public async Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        // Load with children so cascade delete removes players + playtimes too.
        var all = await db.MatchRecords.Include(m => m.AllPlayers).ToListAsync(ct);
        db.MatchRecords.RemoveRange(all);
        await db.SaveChangesAsync(ct);
        return all.Count;
    }
}
