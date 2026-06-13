using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OwTracker.Core.Repositories.Interfaces;
using OwTracker.Core.Stats;

namespace OwTracker.App.ViewModels;

/// <summary>
/// Sessions tab: play sessions derived from matches by grouping on 1h+ inactivity gaps (the live
/// <c>SessionRecord</c> table only stores open/active durations and isn't match-linked, so the rich
/// per-session view — timeline, win-rate trend, tilt, heroes — is computed from matches per the
/// design's option A).
/// </summary>
public sealed partial class SessionViewModel : ObservableObject
{
    private readonly IMatchRepository _matchRepository;

    public ObservableCollection<SessionCardViewModel> Sessions { get; } = new();

    [ObservableProperty] private int _sessionCount;
    [ObservableProperty] private string _owOpenText = "0m";
    [ObservableProperty] private int _inGamePercent;
    [ObservableProperty] private int _roughCount;

    public SessionViewModel(IMatchRepository matchRepository)
        => _matchRepository = matchRepository;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var matches = await _matchRepository.GetAllWithDetailsAsync();
        var sessions = StatsService.BuildSessions(matches, gapMinutes: 60);

        Sessions.Clear();
        foreach (var s in sessions)
            Sessions.Add(new SessionCardViewModel(s));

        SessionCount = sessions.Count;
        var totalOpen = sessions.Aggregate(System.TimeSpan.Zero, (a, s) => a + s.Open);
        var totalIn = sessions.Aggregate(System.TimeSpan.Zero, (a, s) => a + s.InGame);
        OwOpenText = FormatDur(totalOpen);
        InGamePercent = totalOpen.TotalSeconds > 0
            ? (int)System.Math.Round(totalIn.TotalSeconds / totalOpen.TotalSeconds * 100) : 0;
        RoughCount = sessions.Count(s => s.Tilt);
    }

    private static string FormatDur(System.TimeSpan t)
    {
        var h = (int)t.TotalHours;
        var m = (int)System.Math.Round((t.TotalSeconds - h * 3600) / 60);
        return h > 0 ? $"{h}h {m}m" : $"{m}m";
    }
}
