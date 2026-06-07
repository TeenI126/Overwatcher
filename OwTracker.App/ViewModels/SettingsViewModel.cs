using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OwTracker.Core;

namespace OwTracker.App.ViewModels;

/// <summary>
/// Settings tab: self-training thresholds (design §6.6) and data-folder access. Thresholds are
/// surfaced now; they are consumed by the PseudoLabelPipeline in a later milestone.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private double _autoAcceptThreshold = 0.97;

    [ObservableProperty]
    private double _reviewThreshold = 0.70;

    [ObservableProperty]
    private int _retrainBatchSize = 50;

    [RelayCommand]
    private void OpenTrainingDataFolder()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = AppPaths.CropsDirectory,
            UseShellExecute = true
        });
    }
}
