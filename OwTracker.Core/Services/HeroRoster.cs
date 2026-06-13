using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OwTracker.Core.Services;

/// <summary>
/// The Overwatch hero roster, used to snap a noisy OCR read (from the Personal sidebar sub-tabs)
/// to a canonical hero name. OCR often prefixes/suffixes a name with junk ("DL ECHO") or drops a
/// character; matching against a fixed roster recovers the real name.
///
/// New heroes ship constantly, so the roster is NOT hard-coded alone: the built-in defaults are
/// merged with a user-editable file at <c>%APPDATA%\OwTracker\heroes.txt</c> (one hero per line),
/// which is seeded with the defaults on first run. Add new heroes there — no code change or
/// rebuild needed. (This is the OCR-matching roster; the ML classifier has its own
/// <c>HeroRoster.json</c> override handled by <c>HeroRosterProvider</c>.)
/// </summary>
public static class HeroRoster
{
    /// <summary>Built-in roster. Can lag real releases — <see cref="RosterFileName"/> extends it.</summary>
    private static readonly string[] DefaultHeroes =
    {
        // Tank
        "D.Va", "Doomfist", "Hazard", "Junker Queen", "Mauga", "Orisa", "Ramattra",
        "Reinhardt", "Roadhog", "Sigma", "Winston", "Wrecking Ball", "Zarya",
        // Damage
        "Ashe", "Bastion", "Cassidy", "Echo", "Genji", "Hanzo", "Junkrat", "Mei", "Pharah",
        "Reaper", "Sojourn", "Soldier: 76", "Sombra", "Symmetra", "Torbjörn", "Tracer",
        "Venture", "Widowmaker",
        // Support
        "Ana", "Baptiste", "Brigitte", "Illari", "Juno", "Kiriko", "Lifeweaver", "Lúcio",
        "Mercy", "Moira", "Zenyatta",
        // Newer heroes added from the live in-game roster (the lists above lag real releases).
        "Mizuki", "Domina", "Anran", "Emre", "Sierra", "Jetpack Cat", "Wuyang", "Freja",
    };

    /// <summary>User-editable roster file (merged with the built-in defaults).</summary>
    public const string RosterFileName = "heroes.txt";

    public static IReadOnlyList<string> Heroes { get; } = LoadHeroes();

    private static readonly (string Canonical, string Norm)[] Normalised =
        Heroes.Select(h => (h, Norm(h))).ToArray();

    /// <summary>Built-in defaults ∪ any heroes listed in <c>%APPDATA%\OwTracker\heroes.txt</c>.
    /// The file is seeded with the defaults on first run, so it doubles as a self-documenting,
    /// editable list. Best-effort: any IO/parse failure falls back to the built-in defaults.</summary>
    private static IReadOnlyList<string> LoadHeroes()
    {
        var ordered = new List<string>();
        var seen    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string h) { h = h.Trim(); if (h.Length > 0 && seen.Add(h)) ordered.Add(h); }

        foreach (var h in DefaultHeroes) Add(h);

        try
        {
            var path = Path.Combine(AppPaths.Root, RosterFileName);
            if (File.Exists(path))
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;   // comments / blanks
                    Add(line);
                }
            }
            else
            {
                var header = new[]
                {
                    "# OW Tracker hero roster — one hero name per line.",
                    "# Add heroes here as they release; they're matched to OCR reads of the Personal",
                    "# sidebar. Lines starting with '#' are ignored. These entries are MERGED with the",
                    "# built-in defaults, so you only need to add what's missing.",
                    "",
                };
                File.WriteAllLines(path, header.Concat(DefaultHeroes));
            }
        }
        catch { /* roster file is best-effort; the built-in defaults still apply */ }

        return ordered;
    }

    /// <summary>Canonical hero → role ("tank"/"damage"/"support"). New/unknown heroes return "".</summary>
    private static readonly Dictionary<string, string> HeroRole = new(StringComparer.OrdinalIgnoreCase)
    {
        // Tank
        ["D.Va"] = "tank", ["Doomfist"] = "tank", ["Hazard"] = "tank", ["Junker Queen"] = "tank",
        ["Mauga"] = "tank", ["Orisa"] = "tank", ["Ramattra"] = "tank", ["Reinhardt"] = "tank",
        ["Roadhog"] = "tank", ["Sigma"] = "tank", ["Winston"] = "tank", ["Wrecking Ball"] = "tank",
        ["Zarya"] = "tank",
        // Damage
        ["Ashe"] = "damage", ["Bastion"] = "damage", ["Cassidy"] = "damage", ["Echo"] = "damage",
        ["Genji"] = "damage", ["Hanzo"] = "damage", ["Junkrat"] = "damage", ["Mei"] = "damage",
        ["Pharah"] = "damage", ["Reaper"] = "damage", ["Sojourn"] = "damage", ["Soldier: 76"] = "damage",
        ["Sombra"] = "damage", ["Symmetra"] = "damage", ["Torbjörn"] = "damage", ["Tracer"] = "damage",
        ["Venture"] = "damage", ["Widowmaker"] = "damage",
        // Support
        ["Ana"] = "support", ["Baptiste"] = "support", ["Brigitte"] = "support", ["Illari"] = "support",
        ["Juno"] = "support", ["Kiriko"] = "support", ["Lifeweaver"] = "support", ["Lúcio"] = "support",
        ["Mercy"] = "support", ["Moira"] = "support", ["Zenyatta"] = "support",
    };

    /// <summary>
    /// Role ("tank"/"damage"/"support") for a hero name, or "" if unknown (a brand-new hero not yet
    /// classified). Tolerates a noisy name by snapping it to the roster first.
    /// </summary>
    public static string RoleOf(string? hero)
    {
        if (string.IsNullOrWhiteSpace(hero)) return string.Empty;
        if (HeroRole.TryGetValue(hero, out var r)) return r;
        var snapped = Snap(hero);
        return snapped is not null && HeroRole.TryGetValue(snapped, out var r2) ? r2 : string.Empty;
    }

    /// <summary>Uppercase, letters+digits only (strips spaces, punctuation, accents).</summary>
    private static string Norm(string s)
    {
        var folded = s
            .Replace('ö', 'o').Replace('Ö', 'O')
            .Replace('ú', 'u').Replace('Ú', 'U');
        return Regex.Replace(folded.ToUpperInvariant(), "[^A-Z0-9]", "");
    }

    /// <summary>
    /// Snaps a noisy OCR string to the closest roster hero, or null if nothing matches well.
    /// Prefers substring containment either way (handles leading/trailing junk), then falls back
    /// to longest-common-substring length, requiring it to cover most of the hero's name.
    /// </summary>
    public static string? Snap(string ocr)
    {
        var o = Norm(ocr);
        if (o.Length < 2) return null;

        string? best = null;
        var bestScore = 0;
        foreach (var (canonical, n) in Normalised)
        {
            int score;
            if (o.Contains(n) || n.Contains(o))
                score = Math.Min(o.Length, n.Length) * 2;   // strong: one contains the other
            else
                score = LongestCommonSubstring(o, n);

            // Require the match to cover at least ~60% of the hero name to avoid spurious hits
            // (e.g. a 2-char overlap matching "ANA"). Longer names need proportionally more.
            if (score > bestScore && score >= Math.Max(3, n.Length * 6 / 10))
            {
                bestScore = score;
                best = canonical;
            }
        }
        return best;
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
