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

    /// <summary>The six objective/mode types, in roster order. Matches the redesign's mode filter.</summary>
    public static readonly IReadOnlyList<string> Modes = new[]
    {
        "Control", "Hybrid", "Escort", "Push", "Flashpoint", "Clash",
    };

    /// <summary>Canonical map → objective mode ("Control"/"Hybrid"/"Escort"/"Push"/"Flashpoint"/"Clash").</summary>
    private static readonly Dictionary<string, string> MapMode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Antarctic Peninsula"] = "Control", ["Busan"] = "Control", ["Ilios"] = "Control",
        ["Lijiang Tower"] = "Control", ["Nepal"] = "Control", ["Oasis"] = "Control", ["Samoa"] = "Control",

        ["Circuit Royal"] = "Escort", ["Dorado"] = "Escort", ["Havana"] = "Escort",
        ["Junkertown"] = "Escort", ["Rialto"] = "Escort", ["Route 66"] = "Escort",
        ["Shambali Monastery"] = "Escort", ["Watchpoint: Gibraltar"] = "Escort",

        ["Blizzard World"] = "Hybrid", ["Eichenwalde"] = "Hybrid", ["Hollywood"] = "Hybrid",
        ["King's Row"] = "Hybrid", ["Midtown"] = "Hybrid", ["Numbani"] = "Hybrid", ["Paraíso"] = "Hybrid",

        ["Colosseo"] = "Push", ["Esperança"] = "Push", ["New Queen Street"] = "Push", ["Runasapi"] = "Push",

        ["New Junk City"] = "Flashpoint", ["Suravasa"] = "Flashpoint", ["Aatlis"] = "Flashpoint",

        ["Hanaoka"] = "Clash", ["Throne of Anubis"] = "Clash",
    };

    /// <summary>
    /// Resolves the canonical mode for a match. Prefers normalising the stored raw game-mode string
    /// (e.g. "ESCORT" → "Escort"); falls back to the map → mode lookup when the field is blank or
    /// unrecognised. Returns "" if nothing resolves.
    /// </summary>
    public static string ResolveMode(string? mapName, string? rawGameMode)
    {
        var fromRaw = NormalizeMode(rawGameMode);
        if (fromRaw.Length > 0) return fromRaw;
        return mapName is not null && MapMode.TryGetValue(mapName, out var m) ? m : string.Empty;
    }

    /// <summary>Map name → canonical mode, or "" if the map is unknown.</summary>
    public static string ModeOf(string? mapName) =>
        mapName is not null && MapMode.TryGetValue(mapName, out var m) ? m : string.Empty;

    /// <summary>Normalises a raw OCR game-mode token ("ESCORT", "control", "FLASH POINT") to a
    /// canonical mode, or "" if it doesn't look like one of the six modes.</summary>
    private static string NormalizeMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var n = Regex.Replace(raw.ToUpperInvariant(), "[^A-Z]", "");
        return n switch
        {
            "CONTROL"    => "Control",
            "HYBRID"     => "Hybrid",
            "ESCORT"     => "Escort",
            "PUSH"       => "Push",
            "FLASHPOINT" => "Flashpoint",
            "CLASH"      => "Clash",
            _            => string.Empty,
        };
    }

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
