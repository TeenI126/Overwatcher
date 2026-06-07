using System.Reflection;
using System.Text.Json;
using OwTracker.Core;
using OwTracker.Core.Services.Interfaces;

namespace OwTracker.ML;

/// <summary>
/// Loads the hero roster from a user override at <c>%APPDATA%\OwTracker\HeroRoster.json</c>
/// if present, otherwise the embedded canonical roster shipped with this assembly
/// (design §10). Cached after first load.
/// </summary>
public sealed class HeroRosterProvider : IHeroRosterProvider
{
    public const string OverrideFileName = "HeroRoster.json";
    private const string EmbeddedResourceName = "OwTracker.ML.Assets.HeroRoster.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Lazy<HeroRoster> _roster;

    public HeroRosterProvider() => _roster = new Lazy<HeroRoster>(Load);

    public HeroRoster GetRoster() => _roster.Value;

    public IReadOnlyList<string> GetHeroNames()
        => _roster.Value.Heroes.Select(h => h.Name).ToList();

    private static HeroRoster Load()
    {
        var overridePath = Path.Combine(AppPaths.Root, OverrideFileName);
        if (File.Exists(overridePath))
        {
            try
            {
                var json = File.ReadAllText(overridePath);
                var parsed = JsonSerializer.Deserialize<HeroRoster>(json, JsonOptions);
                if (parsed is { Heroes.Count: > 0 })
                    return parsed;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Fall through to the embedded roster.
            }
        }

        return LoadEmbedded();
    }

    private static HeroRoster LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException($"Embedded roster '{EmbeddedResourceName}' not found.");
        return JsonSerializer.Deserialize<HeroRoster>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Embedded hero roster could not be parsed.");
    }
}
