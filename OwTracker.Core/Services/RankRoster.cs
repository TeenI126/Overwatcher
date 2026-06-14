using System.Text.RegularExpressions;

namespace OwTracker.Core.Services;

/// <summary>
/// The Overwatch competitive division ladder (Bronze → Champion) plus the role set, used to snap a
/// noisy OCR read of a rank card on the COMPETITIVE PROGRESS screen to canonical values. Mirrors
/// <see cref="MapRoster"/>/<see cref="HeroRoster"/>: keep current as OW's division/role set changes.
/// </summary>
public static class RankRoster
{
    /// <summary>Divisions low → high. Each has tiers 5 (lowest) → 1 (highest).</summary>
    public static readonly IReadOnlyList<string> Divisions = new[]
    {
        "Bronze", "Silver", "Gold", "Platinum", "Diamond", "Master", "Grandmaster", "Champion",
    };

    /// <summary>The role cards on the COMPETITIVE PROGRESS screen, in left→right card order.</summary>
    public static readonly IReadOnlyList<string> Roles = new[]
    {
        "Tank", "Damage", "Support", "Open Queue",
    };

    /// <summary>
    /// Snaps an upper-cased OCR fragment to a canonical division name, tolerant of the usual glyph
    /// drift, or null if none matches. GRANDMASTER is tested before MASTER (it contains "MASTER").
    /// </summary>
    public static string? SnapDivision(string raw)
    {
        var up = raw.ToUpperInvariant();
        if (up.Contains("BRONZE") || up.Contains("RONZE"))                       return "Bronze";
        if (up.Contains("SILVER") || up.Contains("ILVER"))                       return "Silver";
        if (up.Contains("PLATIN") || up.Contains("LATIN"))                       return "Platinum";
        if (up.Contains("DIAMON") || up.Contains("IAMON"))                       return "Diamond";
        if (up.Contains("GRANDM") || up.Contains("RANDMA") || up.Contains("GRAND")) return "Grandmaster";
        if (up.Contains("MASTER") || up.Contains("ASTER"))                       return "Master";
        if (up.Contains("CHAMPI") || up.Contains("HAMPIO"))                      return "Champion";
        if (up.Contains("GOLD")   || up.Contains("G0LD"))                        return "Gold";
        return null;
    }

    /// <summary>True if <paramref name="division"/> is Master or above (shows a Challenger Score).</summary>
    public static bool IsChallengerTier(string division) =>
        division is "Master" or "Grandmaster" or "Champion";

    /// <summary>Tiers within each division (5 = lowest .. 1 = highest).</summary>
    public const int TiersPerDivision = 5;

    /// <summary>Index of <paramref name="division"/> in the ladder (Bronze=0 … Champion=7), or −1.</summary>
    public static int DivisionIndex(string division)
    {
        for (var i = 0; i < Divisions.Count; i++)
            if (string.Equals(Divisions[i], division, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>
    /// Maps a rank to a single monotonic "ladder score" for charting. Each tier is 1.0 unit
    /// (Bronze 5 = 0 … Bronze 1 = 4 … Diamond 1 = 24 … Champion 1 = 39) and the within-tier rank
    /// progress % is the fractional offset on top — so a rising rank is a rising number, perfect for
    /// a line/ticker chart. Returns null for an unrankable card (Unknown / unplaced division), which
    /// has no position on the ladder.
    /// </summary>
    public static double? Score(string division, int tier, int? progress)
    {
        var di = DivisionIndex(division);
        if (di < 0) return null;
        var t    = Math.Clamp(tier, 1, TiersPerDivision);
        var step = di * TiersPerDivision + (TiersPerDivision - t);          // 0..39, low→high
        var frac = (progress.HasValue ? Math.Clamp(progress.Value, -50, 100) : 0) / 100.0;
        return step + frac;
    }

    /// <summary>Ladder score at the BOTTOM (tier 5, 0%) of a division band — its Y-axis gridline.</summary>
    public static double DivisionFloor(int divisionIndex) => divisionIndex * TiersPerDivision;

    /// <summary>Highest possible ladder score (Champion 1 at 100%).</summary>
    public static double MaxScore => (Divisions.Count - 1) * TiersPerDivision + (TiersPerDivision - 1) + 1.0;
}
