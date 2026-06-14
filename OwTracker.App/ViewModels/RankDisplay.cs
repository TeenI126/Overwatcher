using System;
using System.Globalization;
using System.Windows.Media;
using OwTracker.App.Theme;
using OwTracker.Core.Models;

namespace OwTracker.App.ViewModels;

/// <summary>
/// A role's current standing rendered for the UI (the Dashboard rank strip + the history-view ticker
/// tape). Maps a <see cref="RoleRank"/> to display strings + theme brushes; the ranked/Master+/
/// unplaced variants each render distinctly.
/// </summary>
public sealed record RankCardVm(
    string Role,
    Brush  RoleColor,
    string RankText,     // "Diamond 5" | "Unranked" | "—"
    string DetailText,   // "-2%" | "CS 2,304 · +4%" | "Placement 1/10"
    Brush  DetailColor)  // green up / red down / muted
{
    public static RankCardVm From(RoleRank r)
    {
        var color = r.Role.Equals("Open Queue", StringComparison.OrdinalIgnoreCase)
            ? Palette.Accent
            : Palette.RoleBrush(r.Role);

        if (!r.IsRanked)
        {
            var place = r.PlacementGames is not null && r.PlacementRequired is not null
                ? $"Placement {r.PlacementGames}/{r.PlacementRequired}"
                : "Unplaced";
            return new RankCardVm(r.Role, color, "Unranked", place, Palette.Muted);
        }

        var rank = r.Division == "Unknown" ? "—" : $"{r.Division} {r.Tier}";

        var detail = "";
        var dcol   = Palette.Muted;
        if (r.RankProgress is int p)
        {
            detail = (p > 0 ? "+" : "") + p + "%";
            dcol   = p > 0 ? Palette.RankUp : p < 0 ? Palette.Loss : Palette.Muted;
        }
        if (r.ChallengerScore is int cs)
            // Invariant "#,0" → "2,304" (not the machine locale's "2 304").
            detail = $"CS {cs.ToString("#,0", CultureInfo.InvariantCulture)}" + (detail.Length > 0 ? $" · {detail}" : "");

        return new RankCardVm(r.Role, color, rank, detail, dcol);
    }
}
