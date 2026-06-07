using Microsoft.EntityFrameworkCore;
using OwTracker.Core.Models;
using OwTracker.Core.Repositories.Interfaces;

namespace OwTracker.Data.Repositories;

public sealed class SessionRepository : ISessionRepository
{
    private readonly IDbContextFactory<OwTrackerDbContext> _contextFactory;

    public SessionRepository(IDbContextFactory<OwTrackerDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<SessionRecord> AddAsync(SessionRecord record, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        db.SessionRecords.Add(record);
        await db.SaveChangesAsync(ct);
        return record;
    }

    public async Task<IReadOnlyList<SessionRecord>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.SessionRecords
            .OrderByDescending(s => s.SessionStart)
            .ToListAsync(ct);
    }

    public async Task<TimeSpan> GetTotalActiveTimeAsync(CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        // TimeSpan is stored as TEXT, so aggregate client-side.
        var durations = await db.SessionRecords
            .Select(s => s.ActiveDuration)
            .ToListAsync(ct);
        return durations.Aggregate(TimeSpan.Zero, (acc, d) => acc + d);
    }
}
