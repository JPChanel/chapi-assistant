using Chapi.Presentation.Features.Git.ViewModels;
using System.Windows;

namespace Chapi.Presentation.Shared.Dialogs.Views;

/// <summary>
/// Diálogo para seleccionar proveedor Git y autenticarse.
/// </summary>
public partial class GitProviderSelectionDialog : Window
{
    public GitProviderSelectionDialog(GitProviderSelectionViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
