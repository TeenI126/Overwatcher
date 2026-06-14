namespace OwTracker.Core.Models;

/// <summary>
/// One role's competitive standing within a <see cref="RankSnapshot"/> — read from a single card on
/// the COMPETITIVE PROGRESS screen (Tank / Damage / Support / Open Queue).
///
/// A ranked role shows a division + tier ("DIAMOND 5") and a rank-progress percentage; Master and
/// above additionally show a Challenger Score. An unplaced Open Queue role shows a *predicted* rank
/// plus placement progress ("1/10") instead — captured via <see cref="IsRanked"/> +
/// <see cref="PlacementGames"/>/<see cref="PlacementRequired"/>.
/// </summary>
public class RoleRank
{
    public int Id { get; set; }
    public int RankSnapshotId { get; set; }

    /// <summary>"Tank" | "Damage" | "Support" | "Open Queue".</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Division tier name: "Bronze".."Champion", or "Unranked"/"Unknown".
    /// For an unplaced role this holds the *predicted* division.</summary>
    public string Division { get; set; } = string.Empty;

    /// <summary>Tier within the division (1 = highest .. 5 = lowest); 0 when none is shown.</summary>
    public int Tier { get; set; }

    /// <summary>Rank-progress percentage toward the next tier (can be negative, e.g. −2%),
    /// or null when not shown (e.g. a role still in placements).</summary>
    public int? RankProgress { get; set; }

    /// <summary>Challenger Score (Master and above only), or null.</summary>
    public int? ChallengerScore { get; set; }

    /// <summary>False when the role is unplaced/unranked (the division is a prediction).</summary>
    public bool IsRanked { get; set; }

    /// <summary>Placement games completed (unplaced roles), e.g. 1 of "1/10".</summary>
    public int? PlacementGames { get; set; }

    /// <summary>Placement games required (unplaced roles), e.g. 10 of "1/10".</summary>
    public int? PlacementRequired { get; set; }

    /// <summary>Raw OCR text of the card's rank block — kept for debugging / re-parsing.</summary>
    public string RawText { get; set; } = string.Empty;

    // Navigation
    public RankSnapshot? Snapshot { get; set; }
}
