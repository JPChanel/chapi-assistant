using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Chapi.Presentation.Features.Releases.ViewModels;

namespace Chapi.Presentation.Features.Releases.Views;

public partial class ReleasesView : UserControl
{
    private ReleasesViewModel? _viewModel => DataContext as ReleasesViewModel;

    public ReleasesView()
    {
        InitializeComponent();
    }

    private void ReleaseFilesListView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;

        var wheelEvent = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender
        };

        ReleaseStatsScrollViewer.RaiseEvent(wheelEvent);
    }
}
