using Chapi.Presentation.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Chapi.Presentation.Views.Dialogs;

public partial class ConflictResolutionDialog : UserControl
{
    public ConflictResolutionDialog(ConflictResolutionViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Si el ViewModel solicita cerrar, cerramos el Host del dialogo de MaterialDesign
        viewModel.RequestClose += (s, e) =>
        {
            if (MaterialDesignThemes.Wpf.DialogHost.IsDialogOpen(null))
            {
                MaterialDesignThemes.Wpf.DialogHost.Close(null);
            }
        };
    }
}
