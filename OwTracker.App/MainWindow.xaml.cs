using System.ComponentModel;
using System.Windows;
using OwTracker.App.ViewModels;

namespace OwTracker.App;

public partial class MainWindow : Window
{
    internal bool AllowClose { get; set; }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
