using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OwTracker.Core.Models;
using OwTracker.Core.Repositories.Interfaces;
using OwTracker.Core.Services;
using OwTracker.Core.Stats;

namespace OwTracker.App.ViewModels;

/// <summary>A choice in one of the HUD filter dropdowns. <see cref="Mode"/> drives the colour dot.</summary>
public sealed record FilterOption(string Value, string Label, string Mode = "");

/// <summary>
/// Stored Games tab: every scraped match as an expandable card, with client-side filtering
/// (mode/type/map/result/role/search), sorting, and KPI tiles that recompute from the filtered set.
/// </summary>
public sealed partial class MatchHistoryViewModel : ObservableObject
{
    private readonly IMatchRepository _matchRepository;

    public ObservableCollection<MatchCardViewModel> AllCards { get; } = new();
    public ICollectionView MatchesView { get; }

    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _shownCount;
    [ObservableProperty] private int _shownWins;
    [ObservableProperty] private int _shownLosses;
    [ObservableProperty] private double _shownWinRate;

    // Filter state
    [ObservableProperty] private string _gameModeFilter = "all";
    [ObservableProperty] private string _mapTypeFilter = "all";
    [ObservableProperty] private string _mapFilter = "all";
    [ObservableProperty] private bool _winOnly;
    [ObservableProperty] private bool _lossOnly;
    [ObservableProperty] private bool _tankOnly;
    [ObservableProperty] private bool _damageOnly;
    [ObservableProperty] private bool _supportOnly;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _sortKey = "recent";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SortDirectionGlyph))] private bool _sortDescending = true;

    public string SortDirectionGlyph => SortDescending ? "▼" : "▲";

    // Dropdown options
    public IReadOnlyList<FilterOption> GameModeOptions { get; }
    public IReadOnlyList<FilterOption> MapTypeOptions { get; }
    public IReadOnlyList<FilterOption> SortOptions { get; }
    [ObservableProperty] private IReadOnlyList<FilterOption> _mapOptions;

    public bool AnyFilterActive =>
        GameModeFilter != "all" || MapTypeFilter != "all" || MapFilter != "all" ||
        WinOnly || LossOnly || TankOnly || DamageOnly || SupportOnly ||
        !string.IsNullOrWhiteSpace(SearchText);

    public MatchHistoryViewModel(IMatchRepository matchRepository)
    {
        _matchRepository = matchRepository;

        MatchesView = CollectionViewSource.GetDefaultView(AllCards);
        MatchesView.Filter = FilterCard;
        ApplySort();

        GameModeOptions = new[] { new FilterOption("all", "All game modes") }
            .Concat(StatsService.GameModes.Select(g => new FilterOption(g, g))).ToList();
        MapTypeOptions = new[] { new FilterOption("all", "All map types") }
            .Concat(MapRoster.Modes.Select(m => new FilterOption(m, m, m))).ToList();
        SortOptions = new[]
        {
            new FilterOption("recent", "Most recent"),
            new FilterOption("length", "Game length"),
            new FilterOption("kda", "Performance (KDA)"),
            new FilterOption("sr", "SR change"),
        };
        _mapOptions = BuildMapOptions();
    }

    private IReadOnlyList<FilterOption> BuildMapOptions()
    {
        var maps = MapRoster.Maps
            .Where(m => MapTypeFilter == "all" || MapRoster.ModeOf(m) == MapTypeFilter)
            .Select(m => new FilterOption(m, m, MapRoster.ModeOf(m)));
        return new[] { new FilterOption("all", "All maps") }.Concat(maps).ToList();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var matches = await _matchRepository.GetAllWithDetailsAsync();
        AllCards.Clear();
        foreach (var m in matches)
            AllCards.Add(new MatchCardViewModel(m));
        TotalCount = AllCards.Count;
        RecomputeKpis();
    }

    // Re-filter / re-sort on any state change.
    partial void OnGameModeFilterChanged(string value) => Reapply();
    partial void OnMapFilterChanged(string value) => Reapply();
    partial void OnWinOnlyChanged(bool value) => Reapply();
    partial void OnLossOnlyChanged(bool value) => Reapply();
    partial void OnTankOnlyChanged(bool value) => Reapply();
    partial void OnDamageOnlyChanged(bool value) => Reapply();
    partial void OnSupportOnlyChanged(bool value) => Reapply();
    partial void OnSearchTextChanged(string value) => Reapply();

    partial void OnMapTypeFilterChanged(string value)
    {
        MapOptions = BuildMapOptions();
        if (MapFilter != "all" && MapRoster.ModeOf(MapFilter) != value && value != "all")
            MapFilter = "all"; // selected map no longer in the chosen type
        Reapply();
    }

    partial void OnSortKeyChanged(string value) { ApplySort(); MatchesView.Refresh(); }
    partial void OnSortDescendingChanged(bool value) { ApplySort(); MatchesView.Refresh(); }

    private void Reapply()
    {
        MatchesView.Refresh();
        RecomputeKpis();
        OnPropertyChanged(nameof(AnyFilterActive));
    }

    private bool FilterCard(object obj)
    {
        if (obj is not MatchCardViewModel c) return false;

        if (GameModeFilter != "all" && c.GameModeLabel != GameModeFilter) return false;
        if (MapTypeFilter != "all" && c.Mode != MapTypeFilter) return false;
        if (MapFilter != "all" && !string.Equals(c.Match.MapName, MapFilter, StringComparison.OrdinalIgnoreCase)) return false;

        if (WinOnly || LossOnly)
        {
            var ok = (WinOnly && c.Outcome == MatchOutcome.Win) || (LossOnly && c.Outcome == MatchOutcome.Loss);
            if (!ok) return false;
        }

        if (TankOnly || DamageOnly || SupportOnly)
        {
            var ok = (TankOnly && c.PrimaryRole == "tank")
                  || (DamageOnly && c.PrimaryRole == "damage")
                  || (SupportOnly && c.PrimaryRole == "support");
            if (!ok) return false;
        }

        if (!string.IsNullOrWhiteSpace(SearchText) && !c.SearchKey.Contains(SearchText.ToLowerInvariant()))
            return false;

        return true;
    }

    private void ApplySort()
    {
        if (MatchesView is ListCollectionView lcv)
            lcv.CustomSort = new CardComparer(SortKey, SortDescending);
    }

    private void RecomputeKpis()
    {
        int shown = 0, wins = 0, losses = 0;
        foreach (var item in MatchesView)
        {
            if (item is not MatchCardViewModel c) continue;
            shown++;
            if (c.Outcome == MatchOutcome.Win) wins++;
            else if (c.Outcome == MatchOutcome.Loss) losses++;
        }
        ShownCount = shown;
        ShownWins = wins;
        ShownLosses = losses;
        ShownWinRate = shown > 0 ? (double)wins / shown : 0;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        GameModeFilter = "all";
        MapTypeFilter = "all";
        MapFilter = "all";
        WinOnly = LossOnly = TankOnly = DamageOnly = SupportOnly = false;
        SearchText = string.Empty;
    }

    [RelayCommand]
    private void ToggleSortDirection() => SortDescending = !SortDescending;

    private sealed class CardComparer : IComparer
    {
        private readonly string _key;
        private readonly int _dir;
        public CardComparer(string key, bool desc) { _key = key; _dir = desc ? -1 : 1; }

        public int Compare(object? x, object? y)
        {
            if (x is not MatchCardViewModel a || y is not MatchCardViewModel b) return 0;
            var cmp = _key switch
            {
                "length" => a.Length.CompareTo(b.Length),
                "kda"    => a.Kda.CompareTo(b.Kda),
                "sr"     => (a.Sr ?? int.MinValue).CompareTo(b.Sr ?? int.MinValue),
                _        => a.Match.MatchDatetime.CompareTo(b.Match.MatchDatetime),
            };
            return cmp * _dir;
        }
    }
}
