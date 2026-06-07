using CommunityToolkit.Mvvm.ComponentModel;
using OwTracker.Core.Services;

namespace OwTracker.App.ViewModels;

/// <summary>
/// Root view model for <c>MainWindow</c>. Exposes the live <see cref="OverwatchWatcher"/> for
/// the status bar and holds the per-tab view models.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    public OverwatchWatcher Watcher { get; }
    public DashboardViewModel Dashboard { get; }
    public MatchHistoryViewModel MatchHistory { get; }
    public SessionViewModel Sessions { get; }
    public HeroReviewViewModel HeroReview { get; }
    public SettingsViewModel Settings { get; }

    public MainViewModel(
        OverwatchWatcher watcher,
        DashboardViewModel dashboard,
        MatchHistoryViewModel matchHistory,
        SessionViewModel sessions,
        HeroReviewViewModel heroReview,
        SettingsViewModel settings)
    {
        Watcher = watcher;
        Dashboard = dashboard;
        MatchHistory = matchHistory;
        Sessions = sessions;
        HeroReview = heroReview;
        Settings = settings;
    }
}
