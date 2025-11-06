using Chapi.Helper.GitHelper;
using Chapi.Helper.UserSettings;
using Chapi.Services;
using Chapi.Views.Dialogs;
using MaterialDesignThemes.Wpf;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media; 
using Velopack;
using Velopack.Sources;

namespace Chapi.Views
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
        public Brush ServiceStatusBrush => IsServiceActive ? Brushes.Green : Brushes.Gray;

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
        public Brush ApiKeyStatusBrush => HasApiKey ? Brushes.Green : Brushes.Gray;
        public string ApiKeyButtonText => HasApiKey ? "Actualizar Key" : "Guardar Key";

        public event PropertyChangedEventHandler PropertyChanged;

       
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public UpdateView(string selectedProjectPath)
        {
            InitializeComponent();
            DataContext = this;
            _mgr = new UpdateManager(new SimpleWebSource(updateUrl));
            _selectedProjectPath = selectedProjectPath;
            LoadCurrentInfo();
            LoadApiKey();
            IsServiceActive = true;

        

            NetworkWatcherService.OnProxyConfigChanged += NetworkWatcher_OnProxyConfigChanged;
            this.Closing += UpdateView_Closing;
            LoadProxySettings();
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

            // Mostrar la vista seleccionada
            if (sender == NavButtonEstado)
                ViewEstadoComponente.Visibility = Visibility.Visible;
            else if (sender == NavButtonIA)
                ViewConfiguracionIA.Visibility = Visibility.Visible;
            else if (sender == NavButtonRed)
                ViewConfiguracionRed.Visibility = Visibility.Visible;
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
            if (_selectedProjectPath is not null) {
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

                txtStatus.Text = "Descarga completa. La app se reiniciará ahora.";
                btnCheckUpdate.Content = "Reiniciando...";

                _mgr.ApplyUpdatesAndRestart(_updateInfo);
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
                        Debug.WriteLine(installError);
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
            Application.Current.Shutdown();
    
        }

        private void btnCerrarServicio_Click(object sender, RoutedEventArgs e)
        {

            IsServiceActive = false;
             Application.Current.Shutdown();
        }




        /// <summary>
        /// Carga la API Key guardada en el PasswordBox
        /// </summary>
        private void LoadApiKey()
        {
            var settings = UserSettingsService.LoadSettings();
            if (!string.IsNullOrEmpty(settings.GeminiApiKey))
            {
                txtApiKey.Password = settings.GeminiApiKey;
                txtApiKey_Visible.Text = settings.GeminiApiKey;
                HasApiKey = true;
            }
            else
            {
                HasApiKey = false;
            }
        }

        /// <summary>
        /// Guarda la API Key en el archivo user.api.settings.json
        /// </summary>
        private async void btnSaveApiKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Llama al método estático
                var settings = UserSettingsService.LoadSettings();
                if (chkShowApiKey.IsChecked == true)
                {
                    settings.GeminiApiKey = txtApiKey_Visible.Text;
                }
                else
                {
                    settings.GeminiApiKey = txtApiKey.Password;
                }
                settings.GeminiApiKey = txtApiKey.Password;
                UserSettingsService.SaveSettings(settings);
                HasApiKey = !string.IsNullOrEmpty(settings.GeminiApiKey); 

                txtStatus.Text = "¡API Key guardada! Reinicia Chapi para usarla.";
                await DialogService.ShowConfirmDialog("Confirmación", "¡API Key guardada! Reinicia Chapi para usarla.", DialogVariant.Info, DialogType.Info);

            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Error al guardar la Key: {ex.Message}";
            }
        }
        /// <summary>
        /// Muestra la clave (Ojo abierto)
        /// </summary>
        private void chkShowApiKey_Checked(object sender, RoutedEventArgs e)
        {
            txtApiKey_Visible.Text = txtApiKey.Password;
            txtApiKey_Visible.Visibility = Visibility.Visible;
            txtApiKey.Visibility = Visibility.Collapsed;
            txtApiKey_Visible.Foreground = Brushes.Green;
        }

        /// <summary>
        /// Oculta la clave (Ojo cerrado)
        /// </summary>
        private void chkShowApiKey_Unchecked(object sender, RoutedEventArgs e)
        {
            txtApiKey.Password = txtApiKey_Visible.Text;
            txtApiKey_Visible.Visibility = Visibility.Collapsed;
            txtApiKey.Visibility = Visibility.Visible;
            
        }


        /// <summary>
        /// Carga el estado ACTUAL del Git Config en la UI.
        /// </summary>
        private async void LoadProxySettings()
        {
            var proxyUrl = await Git.EjecutarGit("config --global http.proxy", "");

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
    }
}