namespace OwTracker.Core.Services.Interfaces;

public sealed record HeroInfo(string Name, string Role);

public sealed class HeroRoster
{
    public int Version { get; set; }
    public List<HeroInfo> Heroes { get; set; } = new();
}

/// <summary>
/// Supplies the canonical hero roster — the single source of truth for classifier classes
/// and the review-queue correction dropdown (design §6.5). Never hardcode hero lists elsewhere.
/// </summary>
public interface IHeroRosterProvider
{
    HeroRoster GetRoster();

    IReadOnlyList<string> GetHeroNames();
}
