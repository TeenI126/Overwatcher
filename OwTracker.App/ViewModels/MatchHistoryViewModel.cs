using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OwTracker.Core.Models;
using OwTracker.Core.Repositories.Interfaces;

namespace OwTracker.App.ViewModels;

/// <summary>Match History tab: a sortable list of scraped matches plus a detail pane that shows
/// everything captured for the selected match (both teams' stats and my per-hero playtimes).</summary>
public sealed partial class MatchHistoryViewModel : ObservableObject
{
    private readonly IMatchRepository _matchRepository;

    public ObservableCollection<MatchRecord> Matches { get; } = new();

    /// <summary>Players on my team for the selected match (highlighting the IsMe row in the UI).</summary>
    public ObservableCollection<PlayerRecord> MyTeam { get; } = new();

    /// <summary>Players on the enemy team for the selected match.</summary>
    public ObservableCollection<PlayerRecord> EnemyTeam { get; } = new();

    /// <summary>My per-hero playtimes for the selected match (from the Personal tab).</summary>
    public ObservableCollection<HeroPlaytime> MyHeroes { get; } = new();

    /// <summary>The grid's selected row; changing it loads the full detail for that match.</summary>
    [ObservableProperty]
    private MatchRecord? _selectedMatch;

    /// <summary>The fully-loaded selected match (with players + playtimes eagerly included).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private MatchRecord? _detail;

    /// <summary>Whether the selected match captured any of my per-hero playtimes.</summary>
    [ObservableProperty]
    private bool _hasMyHeroes;

    public bool HasDetail => Detail is not null;

    public MatchHistoryViewModel(IMatchRepository matchRepository)
        => _matchRepository = matchRepository;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var selectedId = SelectedMatch?.Id;

        var matches = await _matchRepository.GetAllAsync();
        Matches.Clear();
        foreach (var match in matches)
            Matches.Add(match);

        // Preserve the selection across a refresh where possible.
        SelectedMatch = selectedId is null
            ? null
            : Matches.FirstOrDefault(m => m.Id == selectedId.Value);
    }

    partial void OnSelectedMatchChanged(MatchRecord? value)
        => _ = LoadDetailAsync(value);

    /// <summary>Loads the full record (players + my hero playtimes) for the selected match and
    /// splits it into the per-team / my-heroes collections the detail pane binds to.</summary>
    private async Task LoadDetailAsync(MatchRecord? selected)
    {
        MyTeam.Clear();
        EnemyTeam.Clear();
        MyHeroes.Clear();

        if (selected is null)
        {
            Detail = null;
            return;
        }

        var full = await _matchRepository.GetByIdAsync(selected.Id);
        Detail = full;
        if (full is null)
            return;

        // Enemy first then me? No — keep capture order, my team on top to mirror the in-game board.
        foreach (var p in full.AllPlayers.Where(p => p.Team == "My Team"))
            MyTeam.Add(p);
        foreach (var p in full.AllPlayers.Where(p => p.Team != "My Team"))
            EnemyTeam.Add(p);

        var me = full.AllPlayers.FirstOrDefault(p => p.IsMe);
        if (me is not null)
            foreach (var h in me.HeroPlaytimes.OrderByDescending(h => h.TimePlayed))
                MyHeroes.Add(h);

        HasMyHeroes = MyHeroes.Count > 0;
    }
}
