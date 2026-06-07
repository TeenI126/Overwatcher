using System.Collections.Specialized;
using System.Windows.Controls;

namespace OwTracker.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        // Auto-scroll the log list to the latest entry whenever a new line is added.
        Loaded += (_, _) =>
        {
            if (DataContext is OwTracker.App.ViewModels.DashboardViewModel vm &&
                vm.ScrapeLog is INotifyCollectionChanged ncc)
            {
                ncc.CollectionChanged += (_, _) =>
                {
                    if (ScrapeLogList.Items.Count > 0)
                        ScrapeLogList.ScrollIntoView(
                            ScrapeLogList.Items[ScrapeLogList.Items.Count - 1]);
                };
            }
        };
    }
}
