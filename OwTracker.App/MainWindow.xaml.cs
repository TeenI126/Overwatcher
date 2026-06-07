using System.Windows;
using OwTracker.App.ViewModels;

namespace OwTracker.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
