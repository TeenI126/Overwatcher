using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OwTracker.App.Navigation;
using OwTracker.Core.Models;
using OwTracker.Core.Repositories.Interfaces;
using OwTracker.Core.Services;
using OwTracker.Core.Stats;

namespace OwTracker.App.ViewModels;

/// <summary>One heatmap cell (a hero×map pairing).</summary>
public sealed record HeatCell(string Map, bool HasData, double WinRate, int Games, bool Low);

/// <summary>One heatmap row: a hero, its per-map cells, and its overall record.</summary>
public sealed record HeatRow(HeroStat Hero, IReadOnlyList<HeatCell> Cells);

/// <summary>A selectable mode-filter chip.</summary>
public sealed partial class ModeChip : ObservableObject
{
    public string Name { get; }
    [ObservableProperty] private bool _isActive;
    public ModeChip(string name) => Name = name;
}

/// <summary>
/// Hero × Map analytics (new screen). Pure aggregation over stored matches — heatmap, By-Map and
/// By-Hero master-detail. Honours deep-link intents from the Dashboard via <see cref="NavigationService"/>.
/// </summary>
public sealed partial class HeroMapViewModel : ObservableObject
{
    private readonly IMatchRepository _matchRepository;
    private IReadOnlyList<MatchRecord> _matches = Array.Empty<MatchRecord>();
    private Dictionary<(string Hero, string Map), HeroMapCell> _matrix = new();

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Crumb))]
    [NotifyPropertyChangedFor(nameof(ShowModeFilter))]
    private HeroMapView _view = HeroMapView.Heatmap;

    public ObservableCollection<ModeChip> ModeChips { get; } = new();

    // Heatmap
    public ObservableCollection<HeatRow> HeatRows { get; } = new();
    public ObservableCollection<MapStat> HeatMaps { get; } = new();

    // By Map
    public ObservableCollection<MapStat> MapList { get; } = new();
    public ObservableCollection<RankedRow> MapHeroRows { get; } = new();
    [ObservableProperty] private MapStat? _selectedMap;

    // By Hero
    public ObservableCollection<HeroStat> HeroList { get; } = new();
    public ObservableCollection<RankedRow> HeroMapRows { get; } = new();
    [ObservableProperty] private HeroStat? _selectedHero;

    public bool ShowModeFilter => View != HeroMapView.ByHero;

    public string Crumb => View switch
    {
        HeroMapView.Heatmap => "win rate by hero on each map",
        HeroMapView.ByMap   => "your heroes, per map",
        _                   => "your maps, per hero",
    };

    public HeroMapViewModel(IMatchRepository matchRepository, NavigationService nav)
    {
        _matchRepository = matchRepository;
        foreach (var m in MapRoster.Modes)
        {
            var chip = new ModeChip(m);
            chip.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ModeChip.IsActive)) RebuildFiltered(); };
            ModeChips.Add(chip);
        }
        nav.HeroMapRequested += OnIntent;
    }

    private IReadOnlyList<string> ActiveModes => ModeChips.Where(c => c.IsActive).Select(c => c.Name).ToList();
    public bool AllModesActive => ModeChips.All(c => !c.IsActive);

    private void OnIntent(HeroMapIntent intent)
    {
        View = intent.View;
        if (intent.Map is not null)
            SelectedMap = MapList.FirstOrDefault(m => m.Map == intent.Map)
                          ?? StatsService.MapTotals(_matches).FirstOrDefault(m => m.Map == intent.Map);
        if (intent.Hero is not null)
            SelectedHero = HeroList.FirstOrDefault(h => h.Name == intent.Hero)
                           ?? StatsService.HeroTotals(_matches).FirstOrDefault(h => h.Name == intent.Hero);
        RebuildFiltered();
    }

    public async Task RefreshAsync()
    {
        _matches = await _matchRepository.GetAllWithDetailsAsync();
        _matrix = StatsService.BuildMatrix(_matches);
        RebuildFiltered();
    }

    partial void OnViewChanged(HeroMapView value) => RebuildFiltered();
    partial void OnSelectedMapChanged(MapStat? value) => RebuildMapDetail();
    partial void OnSelectedHeroChanged(HeroStat? value) => RebuildHeroDetail();

    [RelayCommand] private void ShowHeatmap() => View = HeroMapView.Heatmap;
    [RelayCommand] private void ShowByMap() => View = HeroMapView.ByMap;
    [RelayCommand] private void ShowByHero() => View = HeroMapView.ByHero;
    [RelayCommand] private void ClearModes() { foreach (var c in ModeChips) c.IsActive = false; }
    [RelayCommand] private void ToggleMode(ModeChip? chip) { if (chip is not null) chip.IsActive = !chip.IsActive; }

    [RelayCommand]
    private void PickMap(string? map)
    {
        if (string.IsNullOrEmpty(map)) return;
        View = HeroMapView.ByMap;
        SelectedMap = MapList.FirstOrDefault(m => m.Map == map)
                      ?? StatsService.MapTotals(_matches).FirstOrDefault(m => m.Map == map);
        RebuildFiltered();
    }

    private void RebuildFiltered()
    {
        OnPropertyChanged(nameof(AllModesActive));
        var modes = ActiveModes;
        bool ModeOk(string mode) => modes.Count == 0 || modes.Contains(mode);

        // Heatmap
        var maps = StatsService.MapTotals(_matches).Where(m => ModeOk(m.Mode))
            .OrderByDescending(m => m.Games).Take(12).ToList();
        HeatMaps.Clear();
        foreach (var m in maps) HeatMaps.Add(m);

        var heroes = StatsService.HeroTotals(_matches);
        HeatRows.Clear();
        foreach (var h in heroes)
        {
            var cells = maps.Select(mp =>
            {
                if (_matrix.TryGetValue((h.Name, mp.Map), out var c))
                    return new HeatCell(mp.Map, true, c.WinRate, c.Games, c.Games < 3);
                return new HeatCell(mp.Map, false, 0, 0, false);
            }).ToList();
            HeatRows.Add(new HeatRow(h, cells));
        }

        // By Map list
        var mapList = StatsService.MapTotals(_matches).Where(m => ModeOk(m.Mode)).ToList();
        MapList.Clear();
        foreach (var m in mapList) MapList.Add(m);
        if (SelectedMap is null || MapList.All(m => m.Map != SelectedMap.Map))
            SelectedMap = MapList.FirstOrDefault();
        else
            RebuildMapDetail();

        // By Hero list
        HeroList.Clear();
        foreach (var h in heroes) HeroList.Add(h);
        if (SelectedHero is null || HeroList.All(h => h.Name != SelectedHero.Name))
            SelectedHero = HeroList.FirstOrDefault();
        else
            RebuildHeroDetail();
    }

    private void RebuildMapDetail()
    {
        MapHeroRows.Clear();
        if (SelectedMap is null) return;
        foreach (var r in StatsService.MapHeroes(_matches, SelectedMap.Map)) MapHeroRows.Add(r);
    }

    private void RebuildHeroDetail()
    {
        HeroMapRows.Clear();
        if (SelectedHero is null) return;
        foreach (var r in StatsService.HeroMaps(_matches, SelectedHero.Name)) HeroMapRows.Add(r);
    }
}
