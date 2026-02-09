using Chapi.Infrastructure.Services;
using System.Windows;
using System.Windows.Controls;

using Chapi.Infrastructure.Persistence.Rollbacks;
namespace Chapi.Presentation.Views.Agent
{
    public partial class RollbackSelectorView : Window
    {
        private List<RollbackManager.RollbackEntry> _rollbacks;

        public RollbackSelectorView()
        {
            InitializeComponent();
            LoadRollbacks();
        }

        private void LoadRollbacks()
        {
            _rollbacks = RollbackManager.GetAvailableRollbacks();

            if (_rollbacks.Count == 0)
            {
                listBox.Items.Add("No hay rollbacks disponibles");
                btnAceptar.IsEnabled = false;
                return;
            }

            foreach (var rollback in _rollbacks)
            {
                var displayText = $"[{rollback.Operation}] {rollback.Module}.{rollback.MethodName} - {rollback.CreatedAt:yyyy-MM-dd HH:mm:ss} ({rollback.Changes.Count} cambios)";
                listBox.Items.Add(new RollbackListItem
                {
                    DisplayText = displayText,
                    Entry = rollback
                });
            }


        }

        private async void btnAceptar_Click(object sender, RoutedEventArgs e)
        {
            if (listBox.SelectedItem is not RollbackListItem selected)
            {
                DialogService.ShowTrayNotification("Atencion", "Seleccione un rollback");
                return;
            }

            var entry = selected.Entry;

            // Mostrar detalles de lo que se va a revertir
            var details = string.Join("\n", entry.Changes.Select(c =>
                $"  â€¢ {c.ChangeType}: {System.IO.Path.GetFileName(c.FilePath)}"
            ));

            var confirm = await DialogService.ShowConfirmDialog(
                "Confirmar Rollback",
                $"Â¿Desea revertir los siguientes cambios?\n\nModulo: {entry.Module}\nMetodo: {entry.MethodName}\nOperacion: {entry.Operation}\n\n{details}",
                Dialogs.DialogVariant.Warning,
                Dialogs.DialogType.Confirm
            );

            if (!confirm)
            {
                return;
            }

            try
            {
                var rollbackFilePath = RollbackManager.GetRollbackFilePathForEntry(entry);
                // TODO: Fix ExecuteRollback call - needs RollbackEntry, not string
                // 

                await DialogService.ShowConfirmDialog(
                    " ‰xito",
                    $"âœ… Rollback completado exitosamente.\n\nMetodo '{entry.MethodName}' revertido del modulo '{entry.Module}'",
                    Dialogs.DialogVariant.Success,
                    Dialogs.DialogType.Info
                );

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                await DialogService.ShowConfirmDialog(
                    "Error",
                    $"âŒ Error al ejecutar rollback:\n{ex.Message}",
                    Dialogs.DialogVariant.Error,
                    Dialogs.DialogType.Info
                );
            }
        }

        private void btnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void btnLimpiarAntiguos_Click(object sender, RoutedEventArgs e)
        {
            RollbackManager.CleanOldRollbacks(30);
            listBox.Items.Clear();
            LoadRollbacks();
            DialogService.ShowTrayNotification("Limpieza", "Rollbacks antiguos eliminados");
        }

        private void listBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (listBox.SelectedItem is RollbackListItem selected)
            {
                // Mostrar detalles de los cambios
                var details = string.Join("\n", selected.Entry.Changes.Select(c =>
                    $"  â€¢ {c.ChangeType}: {System.IO.Path.GetFileName(c.FilePath)}"
                ));

                txtDetails.Text = details;
                detailsPanel.Visibility = Visibility.Visible;
            }
            else
            {
                detailsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private class RollbackListItem
        {
            public string DisplayText { get; set; }
            public RollbackManager.RollbackEntry Entry { get; set; }
        }
    }
}





