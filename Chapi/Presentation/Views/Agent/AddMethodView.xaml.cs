using Chapi.Infrastructure.AI;
using Chapi.Infrastructure.AI;
using Chapi.Infrastructure.Roslyn;
using Chapi.Domain.Models;
using Chapi.Infrastructure.Services;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

using Chapi.Infrastructure.Persistence.Rollbacks;
using Chapi.Domain.Entities;
using Chapi.Infrastructure.Common;
namespace Chapi.Presentation.Views.Agent
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
            LoadModulesAndDetectStyle();
            cbobd.SelectedIndex = 0;
        }

        private void LoadModulesAndDetectStyle()
        {
            // 1. Cargar Modulos
            try 
            {
                var modules = FindApiDirectory.GetModuleDirectories(_projectDirectory);
                cboModulo.ItemsSource = modules;
            }
            catch (Exception ex)
            {
                // Log silencioso o ignorar
                Console.WriteLine(ex.Message);
            }

            // 2. Detectar Estilo de API (Si existe carpeta Endpoints -> Es Ardalis)
            string apiProjectName = Path.GetFileName(_projectDirectory);
            string endpointsPath = Path.Combine(_projectDirectory, apiProjectName, "Endpoints");
            
            if (Directory.Exists(endpointsPath))
            {
                rbEndpoint.IsChecked = true;
            }
            else
            {
                rbController.IsChecked = true;
            }
        }
        /// <summary>
        /// Vuelve a centrar esta ventana en relacion con su Dueno (MainWindow).
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
            var modulo = cboModulo.Text.Trim(); // Usar ComboBox
            var metodo = txtMetodo.Text.Trim();
            metodo = string.IsNullOrEmpty(metodo) ? modulo : metodo;
            var bd = (cbobd.SelectedItem as ComboBoxItem)?.Content.ToString();
            
            // Reemplazar / por \ para consistencia en Paths
            modulo = modulo.Replace("/", "\\");

            if (string.IsNullOrEmpty(modulo))
            {
                await DialogService.ShowConfirmDialog("Alerta", "Seleccione o Escriba Nombre de Modulo",
                    Dialogs.DialogVariant.Warning, Dialogs.DialogType.Info);
                return;
            }

            // 2. Forzar que el Modulo empiece con mayuscula (Solo la primera letra del path completo o de cada segmento?)
            // Dejamos tal cual por ahora, asumiendo que el usuario elige del combo o escribe bien.
            
            metodo = string.IsNullOrEmpty(metodo) ? Path.GetFileName(modulo) : metodo; // Si es null, usa el nombre de la carpeta final
            // 4. Forzar que el Metodo empiece con mayuscula
            metodo = char.ToUpper(metodo[0]) + metodo.Substring(1);
            txtMetodo.Text = metodo;

            if (string.IsNullOrEmpty(bd))
            {
                await DialogService.ShowConfirmDialog("Alerta", "Seleccione Base de Datos",
                    Dialogs.DialogVariant.Warning, Dialogs.DialogType.Info);
                return;
            }
            // ðŸ“¦ Recolectar metodos seleccionados
            var metodos = new List<string>();
            if (checkPost.IsChecked == true) metodos.Add("Post");
            if (checkGet.IsChecked == true) metodos.Add("Get");
            if (checkGetById.IsChecked == true) metodos.Add("GetById");

            if (metodos.Count == 0)
            {
                await DialogService.ShowConfirmDialog("Alerta", "Seleccione al menos un Metodo",
                    Dialogs.DialogVariant.Warning, Dialogs.DialogType.Info);
                return;
            }
            // ðŸ§© Si el modo IA esta activo
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

                // âš™ï¸ Ejecutar generacion por IA
                await ExecuteMethodGeneration(modulo, metodo, bd, metodos, _aiResult);
                return;
            }
            // âš™ï¸ Ejecutar generacion manual
            await ExecuteMethodGeneration(modulo, metodo, bd, metodos, null);
        }

        #region ðŸ¤– Modo Avanzado con IA

        private async void btnAnalyze_Click(object sender, RoutedEventArgs e)
        {
            var modulo = cboModulo.Text.Trim();
            var nombreMetodo = txtMetodo.Text.Trim();
            nombreMetodo = string.IsNullOrEmpty(nombreMetodo) ? modulo : nombreMetodo;
            var emailContent = txtEmailContent.Text.Trim();
            var bd = (cbobd.SelectedItem as ComboBoxItem)?.Content.ToString();
            if (string.IsNullOrEmpty(modulo) || string.IsNullOrEmpty(nombreMetodo))
            {
                await DialogService.ShowConfirmDialog("Validacion", "Complete el modulo y metodo",
                    Dialogs.DialogVariant.Warning, Dialogs.DialogType.Info);
                return;
            }

            if (string.IsNullOrEmpty(emailContent))
            {
                await DialogService.ShowConfirmDialog("Validacion", "Pegue el contenido del correo tecnico",
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
                await DialogService.ShowConfirmDialog("Alerta", "Seleccione al menos un Metodo",
                    Dialogs.DialogVariant.Warning, Dialogs.DialogType.Info);
                return;
            }

            if (metodos.Count > 1)
            {
                await DialogService.ShowConfirmDialog("Alerta", "Solo puede seleccionar un metodo",
                    Dialogs.DialogVariant.Warning, Dialogs.DialogType.Info);
                return;
            }

            string tipoMetodo = metodos.First();
            // Cambiar a estado de analisis
            panelInput.Visibility = Visibility.Collapsed;
            panelAnalyzing.Visibility = Visibility.Visible;
            panelResult.Visibility = Visibility.Collapsed;

            try
            {
                // Llamar a la IA
                txtAnalyzingStatus.Text = "Enviando informacion a la IA...";
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
        #region ðŸ”§ Generacion de Metodos (Comun)

        private async Task ExecuteMethodGeneration(
            string modulo,
            string nombreMetodo,
            string bd,
            List<string> metodos,
            SPAnalysisResult aiResult)
        {
            string apiProjectName = Path.GetFileName(_projectDirectory);
            string appPath = Path.Combine(_projectDirectory, "Application", modulo);
            string domainPath = Path.Combine(_projectDirectory, "Domain", modulo);
            string infraPath = Path.Combine(_projectDirectory, "Infrastructure", bd, "Repositories", modulo);
            
            bool useEndpoints = rbEndpoint.IsChecked == true;
            string apiPath = useEndpoints 
                ? Path.Combine(_projectDirectory, apiProjectName, "Endpoints", modulo)
                : Path.Combine(_projectDirectory, apiProjectName, "Controllers", modulo);

            try
            {
                foreach (var metodo in metodos)
                {
                    var rollbackEntry = RollbackManager.StartTransaction(modulo, nombreMetodo, metodo);

                    try
                    {
                        bool includeAppLayer = chkIncludeAppLayer.IsChecked == true;
                        
                        if (useEndpoints)
                        {
                            rollbackEntry = AddApiEndpointMethod.Add(apiPath, modulo, metodo, nombreMetodo, rollbackEntry, includeAppLayer);
                        }
                        else
                        {
                            rollbackEntry = AddApiControllerMethod.Add(apiPath, modulo, metodo, nombreMetodo, rollbackEntry);
                        }
                        
                        // Generar Application Layer si NO son Endpoints (Legacy) O si el usuario lo marco explicitamente
                        if (!useEndpoints || includeAppLayer)
                        {
                            // Si es Endpoint (moderno), usamos repositorio generico en el servicio.
                            // Si es Controller (legacy), usamos repositorio especifico.
                            bool useGenericRepo = useEndpoints; 
                            rollbackEntry = AddApplicationMethod.Add(appPath, modulo, metodo, nombreMetodo, rollbackEntry, useGenericRepo);
                        }

                        // ðŸ¤– SI HAY RESULTADO DE IA, USAR GENERACI “N AVANZADA
                         bool useGenericInterface = useEndpoints; // Endpoint = Generic Interface. Controller = Specific Interface.

                        if (aiResult != null)
                        {
                            rollbackEntry = await AddDomainMethod.Add(domainPath, modulo, metodo, nombreMetodo, rollbackEntry, aiResult, useGenericInterface);

                            rollbackEntry = await AddInfrastructureMethod.Add(
                                infraPath, modulo, bd, metodo, nombreMetodo, rollbackEntry, aiResult, useGenericInterface);
                        }
                        else
                        {
                            rollbackEntry = await AddDomainMethod.Add(domainPath, modulo, metodo, nombreMetodo, rollbackEntry, aiResult: null, useGenericInterface);

                            rollbackEntry = await AddInfrastructureMethod.Add(
                                infraPath, modulo, bd, metodo, nombreMetodo, rollbackEntry, aiResult: null, useGenericInterface);
                        }

                        // Dependency Injection
                        string dependencyInjectionPath = Path.Combine(
                            _projectDirectory, apiProjectName, "Config", "DependencyInjection.cs");

                        if (File.Exists(dependencyInjectionPath))
                        {
                            var diContent = File.ReadAllText(dependencyInjectionPath);
                            RollbackManager.RecordFileModification(rollbackEntry, dependencyInjectionPath, diContent);
                            
                            // Solo agregamos DI manual si NO son endpoints (Scrutor maneja lo demas, o si el usuario quiere force)
                            // En realidad Scrutor deberia manejar todo, pero mantenemos compatibilidad legacy para controllers
                            if (!useEndpoints) 
                            { 
                                AddDependencyInjection.Add(dependencyInjectionPath, nombreMetodo, new[] { metodo });
                            }
                        }

                        RollbackManager.CommitTransaction(rollbackEntry);
                    }
                    catch (Exception ex)
                    {
                        Msg.Assistant($"âŒ Error al agregar metodo {metodo}: {ex.Message}");
                        var tempPath = RollbackManager.GetRollbackFilePathForEntry(rollbackEntry);
                        RollbackManager.CommitTransaction(rollbackEntry);
                        // TODO: Fix ExecuteRollback call - needs RollbackEntry, not string
                // 
                        throw;
                    }
                }

                Msg.Assistant($"âœ… Metodo {nombreMetodo} Agregado Correctamente en Modulo {modulo}");
                await DialogService.ShowConfirmDialog(
                    "Confirmacion",
                    $"âœ… Metodo generado exitosamente\n\nModulo: {modulo}\nMetodo: {nombreMetodo}",
                    Dialogs.DialogVariant.Success,
                    Dialogs.DialogType.Info
                );

                this.Close();
            }
            catch (Exception ex)
            {
                await DialogService.ShowConfirmDialog(
                    "Error",
                    $"Error al generar metodo: {ex.Message}\nSe ha realizado rollback.",
                    Dialogs.DialogVariant.Error,
                    Dialogs.DialogType.Info
                );
            }
        }

        #endregion
        #region ðŸ§  IA Integration
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
                Msg.Assistant($"âŒ Error en analisis IA: {ex.Message}");
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








