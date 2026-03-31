using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using Chapi.Presentation.Features.Workspace.ViewModels;

namespace Chapi.Presentation.Features.Workspace.Views;

public partial class WorkspaceView : UserControl
{
    public WorkspaceView()
    {
        InitializeComponent();
    }

    private void BtnAddAsset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Seleccionar archivos de despliegue",
            Filter = "Todos los archivos (*.*)|*.*|SQL Scripts (*.sql)|*.sql|Config (*.env;*.config)|*.env;*.config"
        };

        if (dialog.ShowDialog() == true)
        {
            if (DataContext is WorkspaceViewModel vm)
            {
                foreach (var file in dialog.FileNames)
                {
                    vm.AddAssetCommand.Execute(file);
                }
            }
        }
    }
}
