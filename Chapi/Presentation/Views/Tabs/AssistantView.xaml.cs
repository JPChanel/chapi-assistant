using Chapi.Presentation.ViewModels;
using System.Windows.Controls;

namespace Chapi.Presentation.Views.Tabs;

public partial class AssistantView : UserControl
{
    private AssistantViewModel _viewModel => DataContext as AssistantViewModel;

    public AssistantView()
    {
        InitializeComponent();
    }
}

