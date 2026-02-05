using Chapi.Presentation.ViewModels;
using System.Windows.Controls;

namespace Chapi.Presentation.Views.Dialogs;

public partial class LoginGitHubDialog : UserControl
{
    public LoginGitHubDialog(LoginGitHubViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
