using System.Windows.Controls;
using Chapi.Presentation.ViewModels;

namespace Chapi.Presentation.Views.Tabs;

public partial class AssistantView : UserControl
{
    private AssistantViewModel _viewModel => DataContext as AssistantViewModel;

    public AssistantView()
    {
        InitializeComponent();
    }
}

