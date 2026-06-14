namespace OwTracker.Core;

/// <summary>
/// Canonical on-disk locations under <c>%APPDATA%\OwTracker</c> (design §10).
/// Accessing any path ensures the root directory exists.
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// The app-data root. Defaults to <c>%APPDATA%\OwTracker</c>; an optional
    /// <c>OWTRACKER_DATA_DIR</c> environment variable overrides it (used for testing against an
    /// isolated DB / a separate profile without touching the live data). Production is unaffected
    /// when the variable is unset.
    /// </summary>
    public static string Root
    {
        get
        {
            var root = Environment.GetEnvironmentVariable("OWTRACKER_DATA_DIR");
            if (string.IsNullOrWhiteSpace(root))
                root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OwTracker");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public static string DatabaseFile => Path.Combine(Root, "owtracker.db");

    public static string CalibrationFile => Path.Combine(Root, UiLayoutFileNameMarker);

    public static string CropsDirectory
    {
        get
        {
            var dir = Path.Combine(Root, "crops");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// All scraper debug screenshots (unknown-screen frames, selection-box frames, queue crops,
    /// Personal/All-Heroes calibration frames) go here — kept in one subfolder so the app-data
    /// root stays clean and the folder can be junctioned elsewhere for inspection.
    /// </summary>
    public static string DebugDirectory
    {
        get
        {
            var dir = Path.Combine(Root, "debug");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    // Kept as a literal here to avoid a Core.Services dependency from this root helper.
    private const string UiLayoutFileNameMarker = "calibration.json";
}
