using OwTracker.Core.Models;

namespace OwTracker.Core.Repositories.Interfaces;

public interface IHeroLabelRepository
{
    Task<PendingHeroLabel> AddAsync(PendingHeroLabel label, CancellationToken ct = default);

    Task<IReadOnlyList<PendingHeroLabel>> GetUnreviewedAsync(CancellationToken ct = default);

    Task<int> GetUnreviewedCountAsync(CancellationToken ct = default);

    /// <summary>Marks a pending label as reviewed with the user-confirmed hero name.</summary>
    Task ConfirmAsync(int labelId, string confirmedHero, CancellationToken ct = default);
}
