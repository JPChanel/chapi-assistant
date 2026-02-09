using Chapi.Infrastructure.AI;
using Chapi.Domain.Entities;
using Chapi.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Chapi.Infrastructure.Persistence.Settings;
using Chapi.Domain.Models;
using Chapi.Infrastructure.Services;
using Chapi.Presentation.Views.Dialogs;
using MaterialDesignThemes.Wpf;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using Velopack;
using Velopack.Sources;
using UseCases = Chapi.Application.UseCases.Git;
using Chapi.Domain.Common;

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

        private List<string> _repositories = new List<string>();
        private FileSystemWatcher _fileWatcher;
        private System.Threading.Timer _debounceTimer;
        private readonly object _lock = new object();
        private bool _isReloadingChanges = false;
        private bool _isGitInstalled = false;
        private System.Windows.Threading.DispatcherTimer _fetchTimer;
        private CancellationTokenSource? _projectSwitchCts;

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
        private readonly IGitRepository _gitRepository;

        public MainWindow()
        {
            InitializeComponent();
            Instance = this;
            DataContext = MessageHelper.Instance;
            
            _gitRepository = App.ServiceProvider.GetRequiredService<IGitRepository>();
            _changesViewModel = App.ServiceProvider.GetService(typeof(Presentation.ViewModels.ChangesViewModel)) as Presentation.ViewModels.ChangesViewModel;
            _historyViewModel = App.ServiceProvider.GetService(typeof(Presentation.ViewModels.HistoryViewModel)) as Presentation.ViewModels.HistoryViewModel;
            _releasesViewModel = App.ServiceProvider.GetService(typeof(Presentation.ViewModels.ReleasesViewModel)) as Presentation.ViewModels.ReleasesViewModel;
            
            ChangesTab.DataContext = _changesViewModel;
            HistoryTab.DataContext = _historyViewModel;
            TagsTab.DataContext = _releasesViewModel;

            Msg.Assistant("👋 ¡Hey! Soy Chapi 🤖 Tu dev buddy para arquitectura.");

            _debounceTimer = new System.Threading.Timer(OnDebounceTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
            Task.Run(CheckForUpdates);
            LoadVersion();

            _fetchTimer = new System.Windows.Threading.DispatcherTimer();
            _fetchTimer.Interval = TimeSpan.FromMinutes(10);
            _fetchTimer.Tick += async (s, ev) => await DoFetchAsync(isSilent: true);
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
            _ = Task.Run(async () => 
            {
                var storage = App.ServiceProvider.GetService<Chapi.Domain.Interfaces.ICredentialStorageService>();
                if (storage != null)
                {
                    await Chapi.Domain.Services.AvatarCacheService.Instance.PreloadAvatarsAsync(storage);
                }
            });
            _ = Task.Run(async () => await CheckGitInstallationAsync());

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
        }

        private async Task CheckForUpdates()
        {
            try
            {
                var mgr = new UpdateManager(new GithubSource(updateUrl, null, false));
                var info = await mgr.CheckForUpdatesAsync();
                if (info == null) return;
                Dispatcher.Invoke(() => Msg.Assistant($"📢 Nueva version v{info.TargetFullRelease.Version} disponible."));
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
            _repositories = ProjectSettings.LoadProjects();
            var projectVMs = _repositories.Select(r => new ProjectViewModel
            {
                FullPath = r,
                Name = new DirectoryInfo(r).Name,
                Icon = PackIconKind.FolderOutline
            }).ToList();

            ProjectsComboBox.ItemsSource = projectVMs;
            App.TrayIconManager?.UpdateProjectList(projectVMs);

            // Ejecutar la actualizacion de estados con retardo para no competir por CPU/Disco al inicio
            _ = Task.Run(async () => 
            {
                await Task.Delay(1500); // Dar prioridad a la UI inicial
                await UpdateProjectStatusesAsync(projectVMs);
            });
        }

        private void OnDebounceTimerElapsed(object state)
        {
            lock (_lock) { if (_isReloadingChanges) return; _isReloadingChanges = true; }
            Dispatcher.InvokeAsync(async () =>
            {
                try { await LoadChangesAsync(); }
                finally { lock (_lock) { _isReloadingChanges = false; } }
            });
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            if (projectDirectory == null || e.FullPath.Contains(".git")) return;
            _debounceTimer?.Change(500, Timeout.Infinite);
        }

        private void InitializeFileSystemWatcher(string path)
        {
            if (_fileWatcher != null) { _fileWatcher.EnableRaisingEvents = false; _fileWatcher.Dispose(); }
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

            _fileWatcher = new FileSystemWatcher(path) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName };
            _fileWatcher.Changed += OnFileSystemChanged;
            _fileWatcher.Created += OnFileSystemChanged;
            _fileWatcher.Deleted += OnFileSystemChanged;
            _fileWatcher.Renamed += (s, ev) => OnFileSystemChanged(s, ev);
            _fileWatcher.EnableRaisingEvents = true;
        }

        private async void ProjectsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProjectsComboBox.SelectedItem is not ProjectViewModel selectedProject) return;
            
            // Cancelar cualquier secuencia de carga anterior
            _projectSwitchCts?.Cancel();
            _projectSwitchCts = new CancellationTokenSource();
            var token = _projectSwitchCts.Token;

            projectDirectory = selectedProject.FullPath;
            
            // Limpieza visual inmediata en el ViewModel de cambios
            if (_changesViewModel != null)
            {
                _changesViewModel.ProjectPath = projectDirectory; // Esto ya dispara LoadChangesAsync interno que ahora limpia la lista
            }

            InitializeFileSystemWatcher(projectDirectory);
            
            if (!_isGitInstalled) return;

            App.TrayIconManager?.UpdateProjectMenuItem(selectedProject.Name, false);

            try
            {
                // Carga inicial rápida de ramas
                var getBranchesUseCase = App.ServiceProvider.GetService(typeof(UseCases.GetBranchesUseCase)) as UseCases.GetBranchesUseCase;
                var branches = (await getBranchesUseCase.ExecuteAsync(projectDirectory)).ToList();
                if (token.IsCancellationRequested) return;

                string activeBranch = await _gitRepository.GetCurrentBranchAsync(projectDirectory);
                if (token.IsCancellationRequested) return;
                
                if (!string.IsNullOrEmpty(activeBranch) && !branches.Contains(activeBranch))
                {
                    branches.Add(activeBranch);
                }

                BranchesComboBox.ItemsSource = branches;
                if (!string.IsNullOrEmpty(activeBranch))
                {
                    _currentlySelectedBranch = activeBranch;
                    BranchesComboBox.SelectedItem = activeBranch;
                    UpdateGitActionButton();
                    
                    if (selectedProject.Ahead > 0)
                    {
                        Msg.Assistant($"🚀 Tienes {selectedProject.Ahead} commits pendientes de subir en '{selectedProject.Name}'. ¡No olvides hacer Push!");
                    }
                }

                // Cargas pesadas en segundo plano con validación de token
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // No necesitamos llamar a LoadChangesAsync de nuevo aquí porque ya se disparó al poner ProjectPath
                        if (token.IsCancellationRequested) return;
                        
                        await Dispatcher.InvokeAsync(async () => {
                            if (!token.IsCancellationRequested) await LoadHistoryAsync();
                        });
                        
                        if (token.IsCancellationRequested) return;
                        await Task.Delay(50, token); 

                        await Dispatcher.InvokeAsync(async () => {
                            if (!token.IsCancellationRequested) 
                            {
                                await LoadReleasesAsync();
                                await CheckBranchStatusAsync();
                                await DoFetchAsync(isSilent: true);
                            }
                        });
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception) { }
                }, token);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al cambiar de proyecto: {ex.Message}");
            }
        }

        private async void BranchesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BranchesComboBox.SelectedItem is not string newBranch || newBranch == _currentlySelectedBranch) return;

            await RunWithLoading(async () =>
            {
                // Verificar si hay cambios pendientes
                var changes = await _gitRepository.GetChangesAsync(projectDirectory);
                bool hasChanges = changes.Any();

                bool stashChanges = false;

                if (hasChanges)
                {
                    // Mostrar dialogo de opciones
                    var dialog = new Chapi.Presentation.Views.Dialogs.SwitchBranchDialog
                    {
                        TargetBranch = newBranch
                    };
                    var result = await DialogService.ShowDialog(dialog);

                    if (result == null || result.ToString() == "cancel")
                    {
                        // Usuario cancelo, revertir seleccion
                        BranchesComboBox.SelectedItem = _currentlySelectedBranch;
                        return;
                    }

                    stashChanges = result.ToString() == "stash";
                }

                var useCase = App.ServiceProvider.GetService(typeof(UseCases.SwitchBranchUseCase)) as UseCases.SwitchBranchUseCase;
                var switchResult = await useCase.ExecuteAsync(projectDirectory, newBranch, stashChanges);
                
                if (switchResult.IsSuccess) 
                {
                    _currentlySelectedBranch = newBranch;
                }
                else 
                {
                    BranchesComboBox.SelectedItem = _currentlySelectedBranch;
                    await DialogService.ShowConfirmDialog("No se pudo cambiar de rama", switchResult.Error, DialogVariant.Error, DialogType.Info);
                }
            });

            await LoadChangesAsync();
            await LoadHistoryAsync();
            await CheckBranchStatusAsync();    
            await UpdateProjectStatusesAsync();
        }

        private async Task LoadChangesAsync()
        {
            if (string.IsNullOrEmpty(projectDirectory) || _changesViewModel == null) return;
            _changesViewModel.ProjectPath = projectDirectory;
            await _changesViewModel.LoadChangesAsync();
        }

        private async Task LoadReleasesAsync()
        {
            if (string.IsNullOrEmpty(projectDirectory) || _releasesViewModel == null) return;
            _releasesViewModel.ProjectPath = projectDirectory;
            await _releasesViewModel.LoadReleasesAsync();
        }

        private async Task LoadHistoryAsync()
        {
            if (string.IsNullOrEmpty(projectDirectory) || _historyViewModel == null) return;
            _historyViewModel.ProjectPath = projectDirectory;
            await _historyViewModel.ReloadHistoryAsync();
        }

        private async Task CheckBranchStatusAsync()
        {
            if (string.IsNullOrEmpty(projectDirectory) || string.IsNullOrEmpty(_currentlySelectedBranch))
            {
                NeedsPublish = false;
                return;
            }

            NeedsPublish = !await _gitRepository.HasUpstreamAsync(projectDirectory, _currentlySelectedBranch);
        }
        
        private async Task RefreshBranchesAsync()
        {
            try 
            {
                // Refrescar ramas tras operaciones que pueden crearlas o borrarlas
                var branches = (await _gitRepository.GetBranchesAsync(projectDirectory)).ToList();
                string activeBranch = await _gitRepository.GetCurrentBranchAsync(projectDirectory);
                
                if (!string.IsNullOrEmpty(activeBranch) && !branches.Contains(activeBranch))
                {
                    branches.Add(activeBranch);
                }

                BranchesComboBox.ItemsSource = branches;
                
                if (!string.IsNullOrEmpty(activeBranch))
                {
                    _currentlySelectedBranch = activeBranch;
                    BranchesComboBox.SelectedItem = activeBranch;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refrescando ramas: {ex.Message}");
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
                    Msg.Assistant($"✅ Rama '{_currentlySelectedBranch}' publicada en origin.");
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
                var sln = Directory.EnumerateFiles(path)
                   .FirstOrDefault(f =>
                       f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                       f.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase));
                if (sln != null) Process.Start(new ProcessStartInfo { FileName = sln, UseShellExecute = true });
            }
            catch (Exception ex) { Msg.Assistant($"Error al abrir Visual Studio: {ex.Message}"); }
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
            var confirm = await DialogService.ShowConfirmDialog("Remover Proyecto", $"Â¿Seguro que quieres remover '{new DirectoryInfo(path).Name}'?", DialogVariant.Warning, DialogType.Confirm);
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
                            Msg.Assistant($"✅ Repositorio clonado exitosamente en {cloneResult.Data}");
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
                Msg.Assistant($"❌ Error: {ex.Message}");
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
        }

        private async Task DoFetchAsync(bool isSilent = false)
        {
            if (string.IsNullOrEmpty(projectDirectory)) return;
            var useCase = App.ServiceProvider.GetService(typeof(UseCases.FetchChangesUseCase)) as UseCases.FetchChangesUseCase;
            if (useCase != null)
            {
                await useCase.ExecuteAsync(projectDirectory, isSilent);
                await LoadChangesAsync();
                
                // Actualizar indicadores despues del fetch
                await UpdateProjectStatusesAsync();
            }
        }

        public async Task UpdateProjectStatusesAsync(List<ProjectViewModel>? projects = null)
        {
            if (!Dispatcher.CheckAccess())
            {
                await Dispatcher.InvokeAsync(async () => await UpdateProjectStatusesAsync(projects));
                return;
            }

            if (projects == null)
            {
                if (ProjectsComboBox.ItemsSource is List<ProjectViewModel> list) projects = list;
                else return;
            }

            var useCase = App.ServiceProvider.GetRequiredService<Chapi.Application.UseCases.Projects.UpdateProjectIndicatorsUseCase>();

            var tasks = projects.Select(proj => useCase.ExecuteAsync(proj.FullPath, (ahead, behind) =>
            {
                Dispatcher.Invoke(() =>
                {
                    bool changed = proj.Ahead != ahead || proj.Behind != behind;
                    proj.Ahead = ahead;
                    proj.Behind = behind;                
                    if ((changed || proj.FullPath == projectDirectory) && proj.FullPath == projectDirectory && !ProjectsComboBox.IsDropDownOpen)
                    {
                        UpdateGitActionButton();
                    }
                });
            })).ToList();

            await Task.WhenAll(tasks);
        }

        private void UpdateGitActionButton()
        {
            if (ProjectsComboBox.SelectedItem is not ProjectViewModel currentProject) return;

            // Accedemos al ComboBoxItem por defecto (índice 0) que usamos como botón dinámico
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
                        textBlock.Text = $"Pull Origin ({currentProject.Behind} ↓)";
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
                        textBlock.Text = $"Push Origin ({currentProject.Ahead} ↑)";
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

                var associateGit = await DialogService.ShowConfirmDialog("¿Deseas asociar un repositorio remoto ahora?", "Asociar Git");
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
                        Msg.Assistant($"✅ Proyecto '{projectName}' creado exitosamente.");
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
                Msg.Assistant($"❌ Error: {ex.Message}");
            }
        }

        public async void GenerateModuleMenu_Click()
        {
            if (!ValidateProject()) return;

            var (okModules, modules) = await DialogService.ShowInputDialog("Crear Módulo", "Ingrese los nombres de los módulos separados por ';':");
            if (!okModules || string.IsNullOrWhiteSpace(modules)) return;

            var (okDb, dbChoice) = await DialogService.ShowInputDialog("Seleccionar Base de Datos", "Ingrese 'S' para Sybase o 'P' para Postgres:");
            if (!okDb || string.IsNullOrWhiteSpace(dbChoice)) return;

            await RunWithLoading(async () =>
            {
                var useCase = App.ServiceProvider.GetRequiredService<Chapi.Application.UseCases.CodeGeneration.GenerateModuleUseCase>();
                var result = await useCase.ExecuteAsync(projectDirectory, modules, dbChoice);

                if (result.IsSuccess)
                {
                    Msg.Assistant("✅ Módulo(s) generado(s) correctamente.");
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
                    Msg.Assistant("✅ Repositorio remoto asociado correctamente.");
                    await DoFetchAsync(isSilent: true);
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
                await DialogService.ShowConfirmDialog("Información", "No hay rollbacks disponibles.", DialogVariant.Info, DialogType.Info);
                return;
            }

            var rollbackView = new Chapi.Presentation.Views.Agent.RollbackSelectorView();
            rollbackView.Owner = this;
            var result = rollbackView.ShowDialog();

            if (result == true)
            {
                Msg.Assistant("✅ Rollback ejecutado correctamente.");
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
                    Msg.Assistant($"✅ Rama '{newBranchName}' creada correctamente.");
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

            var confirm = await DialogService.ShowConfirmDialog("Eliminar Rama", $"¿Estás seguro de eliminar la rama '{branchName}'?", DialogVariant.Warning, DialogType.Confirm);
            if (!confirm) return;

            // Preguntar si borrar remoto también
            var confirmRemote = await DialogService.ShowConfirmDialog("Eliminar Remoto", $"¿Deseas eliminar también la rama '{branchName}' del repositorio remoto (origin)?", DialogVariant.Info, DialogType.Confirm);

            await RunWithLoading(async () =>
            {
                var result = await _gitRepository.DeleteBranchAsync(projectDirectory, branchName, force: false, deleteRemote: confirmRemote); // Force false para manual delete, que avise si no esta merged
                if (result.IsSuccess)
                {
                    await RefreshBranchesAsync();
                    Msg.Assistant($"✅ Rama '{branchName}' eliminada{(confirmRemote ? " (Local y Remoto)" : " (Local)")}.");
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
            // Instanciar VM con dependencias para validación en vivo
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
                        "⚠️ Conflictos Detectados",
                        $"No se puede enviar '{sourceBranch}' a '{targetBranch}' porque hay conflictos pendientes.\n\nSOLUCIÓN: Primero debes fusionar '{targetBranch}' en tu rama actual y resolver los conflictos.",
                        DialogVariant.Error,
                        DialogType.Info);
                    return;
                }
            }

            var status = await _gitRepository.GetChangesAsync(projectDirectory);
            if (status.Any())
            {
                await DialogService.ShowConfirmDialog(
                    "⚠️ Cambios Pendientes",
                    "Para hacer merge hacia otra rama, tu directorio de trabajo debe estar limpio.\n\nPor favor haz commit o stash de tus cambios actuales antes de continuar.",
                    DialogVariant.Warning,
                    DialogType.Info);
                return;
            }

            var prompt = "";
            DialogVariant variant = DialogVariant.Info;

            if (mergeType == "Squash")
            {
                prompt = $"¿Estás seguro de hacer SQUASH MERGE de '{sourceBranch}' en '{targetBranch}'?\n\nEl sistema cambiará a '{targetBranch}', realizará la operación y volverá.";
            }
            else if (mergeType == "Rebase")
            {
                prompt = $"⚠️ EL REBASE REQUERIRÁ FORCE PUSH\n\n" +
                         $"¿Estás seguro de que deseas hacer rebase a '{sourceBranch}' de '{targetBranch}'?\n\n" +
                         $"Al finalizar el rebase, tu historia local cambiará y divergirás del remoto.\n" +
                         $"Para actualizar el servidor, necesitarás hacer un FORCE PUSH posteriormente.\n" +
                         $"Esto alterará la historia en el remoto y podría causar problemas a otros colaboradores en esta rama.\n\n" +
                         $"¿Deseas continuar?";
                variant = DialogVariant.Warning;
            }
            else
            {
                prompt = $"¿Estás seguro de fusionar '{sourceBranch}' en '{targetBranch}'?\n\nEl sistema cambiará a '{targetBranch}', realizará la operación y volverá.";
            }

            string? squashCommitMessage = null;
            bool shouldDeleteBranch = autoDeleteBranch; // Heredamos del dialogo anterior por defecto

            if (mergeType == "Squash")
            {
                 var squashDialog = new Chapi.Presentation.Views.Dialogs.SquashCommitDialog(_gitRepository, projectDirectory, sourceBranch, targetBranch, autoDeleteBranch);
                 // El dialogo de Squash recibe el checkbox inicial del merge dialog para informar si se eliminará o no (opcionalmente podriamos mostrarlo readonly)
                 // O simplemente asumimos que la decisión ya fue tomada. 
                 
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
                // Si NO es squash (ej. Merge normal o Rebase), mostramos confirmación
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
                        Msg.Assistant($"✅ Operación '{mergeType}' exitosa: '{sourceBranch}' → '{targetBranch}'");

                        if (mergeType == "Rebase")
                        {
                            // En Rebase nos quedamos en la rama original (sourceBranch), no cambiamos a target.
                            // Por lo tanto NO actualizamos _currentlySelectedBranch a targetBranch.

                            var forcePushConfirm = await DialogService.ShowConfirmDialog(
                                "Rebase Exitoso - Force Push Requerido",
                                "La rama actual se ha rebasado correctamente.\n\n⚠️ Tu historia local ha divergido del remoto.\n¿Deseas realizar un FORCE PUSH ahora para actualizar el servidor?\n(Solo hazlo si estás seguro de que nadie más trabaja sobre esta rama)",
                                DialogVariant.Warning,
                                DialogType.Confirm);
                            
                            if (forcePushConfirm)
                            {
                                var pushResult = await _gitRepository.PushAsync(projectDirectory, sourceBranch, force: true);
                                if (pushResult.IsSuccess)
                                {
                                    Msg.Assistant($"🚀 Force Push exitoso: '{sourceBranch}' actualizado en remoto.");
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
                                $"El merge local en '{targetBranch}' fue exitoso.\n\n¿Quieres subir (Push) los cambios de '{targetBranch}' a origin ahora mismo para que se reflejen en GitHub/GitLab?",
                                DialogVariant.Info,
                                DialogType.Confirm);

                            if (pushConfirm)
                            {
                                var pushResult = await _gitRepository.PushAsync(projectDirectory, targetBranch);
                                if (pushResult.IsSuccess)
                                {
                                    Msg.Assistant($"🚀 Push exitoso: '{targetBranch}' actualizado en remoto.");
                                }
                                else
                                {
                                    await DialogService.ShowConfirmDialog("Error al hacer Push", pushResult.Error, DialogVariant.Error, DialogType.Info);
                                }
                            }
                        }

                        // Eliminación de rama: Aplica tanto para Squash como para Merge normal si el usuario lo pidió
                        // (En Squash viene del SquashDialog, en Merge viene del autodeleteBranch pasado)
                        if (shouldDeleteBranch && mergeType != "Rebase")
                        {
                            // Intentamos borrar tanto local como remoto para limpieza completa
                            var deleteResult = await _gitRepository.DeleteBranchAsync(projectDirectory, sourceBranch, force: true, deleteRemote: true);
                            
                            if (deleteResult.IsSuccess)
                            {
                                Msg.Assistant($"🗑️ Rama '{sourceBranch}' eliminada (Local y Remoto).");
                            }
                            else
                            {
                                await DialogService.ShowConfirmDialog("Aviso", $"Se intentó eliminar la rama '{sourceBranch}' pero hubo un problema: {deleteResult.Error}", DialogVariant.Warning, DialogType.Info);
                            }
                        }

                        await LoadChangesAsync();
                        await LoadHistoryAsync();
                        await UpdateProjectStatusesAsync();
                        await RefreshBranchesAsync();
                    }
                    else
                    {
                        throw new Exception(result.Error);
                    }
                }
                catch (Exception ex)
                {   
                    if (_currentlySelectedBranch != sourceBranch)
                        await _gitRepository.SwitchBranchAsync(projectDirectory, sourceBranch);

                    await DialogService.ShowConfirmDialog($"Error en {mergeType}", $"Ocurrió un error: {ex.Message}", DialogVariant.Error, DialogType.Info);
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

            bool stashBeforePull = false;
            if (action == GitActionState.Pull)
            {
                var changes = await _gitRepository.GetChangesAsync(projectDirectory);
                if (changes.Any())
                {
                    stashBeforePull = await DialogService.ShowConfirmDialog(
                        "Cambios sin confirmar",
                        "Tienes cambios locales que podrían entrar en conflicto. ¿Deseas guardarlos automáticamente en un Stash antes de hacer Pull?",
                        DialogVariant.Info);
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
                        result = await pullUC.ExecuteAsync(projectDirectory, _currentlySelectedBranch, stashBeforePull);
                        break;
                    
                    case GitActionState.Push:
                        var pushUC = App.ServiceProvider.GetRequiredService<Chapi.Application.UseCases.Git.PushChangesUseCase>();
                        result = await pushUC.ExecuteAsync(projectDirectory, _currentlySelectedBranch);
                        break;
                }

                await LoadHistoryAsync();
                await UpdateProjectStatusesAsync(); 
                _ = DoFetchAsync(isSilent: true);
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
            e.Cancel = true;
            Hide();
        }
    }
}







