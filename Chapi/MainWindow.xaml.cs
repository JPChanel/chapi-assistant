using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;
using Chapi.Domain.Models;
using Chapi.Infrastructure.Persistence.Settings;
using Chapi.Infrastructure.Services;
using Chapi.Presentation.Shell.Models;
using Chapi.Presentation.Shell.Services;
using Chapi.Presentation.Shared.Tasks;
using Chapi.Presentation.Views.Dialogs;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using Velopack;
using Velopack.Sources;
using UseCases = Chapi.Application.UseCases.Git;

namespace Chapi
{
    public partial class MainWindow : Window, System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        private bool _isWindowInitialized = false;
        private string projectDirectory;
        private string _currentlySelectedBranch;
        private string repoUrl = App.Configuration["AppConfig:UrlGit"] ?? throw new Exception("No se encontro Url Git");
        private string updateUrl = App.Configuration["AppConfig:UpdateUrl"] ?? throw new Exception("No se encontro Url Updater");
        public static MainWindow Instance { get; private set; }

        private readonly object _lock = new object();
        private bool _isGitInstalled = false;
        private System.Windows.Threading.DispatcherTimer _fetchTimer;
        private CancellationTokenSource? _projectSwitchCts;
        private bool _isShuttingDown = false;
        private bool _isSwitchingBranch = false;
        private readonly SemaphoreSlim _fetchRefreshSemaphore = new(1, 1);

        public string AppVersion { get; private set; }
        public string ServiceStatusText => "Activo";
        public Brush ServiceStatusBrush => Brushes.Lime;

        private bool _needsPublish;
        public bool NeedsPublish
        {
            get => _needsPublish;
            set { _needsPublish = value; OnPropertyChanged(nameof(NeedsPublish)); }
        }

        private Presentation.ViewModels.ChangesViewModel? _changesViewModel;
        private Presentation.ViewModels.HistoryViewModel? _historyViewModel;
        private Presentation.ViewModels.ReleasesViewModel? _releasesViewModel;
        private Presentation.ViewModels.WorkspaceViewModel? _workspaceViewModel;
        private Presentation.ViewModels.AssistantViewModel? _assistantViewModel;
        private Presentation.ViewModels.DocumentationViewModel? _documentationViewModel;
        private readonly IGitRepository _gitRepository;
        private readonly ProjectShellService _projectShellService;

        public MainWindow()
        {
            InitializeComponent();
            Instance = this;
            DataContext = MessageHelper.Instance;

            _gitRepository = App.ServiceProvider.GetRequiredService<IGitRepository>();
            _projectShellService = App.ServiceProvider.GetRequiredService<ProjectShellService>();
            _changesViewModel = App.ServiceProvider.GetService(typeof(Presentation.ViewModels.ChangesViewModel)) as Presentation.ViewModels.ChangesViewModel;
            _historyViewModel = App.ServiceProvider.GetService(typeof(Presentation.ViewModels.HistoryViewModel)) as Presentation.ViewModels.HistoryViewModel;
            _releasesViewModel = App.ServiceProvider.GetService(typeof(Presentation.ViewModels.ReleasesViewModel)) as Presentation.ViewModels.ReleasesViewModel;
            _assistantViewModel = App.ServiceProvider.GetService(typeof(Presentation.ViewModels.AssistantViewModel)) as Presentation.ViewModels.AssistantViewModel;
            _workspaceViewModel = App.ServiceProvider.GetService(typeof(Presentation.ViewModels.WorkspaceViewModel)) as Presentation.ViewModels.WorkspaceViewModel;
            _documentationViewModel = App.ServiceProvider.GetService(typeof(Presentation.ViewModels.DocumentationViewModel)) as Presentation.ViewModels.DocumentationViewModel;

            ChangesTab.DataContext = _changesViewModel;
            HistoryTab.DataContext = _historyViewModel;
            TagsTab.DataContext = _releasesViewModel;
            WorkspaceTab.DataContext = _workspaceViewModel;
            AssistantViewControl.DataContext = _assistantViewModel;
            DocumentationViewControl.DataContext = _documentationViewModel;

            Msg.Assistant("Hey! Soy Chapi. Tu dev buddy para arquitectura.", showAlert: false);

            CheckForUpdates().Forget("buscando actualizaciones");
            LoadVersion();

            _fetchTimer = new System.Windows.Threading.DispatcherTimer();
            _fetchTimer.Interval = TimeSpan.FromMinutes(10);
            _fetchTimer.Tick += async (s, ev) => await DoFetchAndRefreshAsync(isSilent: true);
            _fetchTimer.Start();
        }

        private void LoadVersion()
        {
            var assembly = System.Reflection.Assembly.GetEntryAssembly();
            if (assembly != null)
            {
                var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
                AppVersion = $"v{fvi.ProductVersion?.Split('+')[0]}";
                OnPropertyChanged(nameof(AppVersion));
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await Task.Delay(300);

            _isWindowInitialized = true;
            LoadProjects();

            // Pre-cargar repositorios remotos para el dialogo de clonado
            _ = App.ServiceProvider.GetService<Chapi.Presentation.ViewModels.CloneRepositoryViewModel>();

            // Pre-cargar avatares de usuario
            Task.Run(async () =>
            {
                var storage = App.ServiceProvider.GetService<Chapi.Domain.Interfaces.ICredentialStorageService>();
                if (storage != null)
                {
                    await Chapi.Domain.Services.AvatarCacheService.Instance.PreloadAvatarsAsync(storage);
                }
            }).Forget("precargando avatares");
            Task.Run(CheckGitInstallationAsync).Forget("validando git");

            if (_changesViewModel != null)
            {
                _changesViewModel.CommitCompleted += async (s, e) =>
                {
                    await LoadHistoryAsync();
                    await UpdateProjectStatusesAsync();
                };
            }
            if (_historyViewModel != null)
            {
                _historyViewModel.ResetCompleted += async (s, e) =>
                {
                    // Forzar recarga de cambios en el VM de Cambios
                    if (_changesViewModel != null)
                    {
                        await _changesViewModel.ForceRefreshAsync();
                    }
                    else
                    {
                        await LoadChangesAsync();
                    }

                    // Actualizar indicadores (flecha de push/pull)
                    await UpdateProjectStatusesAsync();
                };
            }

            if (_releasesViewModel != null)
            {
                _releasesViewModel.TagDeleted += async (s, e) =>
                {
                    await LoadHistoryAsync();
                };
            }
        }

        private async Task CheckForUpdates()
        {
            try
            {
                var mgr = new UpdateManager(new GithubSource(updateUrl, null, false));
                var info = await mgr.CheckForUpdatesAsync();
                if (info == null) return;
                Dispatcher.Invoke(() => Msg.Assistant($"Nueva version v{info.TargetFullRelease.Version} disponible."));
            }
            catch { }
        }


        public void ShowUpdateView()
        {
            var updateView = new Chapi.Presentation.Views.Settings.UpdateView(projectDirectory);
            updateView.Owner = this;
            updateView.ShowDialog();
        }

        private void LogoButton_Click(object sender, RoutedEventArgs e) => ShowUpdateView();

        private void LoadProjects()
        {
            var projectVMs = _projectShellService.LoadProjects().ToList();

            ProjectsComboBox.ItemsSource = projectVMs;
            App.TrayIconManager?.UpdateProjectList(projectVMs);

            // Ejecutar la actualizacion de estados con retardo para no competir por CPU/Disco al inicio
            Task.Run(async () =>
            {
                await Task.Delay(1500);
                await UpdateProjectStatusesAsync(projectVMs);
            }).Forget("actualizando estados de proyectos");
        }

        // El monitoreo del sistema de archivos ahora lo gestiona ChangesViewModel._changeWatcher

        private async void ProjectsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProjectsComboBox.SelectedItem is not ProjectViewModel selectedProject) return;

            _projectSwitchCts?.Cancel();
            _projectSwitchCts = new CancellationTokenSource();
            var token = _projectSwitchCts.Token;

            projectDirectory = selectedProject.FullPath;
            if (_changesViewModel != null)
            {
                _changesViewModel.ProjectPath = projectDirectory;
            }

            if (_historyViewModel != null)
            {
                _historyViewModel.ProjectPath = projectDirectory;
            }

            if (_releasesViewModel != null)
            {
                _releasesViewModel.ProjectPath = projectDirectory;
            }

            if (!_isGitInstalled) return;

            App.TrayIconManager?.UpdateProjectMenuItem(selectedProject.Name, false);

            try
            {
                var snapshot = await _projectShellService.LoadProjectContextAsync(
                    new ProjectSelectionRequest
                    {
                        ProjectPath = projectDirectory,
                        ProjectName = selectedProject.Name,
                        ChangesViewModel = _changesViewModel,
                        HistoryViewModel = _historyViewModel,
                        ReleasesViewModel = _releasesViewModel,
                        WorkspaceViewModel = _workspaceViewModel,
                        AssistantViewModel = _assistantViewModel,
                        DocumentationViewModel = _documentationViewModel
                    },
                    token);

                if (token.IsCancellationRequested) return;

                _currentlySelectedBranch = snapshot.CurrentBranch;
                BranchesComboBox.ItemsSource = snapshot.Branches;
                BranchesComboBox.SelectedItem = snapshot.CurrentBranch;
                NeedsPublish = snapshot.NeedsPublish;

                UpdateGitActionButton();

                if (snapshot.Ahead > 0)
                {
                    Msg.Assistant($"Tienes {snapshot.Ahead} commits pendientes de subir en '{selectedProject.Name}'. No olvides hacer Push!");
                }

                DoFetchAndRefreshAsync(isSilent: true).Forget("sincronizando cambios del proyecto");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Msg.Assistant("Error cambiando de proyecto: " + ex.Message);
            }
        }

        private async void BranchesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSwitchingBranch) return;
            if (BranchesComboBox.SelectedItem is not string newBranch || newBranch == _currentlySelectedBranch) return;
            if (string.IsNullOrWhiteSpace(projectDirectory)) return;

            bool switchedSuccessfully = false;
            _isSwitchingBranch = true;
            try
            {
                await RunWithLoading(async () =>
                {
                    // Evitar "status" completo en cada cambio usando primero el estado ya cargado.
                    bool hasChanges = await HasPendingChangesBeforeBranchSwitchAsync();
                    bool stashChanges = false;

                    if (hasChanges)
                    {
                        var dialog = new Chapi.Presentation.Views.Dialogs.SwitchBranchDialog
                        {
                            TargetBranch = newBranch
                        };
                        var result = await DialogService.ShowDialog(dialog);

                        if (result == null || result.ToString() == "cancel")
                        {
                            BranchesComboBox.SelectedItem = _currentlySelectedBranch;
                            return;
                        }

                        stashChanges = result.ToString() == "stash";
                    }

                    try
                    {
                        using var watcherSilencer = _changesViewModel?.SuspendWatcher();

                        var useCase = App.ServiceProvider.GetService(typeof(UseCases.SwitchBranchUseCase)) as UseCases.SwitchBranchUseCase;
                        var switchResult = await useCase.ExecuteAsync(projectDirectory, newBranch, stashChanges);

                        if (switchResult.IsSuccess)
                        {
                            _currentlySelectedBranch = newBranch;
                            BranchesComboBox.SelectedItem = newBranch;
                            switchedSuccessfully = true;
                        }
                        else
                        {
                            BranchesComboBox.SelectedItem = _currentlySelectedBranch;
                            await DialogService.ShowConfirmDialog("No se pudo cambiar de rama", switchResult.Error, DialogVariant.Error, DialogType.Info);
                        }
                    }
                    catch (Exception ex)
                    {
                        BranchesComboBox.SelectedItem = _currentlySelectedBranch;
                        await DialogService.ShowConfirmDialog("Error al cambiar de rama", $"Excepcion inesperada:\n{ex.Message}", DialogVariant.Error, DialogType.Info);
                    }
                });

                if (!switchedSuccessfully)
                    return;

                // Actualizaciones fuera del camino critico del checkout.
                RefreshBranchesAsync().Forget("refrescando ramas");
                if (_changesViewModel != null)
                {
                    await _changesViewModel.ForceRefreshAsync();
                }

                await LoadHistoryAsync();
                await CheckBranchStatusAsync();
                await UpdateProjectStatusesAsync();
            }
            finally
            {
                _isSwitchingBranch = false;
            }
        }

        private async Task<bool> HasPendingChangesBeforeBranchSwitchAsync()
        {
            return await _projectShellService.HasPendingChangesAsync(projectDirectory, _changesViewModel);
        }

        private async Task LoadChangesAsync()
        {
            await _projectShellService.LoadChangesAsync(projectDirectory, _changesViewModel);
        }

        private async Task LoadReleasesAsync()
        {
            await _projectShellService.LoadReleasesAsync(projectDirectory, _releasesViewModel);
        }

        private async Task LoadHistoryAsync()
        {
            await _projectShellService.LoadHistoryAsync(projectDirectory, _historyViewModel);
        }

        private async Task LoadWorkspaceAsync()
        {
            await _projectShellService.LoadWorkspaceAsync(projectDirectory, _workspaceViewModel);
        }

        private async Task UpdateAssistantContextAsync()
        {
            await _projectShellService.UpdateAssistantContextAsync(projectDirectory, _assistantViewModel, _documentationViewModel);
        }

        private async Task CheckBranchStatusAsync()
        {
            NeedsPublish = await _projectShellService.CheckNeedsPublishAsync(projectDirectory, _currentlySelectedBranch);
        }

        private async Task RefreshBranchesAsync()
        {
            try
            {
                var snapshot = await _projectShellService.RefreshBranchesAsync(projectDirectory);
                BranchesComboBox.ItemsSource = snapshot.Branches;

                if (!string.IsNullOrWhiteSpace(snapshot.CurrentBranch))
                {
                    _currentlySelectedBranch = snapshot.CurrentBranch;
                    BranchesComboBox.SelectedItem = snapshot.CurrentBranch;
                }
            }
            catch (Exception)
            {

            }
        }

        private async void PublishBranch_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateProject()) return;

            await RunWithLoading(async () =>
            {
                var result = await _gitRepository.PushAsync(projectDirectory, _currentlySelectedBranch);
                if (result.IsSuccess)
                {
                    Msg.Assistant($"Rama '{_currentlySelectedBranch}' publicada en origin.");
                    await CheckBranchStatusAsync();
                }
                else
                {
                    await DialogService.ShowConfirmDialog("Error al publicar", $"No se pudo publicar la rama: {result.Error}", DialogVariant.Error, DialogType.Info);
                }
            });
        }

        #region  UI Helpers
        private void ShowLoading() => LoadingOverlay.Visibility = Visibility.Visible;
        private void HideLoading() => LoadingOverlay.Visibility = Visibility.Collapsed;

        public async Task RunWithLoading(Func<Task> action)
        {
            try { ShowLoading(); await action(); }
            finally { HideLoading(); }
        }
        #endregion

        #region Project Context Menu Handlers
        private string GetPathFromMenuItem(object sender)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is string path)
            {
                return Path.IsPathRooted(path) ? path : Path.Combine(projectDirectory ?? "", path);
            }
            return null;
        }

        private void ProjectMenuItem_OpenVSCode_Click(object sender, RoutedEventArgs e)
        {
            string path = GetPathFromMenuItem(sender);
            if (string.IsNullOrEmpty(path)) return;
            try { Process.Start(new ProcessStartInfo { FileName = "code", Arguments = $"\"{path}\"", UseShellExecute = true }); }
            catch (Exception ex) { Msg.Assistant($"Error al abrir VS Code: {ex.Message}"); }
        }

        private void ProjectMenuItem_OpenVisualStudio_Click(object sender, RoutedEventArgs e)
        {
            string path = GetPathFromMenuItem(sender);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var sln = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                    .FirstOrDefault(f =>
                        f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase));

                if (sln != null)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = sln,
                        UseShellExecute = true
                    });
                }
                else
                {
                    Msg.Assistant("No se encontro ningun archivo .sln o .slnx");
                }
            }
            catch (UnauthorizedAccessException)
            {
                Msg.Assistant("No tienes permisos para acceder a algunas carpetas.");
            }
            catch (Exception ex)
            {
                Msg.Assistant($"Error al abrir Visual Studio: {ex.Message}");
            }
        }

        private void ProjectMenuItem_OpenExplorer_Click(object sender, RoutedEventArgs e)
        {
            string path = GetPathFromMenuItem(sender);
            if (string.IsNullOrEmpty(path)) return;
            try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
            catch { Process.Start("explorer.exe", path); }
        }

        private void ProjectMenuItem_OpenAntigravity_Click(object sender, RoutedEventArgs e)
        {
            string path = GetPathFromMenuItem(sender);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                if (path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase) || path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 2)
                    {
                        string distro = parts[1];
                        string linuxPath = "/" + string.Join("/", parts.Skip(2));

                        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        var agyExePath = System.IO.Path.Combine(localAppData, "Programs", "Antigravity", "Antigravity.exe");
                        string remoteUri = $"vscode-remote://wsl+{distro}{linuxPath}";
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = agyExePath,
                            Arguments = $"--folder-uri \"{remoteUri}\"",
                            UseShellExecute = true
                        });
                        return;
                    }
                }

                Process.Start(new ProcessStartInfo { FileName = "antigravity", Arguments = $"\"{path}\"", UseShellExecute = true });
            }
            catch { Msg.Assistant("Antigravity no detectado o error al abrir."); }
        }

        private void ProjectMenuItem_OpenCmd_Click(object sender, RoutedEventArgs e)
        {
            string path = GetPathFromMenuItem(sender);
            if (string.IsNullOrEmpty(path)) return;
            Process.Start(new ProcessStartInfo { FileName = "cmd.exe", WorkingDirectory = path });
        }

        private async void ProjectMenuItem_Remove_Click(object sender, RoutedEventArgs e)
        {
            string path = GetPathFromMenuItem(sender);
            if (string.IsNullOrEmpty(path)) return;
            var confirm = await DialogService.ShowConfirmDialog("Remover Proyecto", $"Seguro que quieres remover '{new DirectoryInfo(path).Name}'?", DialogVariant.Warning, DialogType.Confirm);
            if (confirm) { ProjectSettings.RemoveProject(path); LoadProjects(); }
        }
        #endregion

        #region Project Management

        private async void ShowCloneDialog()
        {
            try
            {
                var viewModel = App.ServiceProvider.GetRequiredService<Chapi.Presentation.ViewModels.CloneRepositoryViewModel>();
                var dialog = new Chapi.Presentation.Views.Dialogs.CloneRepositoryDialog { DataContext = viewModel };

                var result = await DialogService.ShowDialog(dialog);

                if (result is Chapi.Presentation.ViewModels.CloneRepositoryViewModel vm)
                {
                    await RunWithLoading(async () =>
                    {
                        var useCase = App.ServiceProvider.GetRequiredService<Chapi.Application.UseCases.Projects.CloneProjectUseCase>();
                        var cloneResult = await useCase.ExecuteAsync(vm.Url, vm.LocalPath);

                        if (cloneResult.IsSuccess)
                        {
                            Msg.Assistant($"Repositorio clonado exitosamente en {cloneResult.Data}");
                            LoadProjects();
                            SwitchToProject(cloneResult.Data);
                        }
                        else
                        {
                            await DialogService.ShowConfirmDialog("Error", $"No se pudo clonar: {cloneResult.Error}", DialogVariant.Error, DialogType.Info);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Msg.Assistant($"Error: {ex.Message}");
            }
        }

        private async void SelectProject()
        {
            using var folderDialog = new FolderBrowserDialog();
            if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            await RunWithLoading(async () =>
            {
                projectDirectory = folderDialog.SelectedPath;
                ProjectSettings.AddProject(projectDirectory);
                LoadProjects();
                ProjectsComboBox.SelectedItem = ProjectsComboBox.Items.OfType<ProjectViewModel>().FirstOrDefault(p => p.FullPath == projectDirectory);
                Chapi.Infrastructure.Persistence.Rollbacks.RollbackManager.ClearAllRollbacks();
            });
        }

        private async Task CheckGitInstallationAsync()
        {
            _isGitInstalled = _gitRepository.IsGitInstalled();
            if (_isGitInstalled)
            {
                // Estrategia "PreFetch" original: Actualizar todos los proyectos al inicio
                // para que cuando el usuario despliegue el combo, ya vea los numeritos.
                await UpdateProjectStatusesAsync();
            }

            // Verificar si el usuario ha iniciado sesion en algun proveedor Git
            await CheckSetupAsync();
        }

        private async Task CheckSetupAsync()
        {
            var storage = App.ServiceProvider.GetService<ICredentialStorageService>();
            if (storage == null) return;

            bool hasGitHub = await storage.HasCredentialAsync("GitHub");
            bool hasGitLab = await storage.HasCredentialAsync("GitLab");

            if (!hasGitHub && !hasGitLab)
            {
                // Si no hay cuentas configuradas, mostrar el dialogo de conexion (estilo "Setup Inicial")
                await Dispatcher.InvokeAsync(() =>
                {
                    var viewModel = App.ServiceProvider.GetRequiredService<Chapi.Presentation.ViewModels.GitProviderSelectionViewModel>();
                    var dialog = new Chapi.Presentation.Views.Dialogs.GitProviderSelectionDialog(viewModel);
                    dialog.Owner = this;
                    dialog.ShowDialog();
                });
            }
        }

        private async Task DoFetchAndRefreshAsync(bool isSilent = false)
        {
            if (string.IsNullOrEmpty(projectDirectory)) return;
            if (!await _fetchRefreshSemaphore.WaitAsync(0)) return;

            try
            {
                var useCase = App.ServiceProvider.GetService(typeof(UseCases.FetchChangesUseCase)) as UseCases.FetchChangesUseCase;
                if (useCase == null) return;

                var result = await useCase.ExecuteAsync(projectDirectory, isSilent);
                if (!result.IsSuccess)
                    return;
                try { await RefreshBranchesAsync(); } catch { }

                if (_changesViewModel == null)
                    return;

                bool sameProject = string.Equals(_changesViewModel.ProjectPath, projectDirectory, StringComparison.OrdinalIgnoreCase);
                bool shouldRefresh = sameProject && (!isSilent || GitTabs.SelectedItem == ChangesTab);
                if (!shouldRefresh)
                    return;

                if (IsWslPath(projectDirectory))
                {
                    await _changesViewModel.ForceRefreshAsync();
                }
                else
                {
                    await _changesViewModel.RefreshIfNecessaryAsync();
                }
            }
            finally
            {
                _fetchRefreshSemaphore.Release();
            }
        }

        public async Task UpdateProjectStatusesAsync(List<ProjectViewModel>? projects = null)
        {
            if (projects == null)
            {
                if (ProjectsComboBox.ItemsSource is List<ProjectViewModel> list)
                {
                    projects = list.Where(p => p.FullPath == projectDirectory).ToList();
                    if (!projects.Any()) return;
                }
                else return;
            }

            var useCase = App.ServiceProvider.GetRequiredService<Chapi.Application.UseCases.Projects.UpdateProjectIndicatorsUseCase>();

            await Task.Run(async () =>
            {
                var tasks = projects.Select(proj =>
                {
                    return useCase.ExecuteAsync(proj.FullPath, (ahead, behind) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            proj.Ahead = ahead;
                            proj.Behind = behind;
                        });
                    });
                }).ToList();

                await Task.WhenAll(tasks);
            });

            if (projects.Any(p => p.FullPath == projectDirectory) && !ProjectsComboBox.IsDropDownOpen)
            {
                UpdateGitActionButton();
                await RefreshBranchesAsync();
                await CheckBranchStatusAsync();
            }
        }

        private void UpdateGitActionButton()
        {
            if (ProjectsComboBox.SelectedItem is not ProjectViewModel currentProject) return;

            // Accedemos al ComboBoxItem por defecto (indice 0) que usamos como boton dinamico
            if (GitActionsComboBox.Items[0] is ComboBoxItem defaultItem &&
                defaultItem.Content is StackPanel sp &&
                sp.Children.Count >= 2)
            {
                var icon = sp.Children[0] as MaterialDesignThemes.Wpf.PackIcon;
                var textBlock = sp.Children[1] as TextBlock;

                if (currentProject.Behind > 0)
                {
                    _currentGitAction = GitActionState.Pull;
                    GitActionsComboBox.BorderBrush = Brushes.DeepSkyBlue;
                    if (icon != null)
                    {
                        icon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CloudDownloadOutline;
                        icon.Foreground = Brushes.DeepSkyBlue;
                    }
                    if (textBlock != null)
                    {
                        textBlock.Text = $"Pull Origin ({currentProject.Behind})";
                        textBlock.Foreground = Brushes.DeepSkyBlue;
                    }
                }
                else if (currentProject.Ahead > 0)
                {
                    _currentGitAction = GitActionState.Push;
                    GitActionsComboBox.BorderBrush = Brushes.Orange;
                    if (icon != null)
                    {
                        icon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CloudUploadOutline;
                        icon.Foreground = Brushes.Orange;
                    }
                    if (textBlock != null)
                    {
                        textBlock.Text = $"Push Origin ({currentProject.Ahead})";
                        textBlock.Foreground = Brushes.Orange;
                    }
                }
                else
                {
                    _currentGitAction = GitActionState.Fetch;
                    GitActionsComboBox.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
                    if (icon != null)
                    {
                        icon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Refresh;
                        icon.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
                    }
                    if (textBlock != null)
                    {
                        textBlock.Text = "Fetch Origin";
                        textBlock.ClearValue(TextBlock.ForegroundProperty);
                    }
                }

                // Aseguramos que se muestre el item dinamico
                GitActionsComboBox.SelectedIndex = 0;
            }
        }
        #endregion

        private bool ValidateProject()
        {
            if (string.IsNullOrEmpty(projectDirectory) || !Directory.Exists(projectDirectory))
            {
                DialogService.ShowTrayNotification("Error", "Por favor selecciona un proyecto primero.");
                return false;
            }
            return true;
        }

        #region TrayIcon and XAML Event Handlers
        public void SwitchToProject(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var project = ProjectsComboBox.Items.OfType<ProjectViewModel>().FirstOrDefault(p => p.FullPath == path);
            if (project != null)
            {
                ProjectsComboBox.SelectedItem = project;
                if (!IsVisible) Show();
                Activate();
            }
        }

        public void SelectProjectMenu_Click(object sender, RoutedEventArgs e) => SelectProject();

        private void btnAddProject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;

            var contextMenu = new ContextMenu();

            var cloneItem = new MenuItem
            {
                Header = "Clonar Nuevo Repositorio...",
                Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.Add }
            };
            cloneItem.Click += (s, ev) => ShowCloneDialog();

            var addItem = new MenuItem
            {
                Header = "Agregar Repositorio Existente...",
                Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.FolderAdd }
            };
            addItem.Click += (s, ev) => SelectProject();

            contextMenu.Items.Add(cloneItem);
            contextMenu.Items.Add(addItem);

            contextMenu.PlacementTarget = btn;
            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            contextMenu.IsOpen = true;
        }

        public async void CreateNewTemplate()
        {
            try
            {
                var (success, projectName) = await DialogService.ShowInputDialog("Nuevo Proyecto", "Ingrese nombre del proyecto:");
                if (!success || string.IsNullOrWhiteSpace(projectName)) return;

                using var folderDialog = new FolderBrowserDialog();
                if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

                var parentDir = folderDialog.SelectedPath;

                var associateGit = await DialogService.ShowConfirmDialog("Deseas asociar un repositorio remoto ahora?", "Asociar Git");
                string remoteUrl = null;
                if (associateGit)
                {
                    var (remoteSuccess, remoteUrlText) = await DialogService.ShowInputDialog("Repositorio Git", "Ingrese la URL del repositorio remoto:");
                    if (remoteSuccess) remoteUrl = remoteUrlText;
                }

                await RunWithLoading(async () =>
                {
                    var createProjectUseCase = App.ServiceProvider.GetRequiredService<Chapi.Application.UseCases.Projects.CreateProjectUseCase>();
                    var request = new Chapi.Application.UseCases.Projects.CreateProjectRequest(
                        projectName,
                        parentDir,
                        repoUrl,
                        remoteUrl
                    );

                    var result = await createProjectUseCase.ExecuteAsync(request, progress => Msg.Assistant(progress));

                    if (result.IsSuccess)
                    {
                        Msg.Assistant($"Proyecto '{projectName}' creado exitosamente.");
                        LoadProjects();
                        SwitchToProject(result.Data);
                        Chapi.Infrastructure.Persistence.Rollbacks.RollbackManager.ClearAllRollbacks();
                    }
                    else
                    {
                        await DialogService.ShowConfirmDialog("Error", $"No se pudo crear el proyecto: {result.Error}", DialogVariant.Error, DialogType.Info);
                    }
                });
            }
            catch (Exception ex)
            {
                Msg.Assistant($"Error: {ex.Message}");
            }
        }

        public async void GenerateModuleMenu_Click()
        {
            if (!ValidateProject()) return;

            var (okModules, modules) = await DialogService.ShowInputDialog("Crear Modulo", "Ingrese los nombres de los modulos separados por ';':");
            if (!okModules || string.IsNullOrWhiteSpace(modules)) return;

            var (okDb, dbChoice) = await DialogService.ShowInputDialog("Seleccionar Base de Datos", "Ingrese 'S' para Sybase o 'P' para Postgres:");
            if (!okDb || string.IsNullOrWhiteSpace(dbChoice)) return;

            await RunWithLoading(async () =>
            {
                var useCase = App.ServiceProvider.GetRequiredService<Chapi.Application.UseCases.CodeGeneration.GenerateModuleUseCase>();
                var result = await useCase.ExecuteAsync(projectDirectory, modules, dbChoice);

                if (result.IsSuccess)
                {
                    Msg.Assistant("Modulo(s) generado(s) correctamente.");
                }
                else
                {
                    await DialogService.ShowConfirmDialog("Error", result.Error, DialogVariant.Error, DialogType.Info);
                }
            });
        }

        public async void AsociateGitMenu_Click()
        {
            if (!ValidateProject()) return;

            var (success, remoteUrl) = await DialogService.ShowInputDialog("Asociar Git", "Ingrese la URL del repositorio remoto:");
            if (!success || string.IsNullOrWhiteSpace(remoteUrl)) return;

            await RunWithLoading(async () =>
            {
                var associateGitUseCase = App.ServiceProvider.GetRequiredService<UseCases.AssociateGitUseCase>();
                var result = await associateGitUseCase.ExecuteAsync(projectDirectory, remoteUrl);

                if (result.IsSuccess)
                {
                    Msg.Assistant("Repositorio remoto asociado correctamente.");
                    await DoFetchAndRefreshAsync(isSilent: true);
                }
                else
                {
                    await DialogService.ShowConfirmDialog("Error", $"No se pudo asociar el repositorio: {result.Error}", DialogVariant.Error, DialogType.Info);
                }
            });
        }

        public void AddMethod_Click()
        {
            if (!ValidateProject()) return;
            if (!IsVisible) Show();
            Activate();

            var am = new Chapi.Presentation.Views.Agent.AddMethodView(projectDirectory);
            am.Owner = this;
            am.ShowDialog();
        }

        public async void RollbackSelectModule()
        {
            if (!IsVisible) Show();
            Activate();

            var rollbacks = Chapi.Infrastructure.Persistence.Rollbacks.RollbackManager.GetAvailableRollbacks();

            if (!rollbacks.Any())
            {
                await DialogService.ShowConfirmDialog("Informacion", "No hay rollbacks disponibles.", DialogVariant.Info, DialogType.Info);
                return;
            }

            var rollbackView = new Chapi.Presentation.Views.Agent.RollbackSelectorView();
            rollbackView.Owner = this;
            var result = rollbackView.ShowDialog();

            if (result == true)
            {
                Msg.Assistant("Rollback ejecutado correctamente.");
            }
        }

        public void AddClassLog_Click()
        {
            if (!IsVisible) Show();
            Activate();
            GitTabs.SelectedItem = AssistantTab;
        }

        private async void GitTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.OriginalSource is not System.Windows.Controls.TabControl) return;

            if (GitTabs.SelectedItem == ChangesTab && _changesViewModel != null)
            {
                if (!string.IsNullOrEmpty(projectDirectory) &&
                    (projectDirectory.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase) ||
                     projectDirectory.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase)))
                {
                    await _changesViewModel.ForceRefreshAsync();
                }
                else
                {
                    await _changesViewModel.RefreshIfNecessaryAsync();
                }
            }

            if (GitTabs.SelectedItem == TagsTab)
            {
                await LoadReleasesAsync();
            }
        }

        private void ModoAgenteComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isWindowInitialized || ModoAgenteComboBox.SelectedItem is not ComboBoxItem selectedItem) return;

            if (ModoAgenteComboBox.SelectedIndex == 0) return;

            if (selectedItem.Name == "AddMethodItem")
            {
                AddMethod_Click();
            }
            else if (selectedItem.Name == "RollbackItem")
            {
                RollbackSelectModule();
            }
            else if (selectedItem.Name == "SqlGeneratorItem")
            {
                SqlGenerator_Click();
            }

            Dispatcher.BeginInvoke(new Action(() => ModoAgenteComboBox.SelectedIndex = 0));
        }

        private void SqlGenerator_Click()
        {
            if (!IsVisible) Show();
            Activate();

            var sqlView = new Chapi.Presentation.Views.Agent.SqlGeneratorView();
            sqlView.Owner = this;
            sqlView.ShowDialog();
        }


        #endregion

        #region Git Operations Event Handlers
        private async void Branch_Create_Click(object sender, RoutedEventArgs e)
        {
            string? sourceBranch = null;
            if (sender is MenuItem menuItem)
            {
                sourceBranch = menuItem.CommandParameter as string;
            }

            if (string.IsNullOrEmpty(sourceBranch)) sourceBranch = _currentlySelectedBranch;

            if (string.IsNullOrEmpty(sourceBranch)) return;
            if (!ValidateProject()) return;

            var (ok, newBranchName) = await DialogService.ShowInputDialog("Crear Rama", $"Ingrese el nombre de la nueva rama (basada en '{sourceBranch}'):");
            if (!ok || string.IsNullOrWhiteSpace(newBranchName)) return;

            await RunWithLoading(async () =>
            {
                var result = await _gitRepository.CreateBranchAsync(projectDirectory, newBranchName, sourceBranch);
                if (result.IsSuccess)
                {
                    var branches = await _gitRepository.GetBranchesAsync(projectDirectory);
                    BranchesComboBox.ItemsSource = branches;
                    Msg.Assistant($"Rama '{newBranchName}' creada correctamente.");
                }
                else
                {
                    await DialogService.ShowConfirmDialog("Error al crear rama", result.Error, DialogVariant.Error, DialogType.Info);
                }
            });
        }

        private async void Branch_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.CommandParameter is not string branchName) return;
            if (!ValidateProject()) return;

            if (branchName.Equals(_currentlySelectedBranch, StringComparison.OrdinalIgnoreCase))
            {
                await DialogService.ShowConfirmDialog("Error", $"No puedes eliminar la rama '{branchName}' porque es la rama activa.", DialogVariant.Error, DialogType.Info);
                return;
            }

            var confirm = await DialogService.ShowConfirmDialog("Eliminar Rama", $"Estas seguro de eliminar la rama '{branchName}'?", DialogVariant.Warning, DialogType.Confirm);
            if (!confirm) return;

            // Preguntar si borrar remoto tambien
            var confirmRemote = await DialogService.ShowConfirmDialog("Eliminar Remoto", $"Deseas eliminar tambien la rama '{branchName}' del repositorio remoto (origin)?", DialogVariant.Info, DialogType.Confirm);

            await RunWithLoading(async () =>
            {
                var result = await _gitRepository.DeleteBranchAsync(projectDirectory, branchName, force: false, deleteRemote: confirmRemote); // Force false para manual delete, que avise si no esta merged
                if (result.IsSuccess)
                {
                    await RefreshBranchesAsync();
                    Msg.Assistant($" Rama '{branchName}' eliminada{(confirmRemote ? " (Local y Remoto)" : " (Local)")}.");
                }
                else
                {
                    // Si falla por "not fully merged", podemos preguntar si forzar
                    if (result.Error.Contains("not fully merged") || result.Error.Contains("force"))
                    {
                        // Reintentar con force
                        // Pero como estamos dentro de RunWithLoading y DialogService debe correr en UI thread...
                        // Simplificamos mostrando el error, o podriamos mejorar el flujo.
                        await DialogService.ShowConfirmDialog("Error al eliminar rama", result.Error + "\n\nPara forzar el borrado (perdiendo cambios no fusionados), usa la terminal por ahora.", DialogVariant.Error, DialogType.Info);
                    }
                    else
                    {
                        await DialogService.ShowConfirmDialog("Error al eliminar rama", result.Error, DialogVariant.Error, DialogType.Info);
                    }
                }
            });
        }


        private async void Branch_Merge_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateProject()) return;
            string? branch = GetBranchFromSender(sender);

            if (branch != null) await ExecuteGitMergeOperation("Merge", branch);
            else await ShowMergeDialogAsync("Merge");
        }

        private async void Branch_SquashMerge_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateProject()) return;
            string? branch = GetBranchFromSender(sender);

            if (branch != null) await ExecuteGitMergeOperation("Squash", branch);
            else await ShowMergeDialogAsync("Squash");
        }

        private async void Branch_Rebase_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateProject()) return;
            string? branch = GetBranchFromSender(sender);

            if (branch != null) await ExecuteGitMergeOperation("Rebase", branch);
            else await ShowMergeDialogAsync("Rebase");
        }

        private string? GetBranchFromSender(object sender)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is string branchName && !string.IsNullOrWhiteSpace(branchName))
            {
                return branchName;
            }
            return null;
        }

        private async Task ShowMergeDialogAsync(string mergeType)
        {
            // Instanciar VM con dependencias para validacion en vivo
            var viewModel = new Chapi.Presentation.ViewModels.MergeBranchViewModel(_gitRepository, projectDirectory, mergeType);

            // Cargar ramas
            var branches = await _gitRepository.GetBranchesAsync(projectDirectory);
            viewModel.LoadBranches(branches, _currentlySelectedBranch);

            var dialog = new Chapi.Presentation.Views.Dialogs.MergeBranchDialog
            {
                DataContext = viewModel
            };

            var result = await DialogService.ShowDialog(dialog);

            if (result is Chapi.Presentation.ViewModels.BranchItemViewModel selectedBranch)
            {

                await ExecuteGitMergeOperation(mergeType, selectedBranch.Name, autoDeleteBranch: viewModel.IsDeleteSourceBranchChecked);
            }
        }

        private async Task ExecuteGitMergeOperation(string mergeType, string targetBranch, bool autoDeleteBranch = false)
        {

            string sourceBranch = _currentlySelectedBranch;

            if (targetBranch.Equals(sourceBranch, StringComparison.OrdinalIgnoreCase))
            {
                await DialogService.ShowConfirmDialog("Error", $"No puedes hacer {mergeType.ToLower()} de una rama consigo misma.", DialogVariant.Error, DialogType.Info);
                return;
            }
            if (mergeType != "Rebase")
            {
                var (hasConflicts, conflictMessage) = await _gitRepository.CheckMergeConflictsAsync(projectDirectory, targetBranch);
                if (hasConflicts)
                {
                    await DialogService.ShowConfirmDialog(
                        "Conflictos Detectados",
                        $"No se puede enviar '{sourceBranch}' a '{targetBranch}' porque hay conflictos pendientes.\n\nSOLUCION: Primero debes fusionar '{targetBranch}' en tu rama actual y resolver los conflictos.",
                        DialogVariant.Error,
                        DialogType.Info);
                    return;
                }
            }

            var status = await _gitRepository.GetChangesAsync(projectDirectory);
            if (status.Any())
            {
                await DialogService.ShowConfirmDialog(
                    "Cambios Pendientes",
                    "Para hacer merge hacia otra rama, tu directorio de trabajo debe estar limpio.\n\nPor favor haz commit o stash de tus cambios actuales antes de continuar.",
                    DialogVariant.Warning,
                    DialogType.Info);
                return;
            }

            var prompt = "";
            DialogVariant variant = DialogVariant.Info;

            if (mergeType == "Squash")
            {
                prompt = $"Estas seguro de hacer SQUASH MERGE de '{sourceBranch}' en '{targetBranch}'?\n\nEl sistema cambiara a '{targetBranch}', realizara la operacion y volvera.";
            }
            else if (mergeType == "Rebase")
            {
                prompt = $"EL REBASE REQUERIRA FORCE PUSH\n\n" +
                         $"Estas seguro de que deseas hacer rebase a '{sourceBranch}' de '{targetBranch}'?\n\n" +
                         $"Al finalizar el rebase, tu historia local cambiara y divergiras del remoto.\n" +
                         $"Para actualizar el servidor, necesitaras hacer un FORCE PUSH posteriormente.\n" +
                         $"Esto alterara la historia en el remoto y podria causar problemas a otros colaboradores en esta rama.\n\n" +
                         $"Deseas continuar?";
                variant = DialogVariant.Warning;
            }
            else
            {
                prompt = $"Estas seguro de fusionar '{sourceBranch}' en '{targetBranch}'?\n\nEl sistema cambiara a '{targetBranch}', realizara la operacion y volvera.";
            }

            string? squashCommitMessage = null;
            bool shouldDeleteBranch = autoDeleteBranch; // Heredamos del dialogo anterior por defecto

            if (mergeType == "Squash")
            {
                var squashDialog = new Chapi.Presentation.Views.Dialogs.SquashCommitDialog(_gitRepository, projectDirectory, sourceBranch, targetBranch, autoDeleteBranch);
                // El dialogo de Squash recibe el checkbox inicial del merge dialog para informar si se eliminara o no (opcionalmente podriamos mostrarlo readonly)
                // O simplemente asumimos que la decision ya fue tomada. 

                var resultDialog = await DialogService.ShowDialog(squashDialog);

                if (resultDialog is bool confirmed && confirmed)
                {
                    squashCommitMessage = squashDialog.CommitMessage;
                    // shouldDeleteBranch sigue siendo lo que vino por parametro (autoDeleteBranch) 
                    // o si decidimos dar oportunidad de cambio en el squash dialog (que acabamos de quitar), entonces no cambia.
                }
                else
                {
                    return;
                }
            }
            else
            {
                // Si NO es squash (ej. Merge normal o Rebase), mostramos confirmacion
                // Y usamos 'shouldDeleteBranch' que vino de parametro 'autoDeleteBranch'
                // Usamos el 'variant' definido arriba (Warning para Rebase, Info para Merge)
                var confirm = await DialogService.ShowConfirmDialog($"{mergeType} operation", prompt, variant, DialogType.Confirm);
                if (!confirm) return;
            }

            await RunWithLoading(async () =>
            {
                Result result = Result.Fail("Iniciando...");

                try
                {
                    if (mergeType == "Rebase")
                    {
                        result = await _gitRepository.RebaseBranchAsync(projectDirectory, targetBranch);
                    }
                    else
                    {
                        // A. Ir al destino
                        var checkoutTarget = await _gitRepository.SwitchBranchAsync(projectDirectory, targetBranch);
                        if (!checkoutTarget.IsSuccess) throw new Exception($"No se pudo cambiar a '{targetBranch}': {checkoutTarget.Error}");

                        // B. Realizar Merge/Squash (trayendo Source)
                        if (mergeType == "Merge")
                            result = await _gitRepository.MergeBranchAsync(projectDirectory, sourceBranch, fastForward: true);
                        else // Squash
                            result = await _gitRepository.SquashMergeBranchAsync(projectDirectory, sourceBranch, squashCommitMessage);
                    }

                    if (result.IsSuccess)
                    {
                        Msg.Assistant($" Operacion '{mergeType}' exitosa: '{sourceBranch}' '{targetBranch}'");

                        if (mergeType == "Rebase")
                        {
                            // En Rebase nos quedamos en la rama original (sourceBranch), no cambiamos a target.
                            // Por lo tanto NO actualizamos _currentlySelectedBranch a targetBranch.

                            var forcePushConfirm = await DialogService.ShowConfirmDialog(
                                "Rebase Exitoso - Force Push Requerido",
                                "La rama actual se ha rebasado correctamente.\n\nTu historia local ha divergido del remoto.\nDeseas realizar un FORCE PUSH ahora para actualizar el servidor?\n(Solo hazlo si estas seguro de que nadie mas trabaja sobre esta rama)",
                                DialogVariant.Warning,
                                DialogType.Confirm);

                            if (forcePushConfirm)
                            {
                                var pushResult = await _gitRepository.PushAsync(projectDirectory, sourceBranch, force: true);
                                if (pushResult.IsSuccess)
                                {
                                    Msg.Assistant($"Force Push exitoso: '{sourceBranch}' actualizado en remoto.");
                                }
                                else
                                {
                                    await DialogService.ShowConfirmDialog("Error Force Push", pushResult.Error, DialogVariant.Error, DialogType.Info);
                                }
                            }

                            shouldDeleteBranch = false; // Nunca eliminar la rama actual en un rebase
                        }
                        else
                        {
                            // Flujo normal para Merge/Squash: Ya nos movimos a targetBranch
                            _currentlySelectedBranch = targetBranch;
                            BranchesComboBox.SelectedItem = targetBranch;

                            // Preguntar si quiere hacer Push de la rama DESTINO (targetBranch)
                            var pushConfirm = await DialogService.ShowConfirmDialog(
                                "Push al Servidor",
                                $"El merge local en '{targetBranch}' fue exitoso.\n\nQuieres subir (Push) los cambios de '{targetBranch}' a origin ahora mismo para que se reflejen en GitHub/GitLab?",
                                DialogVariant.Info,
                                DialogType.Confirm);

                            if (pushConfirm)
                            {
                                var pushResult = await _gitRepository.PushAsync(projectDirectory, targetBranch);
                                if (pushResult.IsSuccess)
                                {
                                    Msg.Assistant($"Push exitoso: '{targetBranch}' actualizado en remoto.");
                                }
                                else
                                {
                                    await DialogService.ShowConfirmDialog("Error al hacer Push", pushResult.Error, DialogVariant.Error, DialogType.Info);
                                }
                            }
                        }

                        // Eliminacion de rama: Aplica tanto para Squash como para Merge normal si el usuario lo pidio
                        // (En Squash viene del SquashDialog, en Merge viene del autodeleteBranch pasado)
                        if (shouldDeleteBranch && mergeType != "Rebase")
                        {
                            // Intentamos borrar tanto local como remoto para limpieza completa
                            var deleteResult = await _gitRepository.DeleteBranchAsync(projectDirectory, sourceBranch, force: true, deleteRemote: true);

                            if (deleteResult.IsSuccess)
                            {
                                Msg.Assistant($"Rama '{sourceBranch}' eliminada (Local y Remoto).");
                            }
                            else
                            {
                                await DialogService.ShowConfirmDialog("Aviso", $"Se intento eliminar la rama '{sourceBranch}' pero hubo un problema: {deleteResult.Error}", DialogVariant.Warning, DialogType.Info);
                            }
                        }

                        await LoadChangesAsync();
                        await LoadHistoryAsync();
                        await UpdateProjectStatusesAsync();
                        await RefreshBranchesAsync();
                    }
                    else
                    {
                        if (result.Error == "CONFLICTO_DETECTADO")
                        {
                            await HandleMergeConflictsAsync();
                            return; // Terminamos la operacion actual y abrimos la resolucion.
                        }
                        throw new Exception(result.Error);
                    }
                }
                catch (Exception ex)
                {
                    if (_currentlySelectedBranch != sourceBranch)
                        await _gitRepository.SwitchBranchAsync(projectDirectory, sourceBranch);

                    await DialogService.ShowConfirmDialog($"Error en {mergeType}", $"Ocurrio un error: {ex.Message}", DialogVariant.Error, DialogType.Info);
                    await LoadChangesAsync();
                }
            });
        }





        private async void btnReloadChanges_Click(object sender, RoutedEventArgs e)
        {
            await LoadChangesAsync();
        }


        private enum GitActionState { Pull, Push, Fetch }
        private GitActionState _currentGitAction = GitActionState.Fetch;

        private bool _isExecutingGitAction = false;

        private async void GitActionsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isWindowInitialized || _isExecutingGitAction || GitActionsComboBox.SelectedItem is not ComboBoxItem selectedItem) return;

            GitActionState? action = null;

            if (selectedItem.Name == "PullGitActionItem") action = GitActionState.Pull;
            else if (selectedItem.Name == "PushGitActionItem") action = GitActionState.Push;
            else if (selectedItem.Name == "FetchGitActionItem") action = GitActionState.Fetch;

            if (action.HasValue)
            {
                _isExecutingGitAction = true;
                GitActionsComboBox.SelectedIndex = 0;
                _isExecutingGitAction = false;

                await ExecuteGitAction(action.Value);
            }
        }

        private async Task ExecuteGitAction(GitActionState action)
        {
            if (!ValidateProject()) return;

            // Legacy pre-check kept disabled; pull flow now is handled on demand by git error parsing.
            if (false && action == GitActionState.Pull)
            {
                var changes = await _gitRepository.GetChangesAsync(projectDirectory);
                if (changes.Any())
                {
                    var proceedWithStash = await DialogService.ShowConfirmDialog(
                        "Cambios sin confirmar",
                        "Tienes cambios locales que podrian entrar en conflicto. Deseas guardarlos automaticamente en un Stash antes de hacer Pull?",
                        DialogVariant.Warning,
                        DialogType.Confirm,
                        confirmButtonText: "Guardar y continuar",
                        cancelButtonText: "Cancelar");
                    if (!proceedWithStash)
                    {
                        return;
                    }
                    // Disabled: stash decision is now taken only when pull reports overwrite risk.
                }
            }

            await RunWithLoading(async () =>
            {
                Chapi.Domain.Common.Result result = Chapi.Domain.Common.Result.Success();
                switch (action)
                {
                    case GitActionState.Fetch:
                        var fetchUC = App.ServiceProvider.GetRequiredService<Chapi.Application.UseCases.Git.FetchChangesUseCase>();
                        result = await fetchUC.ExecuteAsync(projectDirectory, isSilent: false);
                        break;

                    case GitActionState.Pull:
                        var pullUC = App.ServiceProvider.GetRequiredService<Chapi.Application.UseCases.Git.PullChangesUseCase>();
                        result = await pullUC.ExecuteAsync(projectDirectory, _currentlySelectedBranch, stashChanges: false);
                        if (!result.IsSuccess && Chapi.Application.UseCases.Git.PullChangesUseCase.IsLocalChangesOverwriteError(result.Error))
                        {
                            var conflictingFiles = ExtractFilesFromPullOverwriteError(result.Error);
                            var details = BuildPullOverwriteDetailsAscii(conflictingFiles);
                            var proceedWithStash = await DialogService.ShowConfirmDialog(
                                "No se puede hacer Pull",
                                details,
                                DialogVariant.Warning,
                                DialogType.Confirm,
                                confirmButtonText: "Guardar cambios y continuar",
                                cancelButtonText: "Cancelar");

                            if (!proceedWithStash)
                            {
                                return;
                            }

                            result = await pullUC.ExecuteAsync(projectDirectory, _currentlySelectedBranch, stashChanges: true, restoreAfterPull: false);
                        }
                        break;

                    case GitActionState.Push:
                        var pushUC = App.ServiceProvider.GetRequiredService<Chapi.Application.UseCases.Git.PushChangesUseCase>();
                        result = await pushUC.ExecuteAsync(projectDirectory, _currentlySelectedBranch);
                        break;
                }

                await LoadHistoryAsync();
                await UpdateProjectStatusesAsync();
                if (action != GitActionState.Fetch)
                {
                    DoFetchAndRefreshAsync(isSilent: true).Forget("sincronizando cambios despues de accion git");
                }

                if (_changesViewModel != null &&
                    string.Equals(_changesViewModel.ProjectPath, projectDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    await _changesViewModel.ForceRefreshAsync();
                }

                if (!result.IsSuccess && result.Error == "CONFLICTO_DETECTADO")
                {
                    await HandleMergeConflictsAsync();
                }
            });
        }

        private async void GitActionsComboBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            await ExecuteGitAction(_currentGitAction);
        }

        private void GitActionsComboBox_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            GitActionsComboBox.IsDropDownOpen = true;
        }

        private async Task HandleMergeConflictsAsync()
        {
            try
            {
                var getConflictsUC = App.ServiceProvider.GetRequiredService<Chapi.Application.UseCases.Git.GetConflictsUseCase>();
                var resolveConflictUC = App.ServiceProvider.GetRequiredService<Chapi.Application.UseCases.Git.ResolveConflictUseCase>();

                var viewModel = new Chapi.Presentation.ViewModels.ConflictResolutionViewModel(projectDirectory, getConflictsUC, resolveConflictUC);
                await viewModel.LoadConflictsAsync();

                if (viewModel.Conflicts.Any())
                {
                    var dialog = new Chapi.Presentation.Views.Dialogs.ConflictResolutionDialog(viewModel);
                    await DialogService.ShowDialog(dialog);

                    // Recargar luego de (posible) resolucion
                    await LoadChangesAsync();
                    await LoadHistoryAsync();
                    await UpdateProjectStatusesAsync();
                }
                else
                {
                    Msg.Assistant("No se encontraron conflictos a revisar o ya estan resueltos.");
                }
            }
            catch (Exception ex)
            {
                Msg.Assistant($"Error abriendo ventana de conflictos: {ex.Message}");
            }
        }
        #endregion

        public void ProcessExternalArguments(string args)
        {
            if (string.IsNullOrWhiteSpace(args)) return;

            string path = args.Trim('\"');

            if (Directory.Exists(path))
            {
                SwitchToProject(path);

                if (ProjectsComboBox.SelectedItem == null || ((ProjectViewModel)ProjectsComboBox.SelectedItem).FullPath != path)
                {
                    ProjectSettings.AddProject(path);
                    LoadProjects();
                    SwitchToProject(path);
                }
            }
            else if (File.Exists(path))
            {
                string? dir = Path.GetDirectoryName(path);
                if (dir != null) ProcessExternalArguments(dir);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isShuttingDown)
            {
                CleanupResources();
                return;
            }

            e.Cancel = true;
            Hide();
        }

        public void ForceShutdown()
        {
            _isShuttingDown = true;
            System.Windows.Application.Current.Shutdown();
        }

        private void CleanupResources()
        {
            if (_fetchTimer != null)
            {
                _fetchTimer.Stop();
            }
        }

        public void KillExternalBlockers()
        {
            try
            {
                string appPath = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                // Escapar la ruta para PowerShell
                string escapedPath = appPath.Replace("'", "''");

                // Script de PowerShell para encontrar procesos con handles en la carpeta actual
                // Filtramos por procesos conocidos por causar problemas (wslhost, sqlservr, conhost, git)
                string script = $@"
                    $path = '{escapedPath}'
                    $processes = Get-Process | Where-Object {{ $_.Name -match 'wslhost|sqlservr|conhost|git|Update' }}
                    foreach ($p in $processes) {{
                        try {{
                            $hasHandle = $false
                            # Usamos el comando 'handle' de Sysinternals si estuviera, 
                            # pero como fallback buscamos procesos cuyo CWD o modulos esten ahi
                            if ($p.MainModule.FileName -like ""$path*"" -or $p.StartInfo.WorkingDirectory -like ""$path*"") {{
                                $hasHandle = $true
                            }}
                            if ($hasHandle) {{
                                Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
                            }}
                        }} catch {{ }}
                    }}
                    # Caso especial para WSL eliminado por ser demasiado agresivo.
                    # Se reemplaza por refresco por foco en OnActivated.
                ";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                process?.WaitForExit(5000);
            }
            catch (Exception) { /* Fallback silencioso */ }
        }

        private DateTime _lastActivationRefresh = DateTime.MinValue;
        private DateTime _lastActivationFetch = DateTime.MinValue;

        protected override async void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            // Auto-fetch liviano al recuperar foco para detectar ramas remotas nuevas.
            if (!string.IsNullOrEmpty(projectDirectory) &&
                (DateTime.Now - _lastActivationFetch).TotalSeconds > 90)
            {
                _lastActivationFetch = DateTime.Now;
                DoFetchAndRefreshAsync(isSilent: true).Forget("sincronizando cambios al activar la ventana");
            }

            if (!string.IsNullOrEmpty(projectDirectory) && IsWslPath(projectDirectory))
            {
                if ((DateTime.Now - _lastActivationRefresh).TotalSeconds > 2)
                {
                    _lastActivationRefresh = DateTime.Now;
                    if (_changesViewModel != null)
                    {
                        await _changesViewModel.ForceRefreshAsync();
                    }
                }
            }
            else if (GitTabs.SelectedItem == ChangesTab && _changesViewModel != null)
            {
                await _changesViewModel.RefreshIfNecessaryAsync();
            }
        }

        private static List<string> ExtractFilesFromPullOverwriteError(string error)
        {
            var files = new List<string>();
            if (string.IsNullOrWhiteSpace(error))
                return files;

            var lines = error.Replace('\r', '\n')
                             .Split('\n', StringSplitOptions.RemoveEmptyEntries);

            bool readingFiles = false;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (!readingFiles)
                {
                    if (line.Contains("following files would be overwritten", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("archivos", StringComparison.OrdinalIgnoreCase) && line.Contains("sobrescrit", StringComparison.OrdinalIgnoreCase))
                    {
                        readingFiles = true;
                    }
                    continue;
                }

                if (line.StartsWith("Please ", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Aborta", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("error:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("hint:", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var candidate = line.TrimStart('-', '*', ' ', '\t');
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    files.Add(candidate);
                }
            }

            return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string BuildPullOverwriteDetailsAscii(List<string> files)
        {
            var header = "No se puede hacer Pull porque estos archivos locales serian sobrescritos.";
            var guidance = "Puedes guardar tus cambios en un Stash y continuar, o cancelar para revisarlos.";

            if (files == null || files.Count == 0)
                return $"{header}\n\n{guidance}";

            var max = Math.Min(files.Count, 12);
            var listed = string.Join("\n", files.Take(max).Select(f => $"- {f}"));
            var more = files.Count > max ? $"\n- ... y {files.Count - max} archivo(s) mas" : string.Empty;

            return $"{header}\n\n{listed}{more}\n\n{guidance}";
        }

        private static bool IsWslPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            return path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase);
        }
    }
}









