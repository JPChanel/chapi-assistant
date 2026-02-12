using Chapi.Presentation.ViewModels;
using System.Windows.Controls;

namespace Chapi.Presentation.Views.Tabs;

public partial class ReleasesView : UserControl
{
    private ReleasesViewModel? _viewModel => DataContext as ReleasesViewModel;

    public ReleasesView()
    {
        InitializeComponent();
    }
}
