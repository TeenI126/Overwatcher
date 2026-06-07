using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OwTracker.Core.Models;
using OwTracker.Core.Repositories.Interfaces;
using OwTracker.Core.Services.Interfaces;

namespace OwTracker.App.ViewModels;

/// <summary>
/// Hero Review tab: queue of <see cref="PendingHeroLabel"/> items needing confirmation, plus
/// the canonical hero list for the correction dropdown (design §8). Retrain is stubbed until
/// the training pipeline lands.
/// </summary>
public sealed partial class HeroReviewViewModel : ObservableObject
{
    private readonly IHeroLabelRepository _labelRepository;

    public ObservableCollection<PendingHeroLabel> PendingLabels { get; } = new();
    public IReadOnlyList<string> HeroNames { get; }

    [ObservableProperty]
    private int _unreviewedCount;

    public HeroReviewViewModel(IHeroLabelRepository labelRepository, IHeroRosterProvider rosterProvider)
    {
        _labelRepository = labelRepository;
        HeroNames = rosterProvider.GetHeroNames();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var labels = await _labelRepository.GetUnreviewedAsync();
        PendingLabels.Clear();
        foreach (var label in labels)
            PendingLabels.Add(label);
        UnreviewedCount = PendingLabels.Count;
    }

    [RelayCommand]
    private async Task ConfirmAsync(PendingHeroLabel? label)
    {
        if (label is null)
            return;

        var hero = string.IsNullOrWhiteSpace(label.ConfirmedHero)
            ? label.PredictedHero
            : label.ConfirmedHero;

        await _labelRepository.ConfirmAsync(label.Id, hero);
        await RefreshAsync();
    }
}
