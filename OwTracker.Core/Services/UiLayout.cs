using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OwTracker.Core.Services;

/// <summary>A JSON-serializable rectangle (System.Drawing.Rectangle does not round-trip cleanly).</summary>
public readonly record struct RoiRect(int X, int Y, int Width, int Height)
{
    [JsonIgnore]
    public Rectangle Rectangle => new(X, Y, Width, Height);

    public static RoiRect From(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
}

/// <summary>
/// Describes where ending-hero portraits sit on the Teams tab so a single capture can be
/// sliced into per-player crops. Rows are evenly spaced from the first row.
/// </summary>
public sealed class HeroPortraitRegion
{
    /// <summary>Portrait rectangle of the first (top) player row.</summary>
    public RoiRect FirstRow { get; set; } = new(140, 360, 64, 64);

    /// <summary>Vertical distance between consecutive player rows, in pixels.</summary>
    public int RowStride { get; set; } = 58;

    /// <summary>Number of player rows to slice (e.g. 10 for 5v5/6v6 combined, per design).</summary>
    public int RowCount { get; set; } = 10;
}

/// <summary>
/// Pixel regions of the OW match-detail UI at 1440p. Loaded from
/// <c>%APPDATA%\OwTracker\calibration.json</c> when present, otherwise the hardcoded 1440p
/// defaults below (design §9). Coordinates are placeholders pending calibration.
/// </summary>
public sealed class UiLayout
{
    public const string CalibrationFileName = "calibration.json";

    // --- Summary tab ---
    public RoiRect MapName { get; set; } = new(120, 90, 520, 48);
    public RoiRect MatchDateTime { get; set; } = new(120, 150, 420, 40);
    public RoiRect GameLength { get; set; } = new(560, 150, 220, 40);
    public RoiRect MyTeamScore { get; set; } = new(900, 220, 120, 80);
    public RoiRect EnemyTeamScore { get; set; } = new(1100, 220, 120, 80);
    public RoiRect Outcome { get; set; } = new(120, 220, 360, 60);

    // --- Teams tab ---
    public HeroPortraitRegion TeamsHeroPortraits { get; set; } = new();

    // --- Personal tab ---
    public RoiRect PersonalHeroTabStrip { get; set; } = new(120, 300, 1680, 90);
    public RoiRect PersonalTimePlayed { get; set; } = new(120, 420, 360, 48);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Loads calibration from <paramref name="path"/>, or returns 1440p defaults.</summary>
    public static UiLayout Load(string path)
    {
        if (!File.Exists(path))
            return new UiLayout();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UiLayout>(json, JsonOptions) ?? new UiLayout();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Corrupt or unreadable calibration: fall back to defaults rather than crash.
            return new UiLayout();
        }
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
