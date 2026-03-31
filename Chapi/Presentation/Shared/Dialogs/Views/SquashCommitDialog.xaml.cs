using Chapi.Application.UseCases.AI;
using Chapi.Domain.Interfaces;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace Chapi.Presentation.Shared.Dialogs.Views
{
    public partial class SquashCommitDialog : UserControl
    {
        public string CommitMessage => MessageTextBox.Text;
        private readonly IGitRepository _gitRepository;
        private readonly string _projectPath;
        private readonly string _sourceBranch;
        private readonly string _targetBranch;

        public SquashCommitDialog(IGitRepository gitRepository, string projectPath, string sourceBranch, string targetBranch, bool autoDeleteBranch)
        {
            InitializeComponent();
            _gitRepository = gitRepository;
            _projectPath = projectPath;
            _sourceBranch = sourceBranch;
            _targetBranch = targetBranch;
            // autoDeleteBranch can be used here if we want to display a readonly indicator in the future

            // Set default message
            MessageTextBox.Text = $"SM: Squash merge from '{sourceBranch}'";
            MessageTextBox.Focus();
            MessageTextBox.SelectAll();
        }

        private async void GenerateAIButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetBusy(true);
                StatusText.Text = "Obteniendo diferencias entre ramas...";

                // 1. Obtener diff consolidado entre ramas
                string diff = await _gitRepository.GetBranchDiffAsync(_projectPath, _sourceBranch, _targetBranch);

                if (string.IsNullOrWhiteSpace(diff))
                {
                    StatusText.Text = "No hay cambios suficientes para generar un mensaje.";
                    SetBusy(false);
                    return;
                }

                StatusText.Text = "Generando resumen con IA...";

                // 2. Llamar a la IA usando el UseCase correspondiente
                var useCase = App.ServiceProvider.GetRequiredService<GenerateCommitMessageUseCase>();
                
                // Nota: GenerateCommitMessageUseCase espera 'diff' como único argumento string
                // y retorna un Result<CommitMessageResponse> o similar.
                // Verificamos su firma en uso (normalmente ExecuteAsync(diff)).
                // Adaptamos la llamada:
                var result = await useCase.ExecuteAsync(diff);

                if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Data))
                {
                    try 
                    {
                        var jsonResponse = result.Data;
                        // Limpiar markdown si existe
                        if (jsonResponse.StartsWith("```json"))
                        {
                            jsonResponse = jsonResponse.Replace("```json", "").Replace("```", "").Trim();
                        }
                        
                         var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                         var commitMsg = System.Text.Json.JsonSerializer.Deserialize<Chapi.Domain.Entities.CommitMessageResponse>(jsonResponse, options);

                         if (commitMsg != null && !string.IsNullOrWhiteSpace(commitMsg.Summary))
                         {
                             // Formato consolidado para squash con prefijo SM:
                             string fullMsg = $"SM: {commitMsg.Summary}\n\n{commitMsg.Description}".Trim();
                             MessageTextBox.Text = fullMsg;
                             StatusText.Text = "✅ Mensaje generado.";
                         }
                         else
                         {
                             // Fallback si la deserialización falla o devuelve nulo
                             MessageTextBox.Text = $"SM: {jsonResponse}";
                             StatusText.Text = "✅ Mensaje generado (formato libre).";
                         }
                    }
                    catch
                    {
                         // Fallback si no es JSON válido
                         MessageTextBox.Text = $"SM: {result.Data}";
                         StatusText.Text = "✅ Mensaje generado (formato libre).";
                    }
                }
                else
                {
                    StatusText.Text = $"⚠️ Error IA: {result.Error ?? "Sin respuesta"}";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Error: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool isBusy)
        {
            AIProgressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            GenerateAIButton.IsEnabled = !isBusy;
            AcceptButton.IsEnabled = !isBusy;
            MessageTextBox.IsEnabled = !isBusy;
        }

        private void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(MessageTextBox.Text))
            {
                StatusText.Text = "⚠️ El mensaje no puede estar vacío.";
                return;
            }

            DialogHost.CloseDialogCommand.Execute(true, this);
        }
    }
}
