using Chapi.Domain.Interfaces;
using Chapi.Infrastructure.Persistence.Settings;
using Chapi.Infrastructure.Services;
using Chapi.Presentation.Shared.Dialogs.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Velopack;
using Velopack.Sources;

namespace Chapi.Presentation.Features.Settings.Views
{
    /// <summary>
    /// Lógica de interacción para UpdateView.xaml
    /// </summary>
    public partial class UpdateView : Window, INotifyPropertyChanged
    {
        private UpdateManager _mgr;
        private UpdateInfo _updateInfo;
        private string _selectedProjectPath;
        private string updateUrl = App.Configuration["AppConfig:UpdateUrl"] ?? throw new Exception("No se encontro Url Updater");

        // Campos para el conversor de imágenes
        private readonly ImageConverterService _imageConverterService;
        private List<string> _selectedImageFiles = new();
        private string _imageOutputFolder = string.Empty;
        private bool _isLoadingThemeMode;

        private bool _isServiceActive = false;
        public bool IsServiceActive
        {
            get => _isServiceActive;
            set
            {
                _isServiceActive = value;
                OnPropertyChanged(nameof(IsServiceActive));
                OnPropertyChanged(nameof(ServiceStatusText));
                OnPropertyChanged(nameof(ServiceStatusBrush));
            }
        }

        public string ServiceStatusText => IsServiceActive ? "Activo" : "Inactivo";
        public Brush ServiceStatusBrush => IsServiceActive
            ? ResolveThemeBrush("StatusSuccessBrush", Brushes.Green)
            : ResolveThemeBrush("MaterialDesignBodyLight", Brushes.Gray);

        private bool _hasApiKey = false;
        public bool HasApiKey
        {
            get => _hasApiKey;
            set
            {
                _hasApiKey = value;
                OnPropertyChanged(nameof(HasApiKey));
                OnPropertyChanged(nameof(ApiKeyStatusText));
                OnPropertyChanged(nameof(ApiKeyStatusBrush));
                OnPropertyChanged(nameof(ApiKeyButtonText));
            }
        }

        public string ApiKeyStatusText => HasApiKey ? "Key Guardada" : "No se ha configurado una Key";
        public Brush ApiKeyStatusBrush => HasApiKey
            ? ResolveThemeBrush("StatusSuccessBrush", Brushes.Green)
            : ResolveThemeBrush("MaterialDesignBodyLight", Brushes.Gray);
        public string ApiKeyButtonText => HasApiKey ? "Actualizar Key" : "Guardar Key";

        public event PropertyChangedEventHandler PropertyChanged;


        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private static Brush ResolveThemeBrush(string key, Brush fallback)
        {
            if (System.Windows.Application.Current?.Resources[key] is Brush brush)
                return brush;

            return fallback;
        }

        public UpdateView(string selectedProjectPath)
        {
            _isLoadingThemeMode = true;
            InitializeComponent();
            DataContext = this;
            _mgr = new UpdateManager(new GithubSource(updateUrl, null, false));
            _selectedProjectPath = selectedProjectPath;
            LoadCurrentInfo();
            LoadCurrentInfo();
            LoadGitAccountsInfo();
            LoadApiKey();
            LoadSystemSettings();
            IsServiceActive = true;



            NetworkWatcherService.OnProxyConfigChanged += NetworkWatcher_OnProxyConfigChanged;
            this.Closing += UpdateView_Closing;
            LoadProxySettings();

            // Inicializar conversor de imágenes
            _imageConverterService = new ImageConverterService();
        }
        /// <summary>
        /// Se dispara cuando el Watcher (en segundo plano) cambia la config de Git.
        /// </summary>
        private void NetworkWatcher_OnProxyConfigChanged()
        {
            Dispatcher.Invoke(() =>
            {
                LoadProxySettings();
            });
        }
        /// <summary>
        /// Limpia la suscripción al evento cuando la ventana se cierra
        /// (para evitar fugas de memoria).
        /// </summary>
        private void UpdateView_Closing(object sender, CancelEventArgs e)
        {
            NetworkWatcherService.OnProxyConfigChanged -= NetworkWatcher_OnProxyConfigChanged;
        }
        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            // Ocultar todas las vistas
            ViewEstadoComponente.Visibility = Visibility.Collapsed;
            ViewConfiguracionIA.Visibility = Visibility.Collapsed;
            ViewConfiguracionRed.Visibility = Visibility.Collapsed;
            ViewConfiguracionGitHub.Visibility = Visibility.Collapsed;
            ViewOptimizadorWebP.Visibility = Visibility.Collapsed;
            ViewConfiguracionSistema.Visibility = Visibility.Collapsed;

            // Mostrar la vista seleccionada
            if (sender == NavButtonEstado)
                ViewEstadoComponente.Visibility = Visibility.Visible;
            else if (sender == NavButtonIA)
                ViewConfiguracionIA.Visibility = Visibility.Visible;
            else if (sender == NavButtonRed)
                ViewConfiguracionRed.Visibility = Visibility.Visible;
            else if (sender == NavButtonGitHub)
                ViewConfiguracionGitHub.Visibility = Visibility.Visible;
            else if (sender == NavButtonImageConverter)
                ViewOptimizadorWebP.Visibility = Visibility.Visible;
            else if (sender == NavButtonSystemConfig)
            {
                LoadSystemSettings();
                ViewConfiguracionSistema.Visibility = Visibility.Visible;
            }
        }
        /// <summary>
        /// Carga la información de la tarjeta "Información"
        /// </summary>
        private void LoadCurrentInfo()
        {
            string versionString;

            if (_mgr.IsInstalled)
            {
                versionString = _mgr.CurrentVersion.ToString();
            }
            else
            {
                var assembly = Assembly.GetEntryAssembly();
                var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
                versionString = fvi.ProductVersion.Split('+')[0];

            }

            txtCurrentVersion.Text = $"v{versionString}";
            txtMachineId.Text = Environment.MachineName;
            if (_selectedProjectPath is not null)
            {
                if (Path.GetFileNameWithoutExtension(_selectedProjectPath).IndexOf("chapi", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    btnDeveloperPublish.Visibility = Visibility.Visible;
                }
            }


        }

        /// <summary>
        /// Botón "Buscar Actualizaciones"
        /// </summary>
        private async void btnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            // Cambiamos el botón a modo "Descargando..."
            btnCheckUpdate.IsEnabled = false;
            btnCheckUpdate.Content = "Buscando...";
            txtStatus.Text = "Buscando actualizaciones...";

            try
            {
                _updateInfo = await _mgr.CheckForUpdatesAsync();

                if (_updateInfo == null)
                {
                    txtStatus.Text = "¡Chapi ya está actualizado!";
                    btnCheckUpdate.Content = "Buscar Actualizaciones";
                    btnCheckUpdate.IsEnabled = true;
                }
                else
                {

                    txtStatus.Text = $"¡Nueva versión v{_updateInfo.TargetFullRelease.Version} encontrada!";
                    btnCheckUpdate.Content = "Descargar e Instalar Ahora";
                    btnCheckUpdate.IsEnabled = true;
                    // Cambiamos el evento de clic para que ahora instale
                    btnCheckUpdate.Click -= btnCheckUpdate_Click;
                    btnCheckUpdate.Click += btnInstall_Click;
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Error al buscar: {ex.Message}";
                btnCheckUpdate.Content = "Buscar Actualizaciones";
                btnCheckUpdate.IsEnabled = true;
            }
        }

        /// <summary>
        /// Lógica de instalación (se asigna al botón después de encontrar una)
        /// </summary>
        private async void btnInstall_Click(object sender, RoutedEventArgs e)
        {
            if (_updateInfo == null) return;

            try
            {
                btnCheckUpdate.IsEnabled = false;
                btnCheckUpdate.Content = "Descargando...";
                txtStatus.Text = $"Descargando v{_updateInfo.TargetFullRelease.Version}...";

                await _mgr.DownloadUpdatesAsync(_updateInfo);

                txtStatus.Text = "Descarga completa. Preparando instalación...";
                btnCheckUpdate.Content = "Instalando...";

                // Dar tiempo a que se complete la descarga
                await Task.Delay(1000);

                txtStatus.Text = "Reiniciando con la nueva versión...";

                // Asegurar cierre real de MainWindow y limpieza de recursos
                if (System.Windows.Application.Current.MainWindow is MainWindow mw)
                {
                    // 1. Limpieza de recursos internos (Timers, Watchers)
                    mw.ForceShutdown();
                    // 2. Limpieza de "fantasmas" externos (WSL, SQL Server, etc)
                    mw.KillExternalBlockers();
                }

                // Liberar el Mutex ANTES de que Velopack lance la nueva instancia.
                App.ReleaseMutex();

                // ApplyUpdatesAndRestart: aplica los archivos y relanza automáticamente la nueva versión.
                _mgr.ApplyUpdatesAndRestart(_updateInfo);

                // Si llegamos aquí, forzar la salida inmediata
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Error al instalar: {ex.Message}";
                btnCheckUpdate.Content = "Reintentar Instalación";
                btnCheckUpdate.IsEnabled = true;
            }
        }

        /// <summary>
        /// (MODO DEV) Inicia el proceso de 'dotnet publish' y 'vpk pack'
        /// </summary>
        private async void btnDeveloperPublish_Click(object sender, RoutedEventArgs e)
        {
            btnDeveloperPublish.IsEnabled = false;

            try
            {

                var projectPath = Path.Combine(_selectedProjectPath, "Chapi", "Chapi.csproj");
                var publishOutput = Path.Combine(_selectedProjectPath, "publish-output");
                var publicDir = Path.Combine(_selectedProjectPath, "public");

                var version = txtCurrentVersion.Text.TrimStart('v');
                var parts = version.Split('.');
                int major = int.Parse(parts[0]);
                int minor = int.Parse(parts[1]);
                int patch = int.Parse(parts[2]);

                patch++;

                if (patch >= 10)
                {
                    patch = 0;
                    minor++;
                }

                if (minor >= 10)
                {
                    minor = 0;
                    major++;
                }


                version = $"{major}.{minor}.{patch}";
                // 3. Ejecutar 'dotnet publish'
                txtStatus.Text = "Iniciando 'dotnet publish'...";
                string publishArgs = $"publish \"{projectPath}\" -c Release --self-contained -r win-x64 -o \"{publishOutput}\"";
                var (code, output, error) = await RunProcessAsync("dotnet", publishArgs, _selectedProjectPath);

                if (code != 0)
                {
                    txtStatus.Text = "¡Error durante 'dotnet publish'! Revisa la salida.";
                    return;
                }
                // ==============================================
                //      ¡NUEVA LÓGICA DE VERIFICACIÓN!
                // ==============================================

                // 4. Verificar si 'vpk' está instalado
                txtStatus.Text = "Verificando herramienta 'vpk'...";
                var (listCode, listOutput, listError) = await RunProcessAsync("dotnet", "tool list -g", _selectedProjectPath);

                if (listCode != 0 || !listOutput.Contains("vpk"))
                {
                    // No está instalado, procedemos a instalarlo
                    txtStatus.Text = "'vpk' no encontrado. Instalando automáticamente...";
                    var (installCode, installOutput, installError) = await RunProcessAsync("dotnet", "tool install -g vpk", _selectedProjectPath);

                    if (installCode != 0)
                    {
                        txtStatus.Text = "¡Error fatal! No se pudo instalar 'vpk'.";
                        return;
                    }
                    txtStatus.Text = "'vpk' instalado. Continuando...";
                }
                else
                {
                    txtStatus.Text = "'vpk' ya está instalado.";
                }

                // ==============================================
                //      FIN DE LA NUEVA LÓGICA
                // ==============================================
                // 4. Ejecutar 'vpk pack'
                txtStatus.Text = "Publicación completa. Iniciando 'vpk pack'...";
                if (version == null || publishOutput == null || publicDir == null)
                    throw new Exception("Uno de los parámetros es nulo.");
                string vpkArgs = @$"pack --packId ChapiAssistant --packVersion {version} --packDir ""{publishOutput}"" --mainExe Chapi.exe -o ""{publicDir}""";


                var (codevp, outputvp, errorvp) = await RunProcessAsync("vpk", vpkArgs, _selectedProjectPath);

                if (codevp != 0)
                {
                    txtStatus.Text = outputvp;
                    return;
                }

                txtStatus.Text = $"¡ÉXITO! Paquete v{version} generado en la carpeta '/public'.";
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Error fatal: {ex.Message}";
            }
            finally
            {
                btnDeveloperPublish.IsEnabled = true;
            }
        }

        /// <summary>
        /// (MODO DEV) Helper para ejecutar comandos y mostrar la salida en txtStatus
        /// </summary>
        private async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(string fileName, string arguments, string workingDirectory)
        {
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                    Dispatcher.Invoke(() => txtStatus.Text = e.Data.Trim(), System.Windows.Threading.DispatcherPriority.Background);
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                    Dispatcher.Invoke(() => txtStatus.Text = $"ERROR: {e.Data.Trim()}", System.Windows.Threading.DispatcherPriority.Background);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            return (process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
        }

        private void btnReiniciar_Click(object sender, RoutedEventArgs e)
        {
            IsServiceActive = true;
            string exePath = Environment.ProcessPath;
            Process.Start(exePath);
            System.Windows.Application.Current.Shutdown();

        }

        private void btnCerrarServicio_Click(object sender, RoutedEventArgs e)
        {

            IsServiceActive = false;
            System.Windows.Application.Current.Shutdown();
        }




        /// <summary>
        /// Carga las API Keys guardadas
        /// </summary>
        private void LoadApiKey()
        {
            var settings = UserSettingsService.LoadSettings();
            
            // Cargar Gemini
            if (!string.IsNullOrEmpty(settings.GeminiApiKey))
            {
                txtGeminiKey.Password = settings.GeminiApiKey;
                txtGeminiKey_Visible.Text = settings.GeminiApiKey;
            }

            // Cargar OpenAI
            if (!string.IsNullOrEmpty(settings.OpenAiApiKey))
            {
                txtOpenAiKey.Password = settings.OpenAiApiKey;
                txtOpenAiKey_Visible.Text = settings.OpenAiApiKey;
            }

            // Cargar Claude
            if (!string.IsNullOrEmpty(settings.ClaudeApiKey))
            {
                txtClaudeKey.Password = settings.ClaudeApiKey;
                txtClaudeKey_Visible.Text = settings.ClaudeApiKey;
            }

            // Seleccionar proveedor preferido
            switch (settings.PreferredAiProvider)
            {
                case "Gemini":
                case "gemini":
                    cmbAiProvider.SelectedIndex = 0;
                    break;
                case "OpenAI":
                case "openai":
                    cmbAiProvider.SelectedIndex = 1;
                    break;
                case "Claude":
                case "claude":
                    cmbAiProvider.SelectedIndex = 2;
                    break;
                default: cmbAiProvider.SelectedIndex = 0; break;
            }

            UpdateApiKeyStatus();
        }

        private void LoadSystemSettings()
        {
            _isLoadingThemeMode = true;
            try
            {
                var settings = UserSettingsService.LoadSettings();
                var themeMode = ThemeService.NormalizeThemeMode(settings.ThemeMode);

                rbThemeLight.IsChecked = false;
                rbThemeDark.IsChecked = false;
                rbThemeSystem.IsChecked = false;

                if (themeMode == ThemeService.LightMode)
                    rbThemeLight.IsChecked = true;
                else if (themeMode == ThemeService.SystemMode)
                    rbThemeSystem.IsChecked = true;
                else
                    rbThemeDark.IsChecked = true;

                if (rbThemeLight.IsChecked != true && rbThemeDark.IsChecked != true && rbThemeSystem.IsChecked != true)
                    rbThemeSystem.IsChecked = true;
            }
            finally
            {
                _isLoadingThemeMode = false;
            }
        }

        private void UpdateApiKeyStatus()
        {
            var settings = UserSettingsService.LoadSettings();
            string preferred = settings.PreferredAiProvider;
            bool hasKey = false;

            if (preferred == "Gemini" && !string.IsNullOrEmpty(settings.GeminiApiKey)) hasKey = true;
            else if (preferred == "OpenAI" && !string.IsNullOrEmpty(settings.OpenAiApiKey)) hasKey = true;
            else if (preferred == "Claude" && !string.IsNullOrEmpty(settings.ClaudeApiKey)) hasKey = true;

            HasApiKey = hasKey;
        }

        /// <summary>
        /// Guarda la configuración de IA
        /// </summary>
        private async void btnSaveApiKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = UserSettingsService.LoadSettings();

                // Guardar Gemini
                settings.GeminiApiKey = chkShowGemini.IsChecked == true ? txtGeminiKey_Visible.Text : txtGeminiKey.Password;
                
                // Guardar OpenAI
                settings.OpenAiApiKey = chkShowOpenAi.IsChecked == true ? txtOpenAiKey_Visible.Text : txtOpenAiKey.Password;

                // Guardar Claude
                settings.ClaudeApiKey = chkShowClaude.IsChecked == true ? txtClaudeKey_Visible.Text : txtClaudeKey.Password;

                // Guardar Preferido
                if (cmbAiProvider.SelectedItem is ComboBoxItem item)
                {
                    settings.PreferredAiProvider = NormalizeAiProvider(item.Content?.ToString());
                }

                UserSettingsService.SaveSettings(settings);
                UpdateApiKeyStatus();

                txtStatus.Text = "¡Configuración de IA guardada y aplicada!";
                await DialogService.ShowConfirmDialog("Confirmación", "¡Configuración guardada! El proveedor seleccionado se aplicará en las siguientes solicitudes.", DialogVariant.Info, DialogType.Info);

            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Error al guardar configuración IA: {ex.Message}";
            }
        }

        private void ThemeModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoadingThemeMode)
                return;

            if (sender is not RadioButton radioButton)
                return;

            var selectedMode = ThemeService.NormalizeThemeMode(radioButton.Tag?.ToString());
            var settings = UserSettingsService.LoadSettings();

            if (string.Equals(settings.ThemeMode, selectedMode, StringComparison.OrdinalIgnoreCase))
                return;

            settings.ThemeMode = selectedMode;
            UserSettingsService.SaveSettings(settings);

            ThemeService.ApplyTheme(selectedMode);
            OnPropertyChanged(nameof(ServiceStatusBrush));
            OnPropertyChanged(nameof(ApiKeyStatusBrush));
        }

        #region Toggle Visibility Handlers

        private void chkShowGemini_Checked(object sender, RoutedEventArgs e) => TogglePasswordVisibility(txtGeminiKey, txtGeminiKey_Visible, true);
        private void chkShowGemini_Unchecked(object sender, RoutedEventArgs e) => TogglePasswordVisibility(txtGeminiKey, txtGeminiKey_Visible, false);

        private void chkShowOpenAi_Checked(object sender, RoutedEventArgs e) => TogglePasswordVisibility(txtOpenAiKey, txtOpenAiKey_Visible, true);
        private void chkShowOpenAi_Unchecked(object sender, RoutedEventArgs e) => TogglePasswordVisibility(txtOpenAiKey, txtOpenAiKey_Visible, false);

        private void chkShowClaude_Checked(object sender, RoutedEventArgs e) => TogglePasswordVisibility(txtClaudeKey, txtClaudeKey_Visible, true);
        private void chkShowClaude_Unchecked(object sender, RoutedEventArgs e) => TogglePasswordVisibility(txtClaudeKey, txtClaudeKey_Visible, false);

        private void TogglePasswordVisibility(PasswordBox passwordBox, TextBox textBox, bool show)
        {
            if (show)
            {
                textBox.Text = passwordBox.Password;
                textBox.Visibility = Visibility.Visible;
                passwordBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                passwordBox.Password = textBox.Text;
                textBox.Visibility = Visibility.Collapsed;
                passwordBox.Visibility = Visibility.Visible;
            }
        }

        #endregion

        private static string NormalizeAiProvider(string? raw)
        {
            var value = (raw ?? string.Empty).Trim();
            if (value.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) || value.Contains("OpenAI", StringComparison.OrdinalIgnoreCase))
                return "OpenAI";
            if (value.Equals("Claude", StringComparison.OrdinalIgnoreCase) || value.Contains("Claude", StringComparison.OrdinalIgnoreCase))
                return "Claude";
            return "Gemini";
        }


        /// <summary>
        /// Carga el estado ACTUAL del Git Config en la UI.
        /// </summary>
        private async void LoadProxySettings()
        {
            var repo = App.ServiceProvider.GetRequiredService<IGitRepository>();
            var proxyUrl = await repo.GetConfigAsync(_selectedProjectPath, "http.proxy", isGlobal: true);

            if (!string.IsNullOrWhiteSpace(proxyUrl))
            {
                // SI HAY UN PROXY ACTIVO (en Git Config)
                chkUseProxy.IsChecked = true;
                try
                {
                    var uri = new Uri(proxyUrl.Trim());
                    txtProxyUrl.Text = uri.Host + ":" + uri.Port;
                    if (!string.IsNullOrEmpty(uri.UserInfo))
                    {
                        var userInfo = uri.UserInfo.Split(':');
                        if (userInfo.Length > 0) txtProxyUser.Text = userInfo[0];
                        if (userInfo.Length > 1) txtProxyPass.Password = userInfo[1];
                    }
                }
                catch (Exception ex) { txtStatus.Text = $"Error al leer proxy: {ex.Message}"; }
            }
            else
            {
                // NO HAY PROXY ACTIVO (en Git Config)
                chkUseProxy.IsChecked = false;

                // Rellenamos los campos con lo último guardado (pero deshabilitado)
                var settings = UserSettingsService.LoadSettings();
                txtProxyUrl.Text = settings.ProxyUrl;
                txtProxyUser.Text = settings.ProxyUser;
                txtProxyPass.Password = settings.ProxyPass;
            }

            // Habilita/deshabilita los campos
            chkUseProxy_Toggled(null, null);
        }
        private void chkUseProxy_Toggled(object sender, RoutedEventArgs e)
        {
            bool enabled = chkUseProxy.IsChecked == true;
            if (txtProxyUrl != null) txtProxyUrl.IsEnabled = enabled;
            if (txtProxyUser != null) txtProxyUser.IsEnabled = enabled;
            if (txtProxyPass != null) txtProxyPass.IsEnabled = enabled;
        }

        private async void btnSaveProxy_Click(object sender, RoutedEventArgs e)
        {

            try
            {
                var settings = UserSettingsService.LoadSettings();

                if (chkUseProxy.IsChecked == true)
                {
                    var url = txtProxyUrl.Text.Trim();
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        await DialogService.ShowConfirmDialog("Confirmación", "La dirección del proxy no puede estar vacía.", DialogVariant.Warning, DialogType.Info);
                        return;
                    }

                    // 1. Guarda la configuración en el archivo JSON
                    settings.ProxyEnabled = true;
                    settings.ProxyUrl = url;
                    settings.ProxyUser = txtProxyUser.Text.Trim();
                    settings.ProxyPass = txtProxyPass.Password.Trim();
                }
                else
                {
                    // 2. Guarda la configuración (deshabilitada)
                    settings.ProxyEnabled = false;
                }

                UserSettingsService.SaveSettings(settings);

                // 3. Fuerza al vigilante a comprobar la red AHORA
                await App.NetworkWatcher.CheckNetworkAndApplyProxy();

                await DialogService.ShowConfirmDialog("Confirmación", "¡Configuración de red guardada!", DialogVariant.Info, DialogType.Info);
            }
            catch (Exception ex)
            {
                await DialogService.ShowConfirmDialog("Error", $"Error al guardar proxy: {ex.Message}", DialogVariant.Error, DialogType.Info);
            }

        }

        #region Conversor de Imágenes WebP

        public class ImageResultItem
        {
            public string FileName { get; set; } = string.Empty;
            public string SizeInfo { get; set; } = string.Empty;
            public string Reduction { get; set; } = string.Empty;
        }

        private void btnSelectImages_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Seleccionar Imágenes",
                Filter = "Imágenes|*.png;*.jpg;*.jpeg|Todos los archivos|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                // Filtrar solo archivos con extensiones válidas
                _selectedImageFiles = dialog.FileNames
                    .Where(f => ImageConverterService.IsSupportedImage(f))
                    .ToList();

                if (_selectedImageFiles.Count == 0)
                {
                    MessageBox.Show("No se seleccionaron imágenes válidas (PNG, JPG, JPEG).",
                        "Sin imágenes válidas", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Establecer la carpeta de destino en la misma ubicación del primer archivo seleccionado
                var firstFile = _selectedImageFiles.First();
                var sourceDirectory = Path.GetDirectoryName(firstFile);
                if (!string.IsNullOrEmpty(sourceDirectory))
                {
                    _imageOutputFolder = sourceDirectory;
                    txtImageOutputFolder.Text = _imageOutputFolder;
                }

                UpdateImageFileList();
            }
        }

        private void UpdateImageFileList()
        {
            if (_selectedImageFiles.Count > 0)
            {
                borderImageFileList.Visibility = Visibility.Visible;
                txtImageFileCount.Text = $"📁 {_selectedImageFiles.Count} imagen{(_selectedImageFiles.Count > 1 ? "es" : "")} seleccionada{(_selectedImageFiles.Count > 1 ? "s" : "")}";

                var fileNames = _selectedImageFiles.Select(f => Path.GetFileName(f)).ToList();
                listImageSelectedFiles.ItemsSource = fileNames;

                btnConvertImages.IsEnabled = true;
            }
            else
            {
                borderImageFileList.Visibility = Visibility.Collapsed;
                btnConvertImages.IsEnabled = false;
            }
        }

        private void sliderImageQuality_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (txtImageQualityValue != null)
            {
                txtImageQualityValue.Text = $"{(int)sliderImageQuality.Value}%";
            }
        }

        private void btnSelectImageOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Seleccionar carpeta de destino",
                ShowNewFolderButton = true,
                SelectedPath = _imageOutputFolder
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _imageOutputFolder = dialog.SelectedPath;
                txtImageOutputFolder.Text = _imageOutputFolder;
            }
        }

        private async void btnConvertImages_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedImageFiles.Count == 0)
            {
                MessageBox.Show("Por favor selecciona al menos una imagen.",
                    "Sin imágenes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_imageOutputFolder))
            {
                MessageBox.Show("Por favor selecciona una carpeta de destino.",
                    "Sin carpeta de destino", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Deshabilitar controles durante la conversión
            btnConvertImages.IsEnabled = false;
            btnSelectImages.IsEnabled = false;
            btnSelectImageOutput.IsEnabled = false;
            sliderImageQuality.IsEnabled = false;

            // Mostrar progreso
            panelImageProgress.Visibility = Visibility.Visible;
            borderImageResults.Visibility = Visibility.Collapsed;
            progressImageBar.Value = 0;
            txtImageProgress.Text = "Iniciando conversión...";

            try
            {
                var quality = (int)sliderImageQuality.Value;

                var progressReporter = new Progress<string>(message =>
                {
                    txtImageProgress.Text = message;
                });

                var percentReporter = new Progress<int>(percent =>
                {
                    progressImageBar.Value = percent;
                });

                var results = await _imageConverterService.ConvertMultipleImagesAsync(
                    _selectedImageFiles,
                    _imageOutputFolder,
                    quality,
                    progressReporter,
                    percentReporter);

                // Mostrar resultados
                ShowImageResults(results);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error durante la conversión: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Rehabilitar controles
                btnConvertImages.IsEnabled = true;
                btnSelectImages.IsEnabled = true;
                btnSelectImageOutput.IsEnabled = true;
                sliderImageQuality.IsEnabled = true;
                panelImageProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowImageResults(List<ImageConverterService.ConversionResult> results)
        {
            borderImageResults.Visibility = Visibility.Visible;

            var successful = results.Count(r => r.Success);
            var failed = results.Count - successful;
            var totalOriginal = results.Where(r => r.Success).Sum(r => r.OriginalSize);
            var totalConverted = results.Where(r => r.Success).Sum(r => r.ConvertedSize);
            var totalSaved = totalOriginal - totalConverted;
            var avgReduction = totalOriginal > 0 ? (double)totalSaved / totalOriginal * 100 : 0;

            txtImageResultSummary.Text = $"✅ {successful} imagen{(successful != 1 ? "es" : "")} convertida{(successful != 1 ? "s" : "")} exitosamente" +
                (failed > 0 ? $" | ❌ {failed} fallida{(failed != 1 ? "s" : "")}" : "") +
                $"\n💾 Espacio ahorrado: {ImageConverterService.FormatFileSize(totalSaved)} ({avgReduction:F1}% de reducción promedio)";

            var resultItems = results.Where(r => r.Success).Select(r => new ImageResultItem
            {
                FileName = Path.GetFileName(r.SourcePath),
                SizeInfo = $"{ImageConverterService.FormatFileSize(r.OriginalSize)} → {ImageConverterService.FormatFileSize(r.ConvertedSize)}",
                Reduction = $"-{r.CompressionRatio}%"
            }).ToList();

            listImageResults.ItemsSource = resultItems;
        }

        private void btnOpenImageOutput_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(_imageOutputFolder))
            {
                Process.Start("explorer.exe", _imageOutputFolder);
            }
        }

        #endregion

        #region Git Auth Methods

        private async void LoadGitAccountsInfo()
        {
            try
            {
                var storage = App.ServiceProvider.GetRequiredService<ICredentialStorageService>();

                // GitHub
                var githubCred = await storage.GetCredentialAsync(Chapi.Domain.Enums.GitProvider.GitHub.ToString());
                if (githubCred.HasValue)
                {
                    txtGitHubUser.Text = githubCred.Value.username;
                    btnGitHubLogin.Content = "Desconectar"; // O Cambiar
                }
                else
                {
                    txtGitHubUser.Text = "No conectado";
                    btnGitHubLogin.Content = "Conectar";
                }

                // GitLab
                var gitlabCred = await storage.GetCredentialAsync(Chapi.Domain.Enums.GitProvider.GitLab.ToString());
                if (gitlabCred.HasValue)
                {
                    txtGitLabUser.Text = gitlabCred.Value.username;
                    btnGitLabLogin.Content = "Desconectar";
                }
                else
                {
                    txtGitLabUser.Text = "No conectado";
                    btnGitLabLogin.Content = "Conectar";
                }
            }
            catch (Exception ex)
            {

            }
        }

        private async void btnGitHubLogin_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.Content.ToString() == "Desconectar")
            {
                var confirm = await DialogService.ShowConfirmDialog("Desconectar", "¿Deseas cerrar sesión de GitHub?", DialogVariant.Warning, DialogType.Confirm);
                if (confirm)
                {
                    var storage = App.ServiceProvider.GetRequiredService<ICredentialStorageService>();
                    await storage.DeleteCredentialAsync(Chapi.Domain.Enums.GitProvider.GitHub.ToString());
                    LoadGitAccountsInfo();
                }
                return;
            }

            try
            {
                var factory = App.ServiceProvider.GetRequiredService<IGitAuthProviderFactory>();
                var provider = factory.GetProvider(Chapi.Domain.Enums.GitProvider.GitHub);

                var result = await provider.AuthenticateAsync();
                if (result.IsSuccess)
                {
                    LoadGitAccountsInfo();
                }
                else if (result.Error != "Autenticación cancelada")
                {
                    await DialogService.ShowConfirmDialog("Error", result.Error, DialogVariant.Error, DialogType.Info);
                }
            }
            catch (Exception ex)
            {
                await DialogService.ShowConfirmDialog("Error", ex.Message, DialogVariant.Error, DialogType.Info);
            }
        }

        private async void btnGitLabLogin_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.Content.ToString() == "Desconectar")
            {
                var confirm = await DialogService.ShowConfirmDialog("Desconectar", "¿Deseas cerrar sesión de GitLab?", DialogVariant.Warning, DialogType.Confirm);
                if (confirm)
                {
                    var storage = App.ServiceProvider.GetRequiredService<ICredentialStorageService>();
                    await storage.DeleteCredentialAsync(Chapi.Domain.Enums.GitProvider.GitLab.ToString());
                    LoadGitAccountsInfo();
                }
                return;
            }

            try
            {
                var factory = App.ServiceProvider.GetRequiredService<IGitAuthProviderFactory>();
                var provider = factory.GetProvider(Chapi.Domain.Enums.GitProvider.GitLab);

                var result = await provider.AuthenticateAsync();
                if (result.IsSuccess)
                {
                    LoadGitAccountsInfo();
                }
                else if (result.Error != "Autenticación cancelada")
                {
                    await DialogService.ShowConfirmDialog("Error", result.Error, DialogVariant.Error, DialogType.Info);
                }
            }
            catch (Exception ex)
            {
                await DialogService.ShowConfirmDialog("Error", ex.Message, DialogVariant.Error, DialogType.Info);
            }
        }

        #endregion
    }
}

