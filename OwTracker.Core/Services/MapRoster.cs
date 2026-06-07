using System.Linq;
using System.Text.RegularExpressions;

namespace OwTracker.Core.Services;

/// <summary>
/// The Overwatch map pool, used to snap a noisy OCR read of the Summary map name to a canonical
/// name. The large stylised map title OCRs poorly for some maps ("WATCHPOINT: GIBRALTAR" →
/// "Ly Z-[e ge]"); snapping to a fixed list both fixes the name and keeps the dedup key
/// (MapName, MatchDatetime) stable across scrapes. Keep current as the map pool changes.
/// </summary>
public static class MapRoster
{
    public static readonly IReadOnlyList<string> Maps = new[]
    {
        // Control
        "Antarctic Peninsula", "Busan", "Ilios", "Lijiang Tower", "Nepal", "Oasis", "Samoa",
        // Escort
        "Circuit Royal", "Dorado", "Havana", "Junkertown", "Rialto", "Route 66",
        "Shambali Monastery", "Watchpoint: Gibraltar",
        // Hybrid
        "Blizzard World", "Eichenwalde", "Hollywood", "King's Row", "Midtown", "Numbani", "Paraíso",
        // Push
        "Colosseo", "Esperança", "New Queen Street", "Runasapi",
        // Flashpoint
        "New Junk City", "Suravasa", "Aatlis",
        // Clash
        "Hanaoka", "Throne of Anubis",
    };

    private static readonly (string Canonical, string Norm)[] Normalised =
        Maps.Select(m => (m, Norm(m))).ToArray();

    private static string Norm(string s)
    {
        var folded = s
            .Replace('ç', 'c').Replace('Ç', 'C')
            .Replace('á', 'a').Replace('Á', 'A')
            .Replace('í', 'i').Replace('Í', 'I');
        return Regex.Replace(folded.ToUpperInvariant(), "[^A-Z0-9]", "");
    }

    /// <summary>
    /// Snaps a noisy OCR map string to the closest pool map, or null if nothing matches well.
    /// Prefers substring containment either way, then longest-common-substring length, requiring
    /// the match to cover most of the map's normalised name (so severe garbles fall through and
    /// keep the raw read rather than snap to a wrong map).
    /// </summary>
    public static string? Snap(string ocr)
    {
        var o = Norm(ocr);
        if (o.Length < 3) return null;

        string? best = null;
        var bestScore = 0;
        foreach (var (canonical, n) in Normalised)
        {
            int score;
            if (o.Contains(n) || n.Contains(o))
                score = Math.Min(o.Length, n.Length) * 2;
            else
                score = LongestCommonSubstring(o, n);

            if (score > bestScore && score >= Math.Max(4, n.Length * 6 / 10))
            {
                bestScore = score;
                best = canonical;
            }
        }
        return best;
    }

    /// <summary>
    /// True if <paramref name="name"/> is (the normalised form of) a canonical pool map — i.e. a
    /// clean read or one Snap already resolved. The scraper uses this to detect a garbled map
    /// title ("{e]V}]¥.1.]") that fell through Snap, so it can re-capture after a settle delay
    /// (the Summary header is occasionally still loading on the first match opened).
    /// </summary>
    public static bool IsKnown(string name)
    {
        var n = Norm(name);
        return n.Length >= 3 && Normalised.Any(x => x.Norm == n);
    }

    private static int LongestCommonSubstring(string a, string b)
    {
        var dp = new int[b.Length + 1];
        var best = 0;
        for (var i = 1; i <= a.Length; i++)
        {
            var prev = 0;
            for (var j = 1; j <= b.Length; j++)
            {
                var tmp = dp[j];
                dp[j] = a[i - 1] == b[j - 1] ? prev + 1 : 0;
                if (dp[j] > best) best = dp[j];
                prev = tmp;
            }
        }
        return best;
    }
}
