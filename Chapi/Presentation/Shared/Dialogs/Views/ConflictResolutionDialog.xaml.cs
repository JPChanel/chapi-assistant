using Chapi.Presentation.Features.Git.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Chapi.Presentation.Shared.Dialogs.Views;

public partial class ConflictResolutionDialog : UserControl
{
    public ConflictResolutionDialog(ConflictResolutionViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Si el ViewModel solicita cerrar, cerramos el Host del dialogo de MaterialDesign
        viewModel.RequestClose += (s, e) =>
        {
            if (MaterialDesignThemes.Wpf.DialogHost.IsDialogOpen(App.GlobalDialogIdentifier))
            {
                MaterialDesignThemes.Wpf.DialogHost.Close(App.GlobalDialogIdentifier);
            }
        };
    }
}
