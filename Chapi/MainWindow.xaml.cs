using Chapi.Infrastructure.AI;
using Chapi.Domain.Entities;
using Chapi.Infrastructure.Git;
using Chapi.Infrastructure.Roslyn;
using Chapi.Infrastructure.Persistence.Settings;
using Chapi.Domain.Models;
using Chapi.Infrastructure.Services;
using Chapi.Presentation.Views.Tabs;
using Chapi.Presentation.Views.Agent;
using Chapi.Presentation.Views.Settings;
using Chapi.Presentation.Views.Dialogs;
using MaterialDesignThemes.Wpf;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
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

        private List<string> _repositories = new List<string>();
        private FileSystemWatcher _fileWatcher;
        private System.Threading.Timer _debounceTimer;
        private readonly object _lock = new object();
        private bool _isReloadingChanges = false;
        private bool _isGitInstalled = false;
        private System.Windows.Threading.DispatcherTimer _fetchTimer;

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

        public MainWindow()
        {
            InitializeComponent();
            Instance = this;
            DataContext = MessageHelper.Instance;
            
            _changesViewModel = App.ServiceProvider.GetService(typeof(Presentation.ViewModels.ChangesViewModel)) as Presentation.ViewModels.ChangesViewModel;
            _historyViewModel = App.ServiceProvider.GetService(typeof(Presentation.ViewModels.HistoryViewModel)) as Presentation.ViewModels.HistoryViewModel;
            
            ChangesTab.DataContext = _changesViewModel;
            HistoryTab.DataContext = _historyViewModel;

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
            _isWindowInitialized = true;
            LoadProjects();
            await RunWithLoading(CheckGitInstallationAsync);
            
            // Suscribir eventos entre ViewModels
            if (_changesViewModel != null)
            {
                _changesViewModel.CommitCompleted += async (s, e) => await LoadHistoryAsync();
            }
            if (_historyViewModel != null)
            {
                _historyViewModel.ResetCompleted += async (s, e) => await LoadChangesAsync();
            }
        }

        private async Task CheckForUpdates()
        {
            try
            {
                var mgr = new UpdateManager(new GithubSource(updateUrl, null, false));
                var info = await mgr.CheckForUpdatesAsync();
                if (info == null) return;
                Msg.Assistant($"ðŸ“¢ Nueva version v{info.TargetFullRelease.Version} disponible.");
            }
            catch { }
        }

        private bool ValidateProject()
        {
            if (string.IsNullOrEmpty(projectDirectory))
            {
                Msg.Assistant("âš ï¸ No hay proyecto seleccionado.");
                return false;
            }
            return true;
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
            _ = UpdateProjectStatusesAsync(projectVMs);
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
            projectDirectory = selectedProject.FullPath;
            InitializeFileSystemWatcher(projectDirectory);
            
            if (!_isGitInstalled) return;

            App.TrayIconManager?.UpdateProjectMenuItem(selectedProject.Name, false);

            var getBranchesUseCase = App.ServiceProvider.GetService(typeof(UseCases.GetBranchesUseCase)) as UseCases.GetBranchesUseCase;
            var branches = (await getBranchesUseCase.ExecuteAsync(projectDirectory)).ToList();
            BranchesComboBox.ItemsSource = branches;

            string activeBranch = await Git.GetCurrentBranch(projectDirectory);
            if (!string.IsNullOrEmpty(activeBranch))
            {
                _currentlySelectedBranch = activeBranch;
                BranchesComboBox.SelectedItem = activeBranch;
            }

            await LoadChangesAsync();
            await LoadHistoryAsync();
            await CheckBranchStatusAsync();
            _ = DoFetchAsync(isSilent: true);
        }

        private async void BranchesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BranchesComboBox.SelectedItem is not string newBranch || newBranch == _currentlySelectedBranch) return;

            await RunWithLoading(async () =>
            {
                // Verificar si hay cambios pendientes
                var statusOutput = await Git.EjecutarGit("status --porcelain", projectDirectory);
                bool hasChanges = !string.IsNullOrWhiteSpace(statusOutput);

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
                
                if (switchResult.IsSuccess) _currentlySelectedBranch = newBranch;
                else BranchesComboBox.SelectedItem = _currentlySelectedBranch;
            });

            await LoadChangesAsync();
            await LoadHistoryAsync();
            await CheckBranchStatusAsync();
        }

        private async Task LoadChangesAsync()
        {
            if (string.IsNullOrEmpty(projectDirectory) || _changesViewModel == null) return;
            _changesViewModel.ProjectPath = projectDirectory;
            await _changesViewModel.LoadChangesAsync();
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

            NeedsPublish = !await Git.HasUpstream(_currentlySelectedBranch, projectDirectory);
        }

        private async void PublishBranch_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateProject()) return;

            await RunWithLoading(async () =>
            {
                var result = await Git.EjecutarGit($"push -u origin {_currentlySelectedBranch}", projectDirectory);
                if (!result.Contains("fatal:") && !result.Contains("error:"))
                {
                    Msg.Assistant($"âœ… Rama '{_currentlySelectedBranch}' publicada en origin.");
                    await CheckBranchStatusAsync();
                }
                else
                {
                    await DialogService.ShowConfirmDialog("Error al publicar", $"No se pudo publicar la rama: {result}", DialogVariant.Error, DialogType.Info);
                }
            });
        }

        #region âœ… UI Helpers
        private void ShowLoading() => LoadingOverlay.Visibility = Visibility.Visible;
        private void HideLoading() => LoadingOverlay.Visibility = Visibility.Collapsed;

        public async Task RunWithLoading(Func<Task> action)
        {
            try { ShowLoading(); await action(); }
            finally { HideLoading(); }
        }
        #endregion

        #region âœ… Project Context Menu Handlers
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
                string sln = Directory.GetFiles(path, "*.sln").FirstOrDefault();
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
            try { Process.Start(new ProcessStartInfo { FileName = "antigravity", Arguments = $"\"{path}\"", UseShellExecute = true }); }
            catch { Msg.Assistant("Antigravity no detectado."); }
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

        #region âœ… Project Management
        private void btnAddProject_Click(object sender, RoutedEventArgs e)
        {
            var contextMenu = new ContextMenu();

            var cloneMenuItem = new MenuItem { Header = "Clonar Repositorio", Icon = new PackIcon { Kind = PackIconKind.SourceBranch } };
            cloneMenuItem.Click += (s, ev) => ShowCloneDialog();
            contextMenu.Items.Add(cloneMenuItem);

            var addMenuItem = new MenuItem { Header = "Agregar Repositorio Existente", Icon = new PackIcon { Kind = PackIconKind.FolderAdd } };
            addMenuItem.Click += (s, ev) => SelectProject();
            contextMenu.Items.Add(addMenuItem);

            

            contextMenu.IsOpen = true;
        }

        private async void ShowCloneDialog()
        {
           Msg.Assistant("Funcionalidad Clonar en desarrollo.");
           // Aqui iria la logica para mostrar el dialogo de clonacion
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
            });
        }

        private async Task CheckGitInstallationAsync() => _isGitInstalled = Git.IsGitInstalled();

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

        private async Task UpdateProjectStatusesAsync(List<ProjectViewModel> projects = null)
        {
            if (projects == null)
            {
                if (ProjectsComboBox.ItemsSource is List<ProjectViewModel> list) projects = list;
                else return;
            }

            await Task.Delay(1500);

            await Task.Run(async () =>
            {
                foreach (var proj in projects)
                {
                    try
                    {
                        string branch = await Git.GetCurrentBranch(proj.FullPath);
                        if (!string.IsNullOrEmpty(branch))
                        {
                            var status = await Git.GetAheadBehindCount(proj.FullPath);
                            
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                proj.Ahead = status.Ahead;
                                proj.Behind = status.Behind;
                            });
                        }
                    }
                    catch { /* Ignorar errores en proyectos inaccesibles */ }
                }
            });
        }
        #endregion

        #region âœ… TrayIcon and XAML Event Handlers
        public void SwitchToProject(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            projectDirectory = path;
            ProjectsComboBox.SelectedItem = ProjectsComboBox.Items.OfType<ProjectViewModel>().FirstOrDefault(p => p.FullPath == path);
        }

        public void SelectProjectMenu_Click(object sender, RoutedEventArgs e) => SelectProject();

        public void CreateNewTemplate()
        {
            // Placeholder para crear nuevo template
            Msg.Assistant("Funcionalidad de crear template en desarrollo.");
        }

        public void GenerateModuleMenu_Click()
        {
            // Placeholder para generar modulo
            Msg.Assistant("Funcionalidad de generar modulo en desarrollo.");
        }

        public void AsociateGitMenu_Click()
        {
            // Placeholder para asociar Git
            Msg.Assistant("Funcionalidad de asociar Git en desarrollo.");
        }

        public void AddMethod_Click()
        {
            // Placeholder para agregar metodo
            Msg.Assistant("Funcionalidad de agregar metodo en desarrollo.");
        }

        public void RollbackSelectModule()
        {
            // Placeholder para rollback
            Msg.Assistant("Funcionalidad de rollback en desarrollo.");
        }

        public void AddClassLog_Click()
        {
            // Placeholder para log
            Msg.Assistant("Funcionalidad de log en desarrollo.");
        }

        private void GitTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Placeholder para cambio de pestana Git
        }

        private void ModoAgenteComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Placeholder para cambio de modo agente
        }

        private void ReleasesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Placeholder para cambio de release
        }
        #endregion

        #region âœ… Git Operations Event Handlers
        private async void Branch_Create_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.CommandParameter is not string sourceBranch) return;
            if (!ValidateProject()) return;

            var (ok, newBranchName) = await DialogService.ShowInputDialog("Crear Rama", $"Ingrese el nombre de la nueva rama (basada en '{sourceBranch}'):");
            if (!ok || string.IsNullOrWhiteSpace(newBranchName)) return;

            await RunWithLoading(async () =>
            {
                var result = await Git.EjecutarGit($"branch {newBranchName} {sourceBranch}", projectDirectory);
                if (!result.Contains("fatal:") && !result.Contains("error:"))
                {
                    var branches = Git.GetBranches(projectDirectory);
                    BranchesComboBox.ItemsSource = branches;
                    Msg.Assistant($"âœ… Rama '{newBranchName}' creada correctamente.");
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

            var confirm = await DialogService.ShowConfirmDialog("Eliminar Rama", $"Â¿Estas seguro de eliminar la rama '{branchName}'?", DialogVariant.Warning, DialogType.Confirm);
            if (!confirm) return;

            await RunWithLoading(async () =>
            {
                var result = await Git.EjecutarGit($"branch -d \"{branchName}\"", projectDirectory);
                if (!result.Contains("fatal:") && !result.Contains("error:"))
                {
                    var branches = Git.GetBranches(projectDirectory);
                    BranchesComboBox.ItemsSource = branches;
                    Msg.Assistant($"âœ… Rama '{branchName}' eliminada.");
                }
            });
        }

        private async void btnCrearTag_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateProject()) return;

            var (okTag, tagName) = await DialogService.ShowInputDialog("Crear Tag", "Ingrese el nombre del tag (ej: v1.0.0):");
            if (!okTag || string.IsNullOrWhiteSpace(tagName)) return;

            var (okMsg, tagMessage) = await DialogService.ShowInputDialog("Mensaje del Tag", "Ingrese un mensaje para el tag:", $"Release {tagName}");
            if (!okMsg || string.IsNullOrWhiteSpace(tagMessage)) return;

            await RunWithLoading(async () =>
            {
                var result = await Git.CreateTag(tagName, tagMessage, projectDirectory);
                if (result.Success)
                {
                    Msg.Assistant($"âœ… Tag '{tagName}' creado correctamente.");
                }
                else
                {
                    await DialogService.ShowConfirmDialog("Error", $"No se pudo crear el tag:\n{result.Output}", DialogVariant.Error, DialogType.Info);
                }
            });
        }

        private async void DeleteTag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.CommandParameter is not string tagName) return;
            if (!ValidateProject()) return;

            var confirm = await DialogService.ShowConfirmDialog("Eliminar Tag", $"Â¿Estas seguro de eliminar el tag '{tagName}'?", DialogVariant.Warning, DialogType.Confirm);
            if (!confirm) return;

            await RunWithLoading(async () =>
            {
                var result = await Git.DeleteTagLocal(tagName, projectDirectory);
                if (result.Success)
                {
                    Msg.Assistant($"âœ… Tag '{tagName}' eliminado.");
                }
            });
        }

        private async void btnReloadChanges_Click(object sender, RoutedEventArgs e)
        {
            await LoadChangesAsync();
        }

        private Git.AheadBehindResult _currentGitStatus = new(0, 0);
        private enum GitActionState { Pull, Push, Fetch }
        private GitActionState _currentGitAction = GitActionState.Fetch;

        private async void GitActionsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isWindowInitialized || GitActionsComboBox.SelectedItem == null) return;
            // Placeholder para acciones Git
        }

        private async void GitActionsComboBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_isWindowInitialized) return;
            // Placeholder para acciones Git
        }
        #endregion

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }
    }
}







