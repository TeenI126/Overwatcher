using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OwTracker.Core.Models;
using OwTracker.Core.Repositories.Interfaces;

namespace OwTracker.App.ViewModels;

/// <summary>Sessions tab: list of recorded play sessions with active vs. total durations.</summary>
public sealed partial class SessionViewModel : ObservableObject
{
    private readonly ISessionRepository _sessionRepository;

    public ObservableCollection<SessionRecord> Sessions { get; } = new();

    public SessionViewModel(ISessionRepository sessionRepository)
        => _sessionRepository = sessionRepository;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var sessions = await _sessionRepository.GetAllAsync();
        Sessions.Clear();
        foreach (var session in sessions)
            Sessions.Add(session);
    }
}
