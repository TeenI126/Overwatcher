using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OwTracker.App.Navigation;
using OwTracker.Core.Services;

namespace OwTracker.App.ViewModels;

/// <summary>A rail destination.</summary>
public sealed record NavItem(string Label, AppScreen Screen, string Group);

/// <summary>
/// Root view model for <c>MainWindow</c>: holds the per-screen view models, exposes the live
/// <see cref="OverwatchWatcher"/> for the status chips, and drives rail navigation through the
/// shared <see cref="NavigationService"/> (which also receives Dashboard deep-links).
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    public OverwatchWatcher Watcher { get; }
    public NavigationService Navigation { get; }

    public DashboardViewModel Dashboard { get; }
    public MatchHistoryViewModel MatchHistory { get; }
    public HeroMapViewModel HeroMap { get; }
    public SessionViewModel Sessions { get; }
    public HeroReviewViewModel HeroReview { get; }
    public SettingsViewModel Settings { get; }

    public ObservableCollection<NavItem> NavItems { get; }
    public ObservableCollection<NavItem> TrackerNav { get; }
    public ObservableCollection<NavItem> SystemNav { get; }

    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] private object _currentViewModel;

    public MainViewModel(
        OverwatchWatcher watcher,
        NavigationService navigation,
        DashboardViewModel dashboard,
        MatchHistoryViewModel matchHistory,
        HeroMapViewModel heroMap,
        SessionViewModel sessions,
        HeroReviewViewModel heroReview,
        SettingsViewModel settings)
    {
        Watcher = watcher;
        Navigation = navigation;
        Dashboard = dashboard;
        MatchHistory = matchHistory;
        HeroMap = heroMap;
        Sessions = sessions;
        HeroReview = heroReview;
        Settings = settings;

        NavItems = new ObservableCollection<NavItem>
        {
            new("Dashboard",    AppScreen.Dashboard,   "Tracker"),
            new("Stored Games", AppScreen.StoredGames, "Tracker"),
            new("Hero × Map",   AppScreen.HeroMap,     "Tracker"),
            new("Sessions",     AppScreen.Sessions,    "Tracker"),
            new("Hero Review",  AppScreen.HeroReview,  "System"),
            new("Settings",     AppScreen.Settings,    "System"),
        };

        TrackerNav = new ObservableCollection<NavItem>(NavItems.Where(n => n.Group == "Tracker"));
        SystemNav = new ObservableCollection<NavItem>(NavItems.Where(n => n.Group == "System"));

        _currentViewModel = Dashboard;
        _selectedNav = NavItems[0];
        Navigation.PropertyChanged += OnNavigationChanged;
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is not null && Navigation.Current != value.Screen)
            Navigation.Go(value.Screen);
    }

    private void OnNavigationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NavigationService.Current)) return;

        CurrentViewModel = Navigation.Current switch
        {
            AppScreen.Dashboard   => Dashboard,
            AppScreen.StoredGames => MatchHistory,
            AppScreen.HeroMap     => HeroMap,
            AppScreen.Sessions    => Sessions,
            AppScreen.HeroReview  => HeroReview,
            AppScreen.Settings    => Settings,
            _                     => Dashboard,
        };

        var item = System.Array.Find(NavItems.ToArray(), n => n.Screen == Navigation.Current);
        if (item is not null && !ReferenceEquals(item, SelectedNav))
            SelectedNav = item;

        // Re-pull data for the screen being shown so it reflects any scrape since last view.
        _ = Navigation.Current switch
        {
            AppScreen.Dashboard   => Dashboard.RefreshAsync(),
            AppScreen.StoredGames => MatchHistory.RefreshAsync(),
            AppScreen.HeroMap     => HeroMap.RefreshAsync(),
            AppScreen.Sessions    => Sessions.RefreshAsync(),
            AppScreen.HeroReview  => HeroReview.RefreshAsync(),
            _                     => System.Threading.Tasks.Task.CompletedTask,
        };
    }
}
