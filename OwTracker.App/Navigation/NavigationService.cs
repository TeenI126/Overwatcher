using CommunityToolkit.Mvvm.ComponentModel;

namespace OwTracker.App.Navigation;

/// <summary>The rail destinations.</summary>
public enum AppScreen
{
    Dashboard,
    StoredGames,
    HeroMap,
    Sessions,
    RankHistory,
    HeroReview,
    Settings,
}

/// <summary>Which Hero×Map sub-view to open, plus an optional focused hero/map (deep-link).</summary>
public enum HeroMapView { Heatmap, ByMap, ByHero }

/// <summary>A deep-link request into the Hero×Map screen carried from the Dashboard.</summary>
public sealed record HeroMapIntent(HeroMapView View, string? Hero = null, string? Map = null);

/// <summary>
/// App-wide navigation: the current rail screen plus deep-link intents. Decouples child
/// view-models (which request navigation) from <c>MainViewModel</c> (which hosts the content),
/// avoiding a construction cycle. Registered as a singleton.
/// </summary>
public sealed partial class NavigationService : ObservableObject
{
    [ObservableProperty] private AppScreen _current = AppScreen.Dashboard;

    /// <summary>Raised when a screen requests the Hero×Map screen with a specific view/focus.</summary>
    public event Action<HeroMapIntent>? HeroMapRequested;

    public void Go(AppScreen screen) => Current = screen;

    public void GoHeroMap(HeroMapIntent intent)
    {
        HeroMapRequested?.Invoke(intent);
        Current = AppScreen.HeroMap;
    }
}
