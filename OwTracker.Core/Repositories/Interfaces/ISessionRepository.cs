using OwTracker.Core.Models;

namespace OwTracker.Core.Repositories.Interfaces;

public interface ISessionRepository
{
    Task<SessionRecord> AddAsync(SessionRecord record, CancellationToken ct = default);

    Task<IReadOnlyList<SessionRecord>> GetAllAsync(CancellationToken ct = default);

    Task<TimeSpan> GetTotalActiveTimeAsync(CancellationToken ct = default);
}
