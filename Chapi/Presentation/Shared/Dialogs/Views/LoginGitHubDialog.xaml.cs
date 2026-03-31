using Chapi.Presentation.Features.Git.ViewModels;
using System.Windows.Controls;

namespace Chapi.Presentation.Shared.Dialogs.Views;

public partial class LoginGitHubDialog : UserControl
{
    public LoginGitHubDialog(LoginGitHubViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
