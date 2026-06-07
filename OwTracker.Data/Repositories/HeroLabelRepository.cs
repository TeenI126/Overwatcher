using Microsoft.EntityFrameworkCore;
using OwTracker.Core.Models;
using OwTracker.Core.Repositories.Interfaces;

namespace OwTracker.Data.Repositories;

public sealed class HeroLabelRepository : IHeroLabelRepository
{
    private readonly IDbContextFactory<OwTrackerDbContext> _contextFactory;

    public HeroLabelRepository(IDbContextFactory<OwTrackerDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<PendingHeroLabel> AddAsync(PendingHeroLabel label, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        db.PendingHeroLabels.Add(label);
        await db.SaveChangesAsync(ct);
        return label;
    }

    public async Task<IReadOnlyList<PendingHeroLabel>> GetUnreviewedAsync(CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.PendingHeroLabels
            .Where(l => !l.Reviewed)
            .OrderBy(l => l.CapturedAt)
            .ToListAsync(ct);
    }

    public async Task<int> GetUnreviewedCountAsync(CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.PendingHeroLabels.CountAsync(l => !l.Reviewed, ct);
    }

    public async Task ConfirmAsync(int labelId, string confirmedHero, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var label = await db.PendingHeroLabels.FirstOrDefaultAsync(l => l.Id == labelId, ct);
        if (label is null)
            return;

        label.ConfirmedHero = confirmedHero;
        label.Reviewed = true;
        await db.SaveChangesAsync(ct);
    }
}
