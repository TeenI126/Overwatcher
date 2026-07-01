using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OwTracker.App.Navigation;
using OwTracker.Core.Models;
using OwTracker.Core.Repositories.Interfaces;
using OwTracker.Core.Services;
using OwTracker.Core.Stats;

namespace OwTracker.App.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IMatchRepository   _matchRepository;
    private readonly ISessionRepository _sessionRepository;
    private readonly IRankRepository    _rankRepository;
    private readonly HistoryScraper     _scraper;
    private readonly NavigationService  _nav;

    public OverwatchWatcher Watcher { get; }
    public ObservableCollection<string> ScrapeLog { get; } = new();

    /// <summary>Outcomes of the most recent matches (newest first) for the Recent Form strip.</summary>
    public ObservableCollection<MatchOutcome> RecentForm { get; } = new();

    [ObservableProperty] private int      _matchCount;
    [ObservableProperty] private TimeSpan _totalActiveTime;
    [ObservableProperty] private bool     _isScraping;
    [ObservableProperty] private bool     _ignoreDuplicates;
    [ObservableProperty] private bool     _overwriteOnDuplicate;

    /// <summary>When true (default), a scrape snapshots competitive rank before the match loop.
    /// Off = skip rank capture and go straight to scraping games.</summary>
    [ObservableProperty] private bool     _captureRank = true;

    // ── Queue filter (which queue the overview stats are computed over) ─────
    private const string AllQueues = "all";

    /// <summary>Queue choices: "All queues" + the four game-mode labels (Comp Role/Open, Unranked,
    /// Quick Play). Mirrors the Stored Games game-mode filter.</summary>
    public IReadOnlyList<FilterOption> QueueOptions { get; } =
        new[] { new FilterOption(AllQueues, "All queues") }
            .Concat(StatsService.GameModes.Select(g => new FilterOption(g, g))).ToList();

    /// <summary>Selected queue; defaults to Competitive Role Queue. Recomputes the stats on change.</summary>
    [ObservableProperty] private string _queueFilter = "Comp · Role Queue";

    // ── Overview KPIs (over the queue-filtered set) ────────────────────────
    [ObservableProperty] private int      _filteredGames;
    [ObservableProperty] private double   _winRate;
    [ObservableProperty] private double   _trackedPlaytimeHours;
    [ObservableProperty] private int      _todayWins;
    [ObservableProperty] private int      _todayLosses;
    [ObservableProperty] private int      _todayGames;

    // ── Recent form / streak ───────────────────────────────────────────────
    [ObservableProperty] private int          _recentWins;
    [ObservableProperty] private int          _recentLosses;
    [ObservableProperty] private MatchOutcome _streakOutcome = MatchOutcome.Unknown;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StreakText))] private int _streakLength;

    public string StreakText
    {
        get
        {
            if (StreakLength <= 0) return "No games yet";
            var word = StreakOutcome switch
            {
                MatchOutcome.Win  => "Win",
                MatchOutcome.Loss => "Loss",
                MatchOutcome.Draw => "Draw",
                _                 => "—",
            };
            return $"{StreakLength}-game {word} streak";
        }
    }

    // ── Top performers ─────────────────────────────────────────────────────
    [ObservableProperty] private HeroStat? _topHero;
    [ObservableProperty] private MapStat?  _topMap;
    [ObservableProperty] private HeroStat? _mostPlayed;

    // ── Competitive rank (latest snapshot) ──────────────────────────────────
    /// <summary>The four role cards of the most recent rank snapshot (Tank/Damage/Support/Open Queue).</summary>
    public ObservableCollection<RankCardVm> CurrentRank { get; } = new();
    [ObservableProperty] private bool   _hasRank;
    [ObservableProperty] private string _rankCapturedText = "";

    public DashboardViewModel(
        OverwatchWatcher    watcher,
        IMatchRepository    matchRepository,
        ISessionRepository  sessionRepository,
        IRankRepository     rankRepository,
        HistoryScraper      scraper,
        NavigationService   nav)
    {
        Watcher            = watcher;
        _matchRepository   = matchRepository;
        _sessionRepository = sessionRepository;
        _rankRepository    = rankRepository;
        _scraper           = scraper;
        _nav               = nav;

        Watcher.PropertyChanged += OnWatcherPropertyChanged;

        _scraper.LogLine += line =>
            Application.Current?.Dispatcher.Invoke(() => ScrapeLog.Add(line));
    }

    partial void OnIsScrapingChanged(bool value)
    {
        StartScrapeCommand.NotifyCanExecuteChanged();
        ScrapeRecentCommand.NotifyCanExecuteChanged();
        ScrapeLastMatchCommand.NotifyCanExecuteChanged();
        DeleteHistoryCommand.NotifyCanExecuteChanged();
    }

    partial void OnStreakOutcomeChanged(MatchOutcome value) => OnPropertyChanged(nameof(StreakText));

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

    public void AddLog(string message) =>
        Application.Current?.Dispatcher.Invoke(() => ScrapeLog.Add(message));

    public async Task RefreshAsync()
    {
        MatchCount      = await _matchRepository.CountAsync();
        TotalActiveTime = await _sessionRepository.GetTotalActiveTimeAsync();

        _allMatches = await _matchRepository.GetAllWithDetailsAsync();
        RecomputeStats();

        await RefreshRankAsync();
    }

    /// <summary>All loaded matches (cached so the queue filter recomputes in-memory, no DB re-query).</summary>
    private IReadOnlyList<MatchRecord> _allMatches = System.Array.Empty<MatchRecord>();

    /// <summary>Re-derives every overview stat from the cached matches filtered to the selected queue.</summary>
    private void RecomputeStats()
    {
        var matches = QueueFilter == AllQueues
            ? _allMatches
            : _allMatches.Where(m => StatsService.GameModeLabel(m) == QueueFilter).ToList();

        var overall = StatsService.Overall(matches);
        FilteredGames        = overall.Games;
        WinRate              = overall.WinRate;
        TrackedPlaytimeHours = Math.Round(overall.Time.TotalHours, 1);

        var today  = StatsService.Today(matches, DateTime.Now);
        TodayGames  = today.Games;
        TodayWins   = today.Wins;
        TodayLosses = today.Losses;

        var form = StatsService.RecentForm(matches, 14);
        RecentForm.Clear();
        foreach (var o in form) RecentForm.Add(o);
        RecentWins   = form.Count(o => o == MatchOutcome.Win);
        RecentLosses = form.Count(o => o == MatchOutcome.Loss);

        var streak = StatsService.CurrentStreak(matches);
        StreakOutcome = streak.Type;
        StreakLength  = streak.Length;

        TopHero    = StatsService.TopHero(matches, minGames: 3);
        TopMap     = StatsService.TopMap(matches, minGames: 3);
        MostPlayed = StatsService.MostPlayedHero(matches);
    }

    partial void OnQueueFilterChanged(string value) => RecomputeStats();

    /// <summary>Loads the most recent competitive-rank snapshot for the Dashboard rank strip.</summary>
    private async Task RefreshRankAsync()
    {
        var latest = await _rankRepository.GetLatestAsync();
        CurrentRank.Clear();
        if (latest is null || latest.Roles.Count == 0)
        {
            HasRank = false;
            RankCapturedText = "";
            return;
        }

        // Keep the canonical role order (Tank, Damage, Support, Open Queue) regardless of row order.
        foreach (var role in RankRoster.Roles)
        {
            var r = latest.Roles.FirstOrDefault(x => x.Role == role);
            if (r is not null) CurrentRank.Add(RankCardVm.From(r));
        }
        foreach (var r in latest.Roles.Where(x => !RankRoster.Roles.Contains(x.Role)))
            CurrentRank.Add(RankCardVm.From(r));

        HasRank = true;
        RankCapturedText = $"captured {latest.CapturedAt.ToLocalTime():MMM d, h:mm tt}";
    }

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

    [RelayCommand(CanExecute = nameof(CanStartScrape))]
    private Task ScrapeRecentAsync() => RunScrapeAsync(10);

    [RelayCommand(CanExecute = nameof(CanStartScrape))]
    private Task ScrapeLastMatchAsync() => RunScrapeAsync(1);

    internal Task ScrapeForTrayAsync(int maxGames) => RunScrapeAsync(maxGames);

    private async Task RunScrapeAsync(int? maxGames)
    {
        IsScraping = true;
        ScrapeLog.Clear();

        try
        {
            var result = await _scraper.ScrapeAsync(
                maxGames: maxGames, stopOnDuplicates: !IgnoreDuplicates, overwrite: OverwriteOnDuplicate,
                captureRank: CaptureRank);
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

    // ── Navigation (rail jumps + top-performer deep-links) ──────────────────
    [RelayCommand] private void GoStoredGames() => _nav.Go(AppScreen.StoredGames);
    [RelayCommand] private void GoHeatmap() => _nav.GoHeroMap(new HeroMapIntent(HeroMapView.Heatmap));
    [RelayCommand] private void GoRankHistory() => _nav.Go(AppScreen.RankHistory);

    [RelayCommand]
    private void OpenTopHero()
    {
        if (TopHero is not null) _nav.GoHeroMap(new HeroMapIntent(HeroMapView.ByHero, Hero: TopHero.Name));
    }

    [RelayCommand]
    private void OpenTopMap()
    {
        if (TopMap is not null) _nav.GoHeroMap(new HeroMapIntent(HeroMapView.ByMap, Map: TopMap.Map));
    }

    [RelayCommand]
    private void OpenMostPlayed()
    {
        if (MostPlayed is not null) _nav.GoHeroMap(new HeroMapIntent(HeroMapView.ByHero, Hero: MostPlayed.Name));
    }
}
