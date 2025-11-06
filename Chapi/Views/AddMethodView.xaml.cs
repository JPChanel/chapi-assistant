using AI.Clients;
using Chapi.Helper.AI;
using Chapi.Helper.Roslyn;
using Chapi.Model;
using Chapi.Services;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Chapi.Views
{
    public partial class AddMethodView : Window
    {
        private string _projectDirectory;
        private bool isGenerateByIA = false;
        private SPAnalysisResult _aiResult;
        public AddMethodView(string projectDirectory)
        {
            InitializeComponent();
            _projectDirectory = projectDirectory;
            cbobd.SelectedIndex = 0;
        }
        /// <summary>
        /// Vuelve a centrar esta ventana en relación con su Dueño (MainWindow).
        /// </summary>
        private void RecenterWindow()
        {
            if (this.Owner != null && this.IsLoaded)
            {
                this.UpdateLayout();
                this.Top = this.Owner.Top + (this.Owner.ActualHeight - this.ActualHeight) / 2;
            }
        }
        private async void btnCrear_Click(object sender, RoutedEventArgs e)
        {
            var modulo = txtModulo.Text.Trim();
            var metodo = txtMetodo.Text.Trim();
            metodo = string.IsNullOrEmpty(metodo) ? modulo : metodo;
            var bd = (cbobd.SelectedItem as ComboBoxItem)?.Content.ToString();
            if (string.IsNullOrEmpty(modulo))
            {
                await DialogService.ShowConfirmDialog("Alerta", "Ingrese Nombre de Módulo",
                    Dialogs.DialogVariant.Warning, Dialogs.DialogType.Info);
                return;
            }

            // 2. Forzar que el Módulo empiece con mayúscula
            modulo = char.ToUpper(modulo[0]) + modulo.Substring(1);
            txtModulo.Text = modulo; 

            metodo = string.IsNullOrEmpty(metodo) ? modulo : metodo;
            // 4. Forzar que el Método empiece con mayúscula
            metodo = char.ToUpper(metodo[0]) + metodo.Substring(1);
            txtMetodo.Text = metodo;

            if (string.IsNullOrEmpty(bd))
            {
                await DialogService.ShowConfirmDialog("Alerta", "Seleccione Base de Datos",
                    Dialogs.DialogVariant.Warning, Dialogs.DialogType.Info);
                return;
            }
            // 📦 Recolectar métodos seleccionados
            var metodos = new List<string>();
            if (checkPost.IsChecked == true) metodos.Add("Post");
            if (checkGet.IsChecked == true) metodos.Add("Get");
            if (checkGetById.IsChecked == true) metodos.Add("GetById");

            if (metodos.Count == 0)
            {
                await DialogService.ShowConfirmDialog("Alerta", "Seleccione al menos un Método",
                    Dialogs.DialogVariant.Warning, Dialogs.DialogType.Info);
                return;
            }
            // 🧩 Si el modo IA está activo
            if (isGenerateByIA)
            {
                // Actualiza los valores editados manualmente por el usuario
                _aiResult.StoredProcedureName = txtSPName.Text.Trim();

                _aiResult.RequestParameters = txtRequestParams.Text
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .ToList();

                _aiResult.Parameters = txtParameters.Text
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .ToList();

                _aiResult.DTOFields = txtDTOFields.Text
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .ToList();

                _aiResult.ResponseMapper = txtResponseMapper.Text
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .ToList();

                // ⚙️ Ejecutar generación por IA
                await ExecuteMethodGeneration(modulo, metodo, bd, metodos, _aiResult);
                return;
            }
            // ⚙️ Ejecutar generación manual
            await ExecuteMethodGeneration(modulo, metodo, bd, metodos, null);
        }

        #region 🤖 Modo Avanzado con IA

        private async void btnAnalyze_Click(object sender, RoutedEventArgs e)
        {
            var modulo = txtModulo.Text.Trim();
            var nombreMetodo = txtMetodo.Text.Trim();
            nombreMetodo = string.IsNullOrEmpty(nombreMetodo) ? modulo : nombreMetodo;
            var emailContent = txtEmailContent.Text.Trim();
            var bd = (cbobd.SelectedItem as ComboBoxItem)?.Content.ToString();
            if (string.IsNullOrEmpty(modulo) || string.IsNullOrEmpty(nombreMetodo))
            {
                await DialogService.ShowConfirmDialog("Validación", "Complete el módulo y método",
                    Dialogs.DialogVariant.Warning, Dialogs.DialogType.Info);
                return;
            }

            if (string.IsNullOrEmpty(emailContent))
            {
                await DialogService.ShowConfirmDialog("Validación", "Pegue el contenido del correo técnico",
                    Dialogs.DialogVariant.Warning, Dialogs.DialogType.Info);

                return;
            }
            if (string.IsNullOrEmpty(bd))
            {
                await DialogService.ShowConfirmDialog("Alerta", "Seleccione Base de Datos",
                    Dialogs.DialogVariant.Warning, Dialogs.DialogType.Info);
                return;
            }

            var metodos = new List<string>();

            if (checkPost.IsChecked == true) metodos.Add("Post");
            if (checkGet.IsChecked == true) metodos.Add("Get");
            if (checkGetById.IsChecked == true) metodos.Add("GetById");

            if (metodos.Count == 0)
            {
                await DialogService.ShowConfirmDialog("Alerta", "Seleccione al menos un Método",
                    Dialogs.DialogVariant.Warning, Dialogs.DialogType.Info);
                return;
            }

            if (metodos.Count > 1)
            {
                await DialogService.ShowConfirmDialog("Alerta", "Solo puede seleccionar un método",
                    Dialogs.DialogVariant.Warning, Dialogs.DialogType.Info);
                return;
            }

            string tipoMetodo = metodos.First();
            // Cambiar a estado de análisis
            panelInput.Visibility = Visibility.Collapsed;
            panelAnalyzing.Visibility = Visibility.Visible;
            panelResult.Visibility = Visibility.Collapsed;

            try
            {
                // Llamar a la IA
                txtAnalyzingStatus.Text = "Enviando información a la IA...";
                var analysisResult = await AnalyzeEmailWithAI(emailContent, modulo, nombreMetodo, tipoMetodo, bd);

                if (analysisResult != null)
                {
                    _aiResult = analysisResult;
                    // Llenar los campos con el resultado
                    txtSPName.Text = analysisResult.StoredProcedureName;
                    txtRequestParams.Text = string.Join("\n", analysisResult.RequestParameters);
                    txtParameters.Text = string.Join("\n", analysisResult.Parameters);
                    txtDTOFields.Text = string.Join("\n", analysisResult.DTOFields);
                    txtResponseMapper.Text = string.Join(",\n", analysisResult.ResponseMapper);

                    // Mostrar resultado
                    panelAnalyzing.Visibility = Visibility.Collapsed;
                    panelResult.Visibility = Visibility.Visible;
                    btnVolverAnalizar.Visibility = Visibility.Visible;
                    btnCrear.Visibility = Visibility.Visible;
                    isGenerateByIA = true;
                }
                else
                {
                    throw new Exception("No se pudo analizar el correo");
                }
            }
            catch (Exception ex)
            {
                await DialogService.ShowConfirmDialog("Error", $"Error al analizar: {ex.Message}",
                 Dialogs.DialogVariant.Error, Dialogs.DialogType.Info);

                // Volver al input
                panelAnalyzing.Visibility = Visibility.Collapsed;
                panelInput.Visibility = Visibility.Visible;
                btnVolverAnalizar.Visibility = Visibility.Collapsed;
            }
        }
        private void btnBackToInput_Click(object sender, RoutedEventArgs e)
        {
            panelResult.Visibility = Visibility.Collapsed;
            panelInput.Visibility = Visibility.Visible;
            btnVolverAnalizar.Visibility = Visibility.Collapsed;
        }



        #endregion
        #region 🔧 Generación de Métodos (Común)

        private async Task ExecuteMethodGeneration(
            string modulo,
            string nombreMetodo,
            string bd,
            List<string> metodos,
            SPAnalysisResult aiResult)
        {
            string apiProjectName = Path.GetFileName(_projectDirectory);
            string apiPath = Path.Combine(_projectDirectory, apiProjectName, "Controllers", modulo);
            string appPath = Path.Combine(_projectDirectory, "Application", modulo);
            string domainPath = Path.Combine(_projectDirectory, "Domain", modulo);
            string infraPath = Path.Combine(_projectDirectory, "Infrastructure", bd, "Repositories", modulo);

            try
            {
                foreach (var metodo in metodos)
                {
                    var rollbackEntry = RollbackManager.StartTransaction(modulo, nombreMetodo, metodo);

                    try
                    {
                        rollbackEntry = AddApiControllerMethod.Add(apiPath, modulo, metodo, nombreMetodo, rollbackEntry);
                        rollbackEntry = AddApplicationMethod.Add(appPath, modulo, metodo, nombreMetodo, rollbackEntry);

                        // 🤖 SI HAY RESULTADO DE IA, USAR GENERACIÓN AVANZADA
                        if (aiResult != null)
                        {
                            rollbackEntry = await AddDomainMethod.Add(domainPath, modulo, metodo, nombreMetodo, rollbackEntry, aiResult);

                            rollbackEntry = await AddInfrastructureMethod.Add(
                                infraPath, modulo, bd, metodo, nombreMetodo, rollbackEntry, aiResult);
                        }
                        else
                        {
                            rollbackEntry = await AddDomainMethod.Add(domainPath, modulo, metodo, nombreMetodo, rollbackEntry);

                            rollbackEntry = await AddInfrastructureMethod.Add(
                                infraPath, modulo, bd, metodo, nombreMetodo, rollbackEntry);
                        }

                        // Dependency Injection
                        string dependencyInjectionPath = Path.Combine(
                            _projectDirectory, apiProjectName, "Config", "DependencyInjection.cs");

                        if (File.Exists(dependencyInjectionPath))
                        {
                            var diContent = File.ReadAllText(dependencyInjectionPath);
                            RollbackManager.RecordFileModification(rollbackEntry, dependencyInjectionPath, diContent);
                            AddDependencyInjection.Add(dependencyInjectionPath, nombreMetodo, new[] { metodo });
                        }

                        RollbackManager.CommitTransaction(rollbackEntry);
                    }
                    catch (Exception ex)
                    {
                        Msg.Assistant($"❌ Error al agregar método {metodo}: {ex.Message}");
                        var tempPath = RollbackManager.GetRollbackFilePathForEntry(rollbackEntry);
                        RollbackManager.CommitTransaction(rollbackEntry);
                        RollbackManager.ExecuteRollback(tempPath);
                        throw;
                    }
                }

                Msg.Assistant($"✅ Método {nombreMetodo} Agregado Correctamente en Módulo {modulo}");
                await DialogService.ShowConfirmDialog(
                    "Confirmación",
                    $"✅ Método generado exitosamente\n\nMódulo: {modulo}\nMétodo: {nombreMetodo}",
                    Dialogs.DialogVariant.Success,
                    Dialogs.DialogType.Info
                );

                this.Close();
            }
            catch (Exception ex)
            {
                await DialogService.ShowConfirmDialog(
                    "Error",
                    $"Error al generar método: {ex.Message}\nSe ha realizado rollback.",
                    Dialogs.DialogVariant.Error,
                    Dialogs.DialogType.Info
                );
            }
        }

        #endregion
        #region 🧠 IA Integration
        private async Task<SPAnalysisResult> AnalyzeEmailWithAI(
            string emailContent,
            string moduleName,
            string nombreMetodo, string dataBase, string tipoMetodo)
        {



            var prompt = GetPrompt.AnalyzeEmail(moduleName, nombreMetodo, emailContent, dataBase, tipoMetodo);

            try
            {
                var aiResponse = await AIClient.SendPromptAsync(prompt);

                if (aiResponse.StartsWith("```json"))
                {
                    aiResponse = aiResponse.Replace("```json", "").Replace("```", "").Trim();
                }

                // Parsear JSON
                var result = JsonSerializer.Deserialize<SPAnalysisResult>(aiResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return result;
            }
            catch (Exception ex)
            {
                Msg.Assistant($"❌ Error en análisis IA: {ex.Message}");
                throw new Exception($"No se pudo analizar el correo: {ex.Message}");
            }
        }

        #endregion
        private void chkUseIA_Checked(object sender, RoutedEventArgs e)
        {
            iaPanel.Visibility = Visibility.Visible;
            btnCrear.Visibility = Visibility.Collapsed;
            RecenterWindow();
        }

        private void chkUseIA_Unchecked(object sender, RoutedEventArgs e)
        {
            iaPanel.Visibility = Visibility.Collapsed;
            btnCrear.Visibility = Visibility.Visible;
            RecenterWindow();
        }
        private void btnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

    }
}