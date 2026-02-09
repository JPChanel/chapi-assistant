using System.Windows.Controls;
using Chapi.Presentation.ViewModels;

namespace Chapi.Presentation.Views.Tabs;

public partial class ReleasesView : UserControl
{
    private ReleasesViewModel? _viewModel => DataContext as ReleasesViewModel;

    public ReleasesView()
    {
        InitializeComponent();
    }
}
