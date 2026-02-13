using Chapi.Presentation.ViewModels;
using System.Windows.Controls;

namespace Chapi.Presentation.Views.Tabs;

public partial class AssistantView : UserControl
{
    private AssistantViewModel? _viewModel => DataContext as AssistantViewModel;

    public AssistantView()
    {
        InitializeComponent();
        
        Loaded += (s, e) =>
        {
            if (_viewModel != null)
            {
                _viewModel.ScrollToBottom += ScrollToBottomHandler;
            }
            
            // Focus en el input al cargar
            MessageInput.Focus();
        };

        Unloaded += (s, e) =>
        {
            if (_viewModel != null)
            {
                _viewModel.ScrollToBottom -= ScrollToBottomHandler;
            }
        };
    }

    private void ScrollToBottomHandler()
    {
        Dispatcher.InvokeAsync(() =>
        {
            ChatScrollViewer.ScrollToEnd();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }
}
