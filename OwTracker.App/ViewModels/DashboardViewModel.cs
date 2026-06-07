using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OwTracker.Core.Repositories.Interfaces;
using OwTracker.Core.Services;

namespace OwTracker.App.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IMatchRepository   _matchRepository;
    private readonly ISessionRepository _sessionRepository;
    private readonly HistoryScraper     _scraper;

    public OverwatchWatcher Watcher { get; }
    public ObservableCollection<string> ScrapeLog { get; } = new();

    [ObservableProperty] private int      _matchCount;
    [ObservableProperty] private TimeSpan _totalActiveTime;
    [ObservableProperty] private bool     _isScraping;

    /// <summary>When checked, the scrape ignores the "stop after 3 consecutive duplicates" rule and
    /// walks the whole list (back-fill). Mirrors the CLI's --scrape-deep.</summary>
    [ObservableProperty] private bool     _ignoreDuplicates;

    public DashboardViewModel(
        OverwatchWatcher    watcher,
        IMatchRepository    matchRepository,
        ISessionRepository  sessionRepository,
        HistoryScraper      scraper)
    {
        Watcher            = watcher;
        _matchRepository   = matchRepository;
        _sessionRepository = sessionRepository;
        _scraper           = scraper;

        // Re-evaluate Start Scrape button whenever IsOwRunning changes.
        Watcher.PropertyChanged += OnWatcherPropertyChanged;

        _scraper.LogLine += line =>
            Application.Current?.Dispatcher.Invoke(() => ScrapeLog.Add(line));
    }

    // Toggle command states whenever a scrape starts/stops.
    partial void OnIsScrapingChanged(bool value)
    {
        StartScrapeCommand.NotifyCanExecuteChanged();
        ScrapeRecentCommand.NotifyCanExecuteChanged();
        ScrapeLastMatchCommand.NotifyCanExecuteChanged();
        DeleteHistoryCommand.NotifyCanExecuteChanged();
    }

    private void OnWatcherPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OverwatchWatcher.IsOwRunning) or nameof(OverwatchWatcher.IsOwInForeground))
            Application.Current?.Dispatcher.Invoke(() =>
            {
                StartScrapeCommand.NotifyCanExecuteChanged();
                ScrapeRecentCommand.NotifyCanExecuteChanged();
                ScrapeLastMatchCommand.NotifyCanExecuteChanged();
            });
    }

    /// <summary>Appends a line to the scrape log from any thread (used by App bootstrap).</summary>
    public void AddLog(string message) =>
        Application.Current?.Dispatcher.Invoke(() => ScrapeLog.Add(message));

    public async Task RefreshAsync()
    {
        MatchCount      = await _matchRepository.CountAsync();
        TotalActiveTime = await _sessionRepository.GetTotalActiveTimeAsync();
    }

    // Enabled when OW is running (anywhere on desktop) and we're not already scraping.
    private bool CanStartScrape() => Watcher.IsOwRunning && !IsScraping;

    private bool CanDeleteHistory() => !IsScraping;

    [RelayCommand(CanExecute = nameof(CanDeleteHistory))]
    private async Task DeleteHistoryAsync()
    {
        var confirm = MessageBox.Show(
            "Delete ALL scraped match history?\n\nThis permanently removes every match, " +
            "player, and hero-playtime record from the local database. This cannot be undone.",
            "Delete Match History",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var deleted = await _matchRepository.DeleteAllAsync();
            ScrapeLog.Add($"[{DateTime.Now:HH:mm:ss}] Deleted {deleted} match record(s) from the database.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ScrapeLog.Add($"[{DateTime.Now:HH:mm:ss}] Delete failed: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartScrape))]
    private Task StartScrapeAsync() => RunScrapeAsync(null);

    /// <summary>Test helper: scrape only the most recent 10 games.</summary>
    [RelayCommand(CanExecute = nameof(CanStartScrape))]
    private Task ScrapeRecentAsync() => RunScrapeAsync(10);

    /// <summary>Scrape only the most recent match (the top of the Game Reports list).</summary>
    [RelayCommand(CanExecute = nameof(CanStartScrape))]
    private Task ScrapeLastMatchAsync() => RunScrapeAsync(1);

    private async Task RunScrapeAsync(int? maxGames)
    {
        IsScraping = true;
        ScrapeLog.Clear();

        try
        {
            var result = await _scraper.ScrapeAsync(maxGames: maxGames, stopOnDuplicates: !IgnoreDuplicates);
            ScrapeLog.Add(result.Success
                ? $"[{DateTime.Now:HH:mm:ss}] Completed — {result.NewRecords} new, {result.SkippedDuplicates} duplicates."
                : $"[{DateTime.Now:HH:mm:ss}] Failed: {result.ErrorMessage}");

            await RefreshAsync();
        }
        finally
        {
            IsScraping = false;
        }
    }
}
