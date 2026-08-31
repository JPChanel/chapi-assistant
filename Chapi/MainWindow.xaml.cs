using Chapi.Domain.Models;
using Chapi.Infrastructure.Persistence.Settings;
using Chapi.Infrastructure.Services;
using Chapi.Presentation.Features.ActivityOverview.ViewModels;
using Chapi.Presentation.Features.ActivityOverview.Views;
using Chapi.Presentation.Features.Assistant.ViewModels;
using Chapi.Presentation.Features.Changes.ViewModels;
using Chapi.Presentation.Features.Documentation.ViewModels;
using Chapi.Presentation.Features.Git.Models;
using Chapi.Presentation.Features.Git.Services;
using Chapi.Presentation.Features.History.ViewModels;
using Chapi.Presentation.Features.Projects.Models;
using Chapi.Presentation.Features.Projects.Services;
using Chapi.Presentation.Features.Projects.ViewModels;
using Chapi.Presentation.Features.Releases.ViewModels;
using Chapi.Presentation.Features.Workspace.ViewModels;
using Chapi.Presentation.Shared.Dialogs.Views;
using Chapi.Presentation.Shared.Tasks;
using Chapi.Presentation.Startup.Models;
using Chapi.Presentation.Startup.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using DialogResult = System.Windows.Forms.DialogResult;
using System.Windows.Media;
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
        public string AppVersion { get; private set; }
        public string ServiceStatusText => "Activo";
        public Brush ServiceStatusBrush => Brushes.Lime;

        private bool _needsPublish;
        public bool NeedsPublish
        {
            get => _needsPublish;
            set { _needsPublish = value; OnPropertyChanged(nameof(NeedsPublish)); }
        }

        private ChangesViewModel? _changesViewModel;
        private HistoryViewModel? _historyViewModel;
        private ReleasesViewModel? _releasesViewModel;
        private WorkspaceViewModel? _workspaceViewModel;
        private AssistantViewModel? _assistantViewModel;
        private DocumentationViewModel? _documentationViewModel;
        private ActivityOverviewWindow? _activityOverviewWindow;
        private readonly GitWorkflowCoordinator _gitWorkflowCoordinator;
        private readonly ProjectShellService _projectShellService;
        private readonly ProjectSyncCoordinator _projectSyncCoordinator;
        private readonly ProjectToolLauncher _projectToolLauncher;
        private readonly StartupTaskCoordinator _startupTaskCoordinator;
        private readonly Chapi.Domain.Interfaces.IGitRepository _gitRepository;

        public MainWindow()
        {
            InitializeComponent();
            Instance = this;
            DataContext = MessageHelper.Instance;

            _gitWorkflowCoordinator = App.ServiceProvider.GetRequiredService<GitWorkflowCoordinator>();
            _projectShellService = App.ServiceProvider.GetRequiredService<ProjectShellService>();
            _projectSyncCoordinator = App.ServiceProvider.GetRequiredService<ProjectSyncCoordinator>();
            _projectToolLauncher = App.ServiceProvider.GetRequiredService<ProjectToolLauncher>();
            _startupTaskCoordinator = App.ServiceProvider.GetRequiredService<StartupTaskCoordinator>();
            _gitRepository = App.ServiceProvider.GetRequiredService<Chapi.Domain.Interfaces.IGitRepository>();
            _changesViewModel = App.ServiceProvider.GetService(typeof(ChangesViewModel)) as ChangesViewModel;
            _historyViewModel = App.ServiceProvider.GetService(typeof(HistoryViewModel)) as HistoryViewModel;
            _releasesViewModel = App.ServiceProvider.GetService(typeof(ReleasesViewModel)) as ReleasesViewModel;
            _assistantViewModel = App.ServiceProvider.GetService(typeof(AssistantViewModel)) as AssistantViewModel;
            _workspaceViewModel = App.ServiceProvider.GetService(typeof(WorkspaceViewModel)) as WorkspaceViewModel;
            _documentationViewModel = App.ServiceProvider.GetService(typeof(DocumentationViewModel)) as DocumentationViewModel;

            ChangesTab.DataContext = _changesViewModel;
            HistoryTab.DataContext = _historyViewModel;
            TagsTab.DataContext = _releasesViewModel;
            WorkspaceTab.DataContext = _workspaceViewModel;
            AssistantViewControl.DataContext = _assistantViewModel;
            DocumentationViewControl.DataContext = _documentationViewModel;

            Msg.Assistant("Hey! Soy Chapi. Tu dev buddy para arquitectura.", showAlert: false);

            _startupTaskCoordinator.CheckForUpdatesAsync(updateUrl).Forget("buscando actualizaciones");
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
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            _startupTaskCoordinator.HandleWindowLoadedAsync(CreateStartupTaskContext())
                .Forget("inicializando ventana principal");
        }


        public void ShowUpdateView()
        {
            var updateView = new Chapi.Presentation.Features.Settings.Views.UpdateView(projectDirectory);
            updateView.Owner = this;
            updateView.ShowDialog();
        }

        private void LogoButton_Click(object sender, RoutedEventArgs e) => ShowUpdateView();

        private async void GlobalActivityButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activityOverviewWindow != null)
            {
                _activityOverviewWindow.Show();
                _activityOverviewWindow.Activate();
                return;
            }

            var viewModel = App.ServiceProvider.GetRequiredService<ActivityOverviewViewModel>();
            viewModel.NavigateToProject = path =>
            {
                Dispatcher.Invoke(() => SwitchToProject(path));
            };

            _activityOverviewWindow = new ActivityOverviewWindow
            {
                Owner = this,
                DataContext = viewModel
            };
            _activityOverviewWindow.Closed += (_, _) => _activityOverviewWindow = null;

            _activityOverviewWindow.Show();
            _activityOverviewWindow.Activate();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            await viewModel.LoadAsync();
        }

        private void FloatingOptionsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button || button.ContextMenu is null)
                return;

            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }

        private void FloatingMenu_Services_Click(object sender, RoutedEventArgs e)
        {
            ShowUpdateView();
        }

        private void FloatingMenu_ActivityLog_Click(object sender, RoutedEventArgs e)
        {
            GlobalActivityButton_Click(sender, e);
        }

        private void LoadProjects()
        {
            var projectVMs = _projectShellService.LoadProjects().ToList();

            ProjectsComboBox.ItemsSource = projectVMs;
            App.TrayIconManager?.UpdateProjectList(projectVMs);

            // Ejecutar la actualizacion de estados con retardo para no competir por CPU/Disco al inicio
            DelayedUpdateProjectStatusesAsync(projectVMs).Forget("actualizando estados de proyectos");
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
                _changesViewModel.SetLiveRefreshEnabled(GitTabs.SelectedItem == ChangesTab);
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

            // Verificar si la carpeta del proyecto es un repositorio Git
            bool isGit = await _gitRepository.IsGitRepositoryAsync(projectDirectory);
            if (!isGit)
            {
                BranchesComboBox.ItemsSource = new List<string>();
                BranchesComboBox.SelectedItem = null;
                _currentlySelectedBranch = string.Empty;

                var initConfirm = await DialogService.ShowConfirmDialog(
                    "Repositorio no inicializado",
                    $"El proyecto '{selectedProject.Name}' no tiene Git inicializado.\n\n¿Deseas inicializarlo como repositorio Git ahora?",
                    DialogVariant.Info,
                    DialogType.Confirm,
                    confirmButtonText: "INICIALIZAR",
                    cancelButtonText: "MÁS TARDE");

                if (initConfirm)
                {
                    ShowCreateRepositoryDialog(projectDirectory);
                }
                return;
            }

            App.TrayIconManager?.UpdateProjectMenuItem(selectedProject.Name, false);

            try
            {
                var request = new ProjectSelectionRequest
                {
                    ProjectPath = projectDirectory,
                    ProjectName = selectedProject.Name,
                    ChangesViewModel = _changesViewModel,
                    HistoryViewModel = _historyViewModel,
                    ReleasesViewModel = _releasesViewModel,
                    WorkspaceViewModel = _workspaceViewModel,
                    AssistantViewModel = _assistantViewModel,
                    DocumentationViewModel = _documentationViewModel
                };

                var snapshot = await _projectShellService.LoadProjectContextAsync(request, token);

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

                _projectShellService.WarmProjectContextAsync(request, token).Forget("cargando contexto del proyecto");
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

            _isSwitchingBranch = true;
            try
            {
                await _gitWorkflowCoordinator.SwitchBranchAsync(CreateGitWorkflowContext(), newBranch);
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
            await _gitWorkflowCoordinator.PublishBranchAsync(CreateGitWorkflowContext());
        }

        #region  UI Helpers
        private void ShowLoading() => LoadingOverlay.Visibility = Visibility.Visible;
        private void HideLoading() => LoadingOverlay.Visibility = Visibility.Collapsed;

        public async Task RunWithLoading(Func<Task> action)
        {
            try { ShowLoading(); await action(); }
            finally { HideLoading(); }
        }

        private StartupTaskContext CreateStartupTaskContext()
        {
            return new StartupTaskContext
            {
                Owner = this,
                MarkWindowInitialized = () => _isWindowInitialized = true,
                SetGitInstalled = isInstalled => _isGitInstalled = isInstalled,
                LoadProjects = LoadProjects,
                UpdateProjectStatusesAsync = () => UpdateProjectStatusesAsync(),
                LoadHistoryAsync = LoadHistoryAsync,
                RefreshChangesAfterResetAsync = async () =>
                {
                    if (_changesViewModel != null)
                    {
                        await _changesViewModel.ForceRefreshAsync();
                        return;
                    }

                    await LoadChangesAsync();
                },
                ChangesViewModel = _changesViewModel,
                HistoryViewModel = _historyViewModel,
                ReleasesViewModel = _releasesViewModel
            };
        }

        private ProjectSyncContext CreateProjectSyncContext()
        {
            return new ProjectSyncContext
            {
                ProjectPath = projectDirectory,
                GetLoadedProjects = () =>
                {
                    return ProjectsComboBox.ItemsSource as IReadOnlyList<ProjectViewModel>
                        ?? ProjectsComboBox.Items.OfType<ProjectViewModel>().ToList();
                },
                GetChangesProjectPath = () => _changesViewModel?.ProjectPath,
                IsProjectDropdownOpen = () => ProjectsComboBox.IsDropDownOpen,
                IsChangesTabActive = () => GitTabs.SelectedItem == ChangesTab,
                IsWslProject = () => IsWslPath(projectDirectory),
                RefreshBranchesAsync = RefreshBranchesAsync,
                CheckBranchStatusAsync = CheckBranchStatusAsync,
                ForceRefreshChangesAsync = async () =>
                {
                    if (_changesViewModel != null)
                    {
                        await _changesViewModel.ForceRefreshAsync();
                    }
                },
                RefreshChangesIfNecessaryAsync = async () =>
                {
                    if (_changesViewModel != null)
                    {
                        await _changesViewModel.RefreshIfNecessaryAsync();
                    }
                }
            };
        }

        private GitWorkflowContext CreateGitWorkflowContext()
        {
            return new GitWorkflowContext
            {
                ProjectPath = projectDirectory,
                GetCurrentBranch = () => _currentlySelectedBranch,
                SetCurrentBranch = branch => _currentlySelectedBranch = branch,
                SelectBranch = branch => BranchesComboBox.SelectedItem = branch,
                HasPendingChangesAsync = HasPendingChangesBeforeBranchSwitchAsync,
                RunWithLoadingAsync = RunWithLoading,
                LoadChangesAsync = LoadChangesAsync,
                LoadHistoryAsync = LoadHistoryAsync,
                RefreshBranchesAsync = RefreshBranchesAsync,
                CheckBranchStatusAsync = CheckBranchStatusAsync,
                UpdateProjectStatusesAsync = () => UpdateProjectStatusesAsync(),
                ForceRefreshChangesAsync = async () =>
                {
                    if (_changesViewModel != null)
                    {
                        await _changesViewModel.ForceRefreshAsync();
                    }
                },
                SyncProjectAsync = () => DoFetchAndRefreshAsync(isSilent: true),
                SuspendWatcher = () => _changesViewModel?.SuspendWatcher()
            };
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
            _projectToolLauncher.OpenVSCode(path);
        }

        private void ProjectMenuItem_OpenVisualStudio_Click(object sender, RoutedEventArgs e)
        {
            string path = GetPathFromMenuItem(sender);
            if (string.IsNullOrEmpty(path)) return;
            _projectToolLauncher.OpenVisualStudio(path);
        }

        private void ProjectMenuItem_OpenExplorer_Click(object sender, RoutedEventArgs e)
        {
            string path = GetPathFromMenuItem(sender);
            if (string.IsNullOrEmpty(path)) return;
            _projectToolLauncher.OpenExplorer(path);
        }

        private void ProjectMenuItem_OpenAntigravity_Click(object sender, RoutedEventArgs e)
        {
            string path = GetPathFromMenuItem(sender);
            if (string.IsNullOrEmpty(path)) return;
            _projectToolLauncher.OpenAntigravity(path);
        }

        private void ProjectMenuItem_OpenCmd_Click(object sender, RoutedEventArgs e)
        {
            string path = GetPathFromMenuItem(sender);
            if (string.IsNullOrEmpty(path)) return;
            _projectToolLauncher.OpenCmd(path);
        }
        private void ProjectMenuItem_OpenGitTerminal_Click(object sender, RoutedEventArgs e)
        {
            string path = GetPathFromMenuItem(sender);
            if (string.IsNullOrEmpty(path)) return;
            _projectToolLauncher.OpenGitTerminal(path);
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

        public async void ShowCreateRepositoryDialog(string? initialPath = null)
        {
            try
            {
                var defaultBranch = await _gitRepository.GetDefaultBranchAsync();
                var (confirmed, projectPath, branch, remoteUrl, createReadme, createGitIgnore) =
                    await DialogService.ShowCreateRepositoryDialog(initialPath, defaultBranch);

                if (!confirmed || string.IsNullOrWhiteSpace(projectPath)) return;

                await RunWithLoading(async () =>
                {
                    var initUseCase = App.ServiceProvider.GetRequiredService<Chapi.Application.UseCases.Projects.InitRepositoryUseCase>();
                    var request = new Chapi.Application.UseCases.Projects.InitRepositoryRequest(
                        projectPath,
                        branch,
                        remoteUrl,
                        createReadme,
                        createGitIgnore
                    );

                    var result = await initUseCase.ExecuteAsync(request, progress => Msg.Assistant(progress));
                    if (result.IsSuccess)
                    {
                        Msg.Assistant($"Repositorio '{new DirectoryInfo(projectPath).Name}' inicializado exitosamente en rama '{branch}'.");
                        LoadProjects();
                        SwitchToProject(result.Data);
                        Chapi.Infrastructure.Persistence.Rollbacks.RollbackManager.ClearAllRollbacks();
                    }
                    else
                    {
                        await DialogService.ShowConfirmDialog("Error", $"No se pudo inicializar el repositorio: {result.Error}", DialogVariant.Error, DialogType.Info);
                    }
                });
            }
            catch (Exception ex)
            {
                Msg.Assistant($"Error: {ex.Message}");
            }
        }

        private async void ShowCloneDialog()
        {
            try
            {
                var viewModel = App.ServiceProvider.GetRequiredService<CloneRepositoryViewModel>();
                var dialog = new Chapi.Presentation.Shared.Dialogs.Views.CloneRepositoryDialog { DataContext = viewModel };

                var result = await DialogService.ShowDialog(dialog);

                if (result is CloneRepositoryViewModel vm)
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

            var selectedPath = folderDialog.SelectedPath;
            if (string.IsNullOrWhiteSpace(selectedPath)) return;

            // Verificar si el directorio seleccionado ya es un repositorio Git
            bool isGit = await _gitRepository.IsGitRepositoryAsync(selectedPath);
            if (!isGit)
            {
                var folderName = new DirectoryInfo(selectedPath).Name;
                var initConfirm = await DialogService.ShowConfirmDialog(
                    "Repositorio no inicializado",
                    $"La carpeta '{folderName}' no es un repositorio Git.\n\n¿Deseas inicializar un nuevo repositorio Git en esta ubicación?",
                    DialogVariant.Info,
                    DialogType.Confirm,
                    confirmButtonText: "INICIALIZAR",
                    cancelButtonText: "CANCELAR");

                if (initConfirm)
                {
                    ShowCreateRepositoryDialog(selectedPath);
                }
                return;
            }

            await RunWithLoading(async () =>
            {
                projectDirectory = selectedPath;
                ProjectSettings.AddProject(projectDirectory);
                LoadProjects();
                ProjectsComboBox.SelectedItem = ProjectsComboBox.Items.OfType<ProjectViewModel>().FirstOrDefault(p => p.FullPath == projectDirectory);
                Chapi.Infrastructure.Persistence.Rollbacks.RollbackManager.ClearAllRollbacks();
            });
        }

        private async Task DoFetchAndRefreshAsync(bool isSilent = false)
        {
            await _projectSyncCoordinator.FetchAndRefreshAsync(CreateProjectSyncContext(), isSilent);
        }

        public async Task UpdateProjectStatusesAsync(List<ProjectViewModel>? projects = null)
        {
            await _projectSyncCoordinator.UpdateProjectStatusesAsync(CreateProjectSyncContext(), projects);
            UpdateGitActionButton();
        }

        private async Task DelayedUpdateProjectStatusesAsync(List<ProjectViewModel> projects)
        {
            await Task.Delay(1500);
            await UpdateProjectStatusesAsync(projects);
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

            var createItem = new MenuItem
            {
                Header = "Crear Nuevo Repositorio...",
                Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.FolderPlusOutline }
            };
            createItem.Click += (s, ev) => ShowCreateRepositoryDialog();

            var cloneItem = new MenuItem
            {
                Header = "Clonar Nuevo Repositorio...",
                Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.CloudDownloadOutline }
            };
            cloneItem.Click += (s, ev) => ShowCloneDialog();

            var addItem = new MenuItem
            {
                Header = "Agregar Repositorio Existente...",
                Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.FolderAdd }
            };
            addItem.Click += (s, ev) => SelectProject();

            contextMenu.Items.Add(createItem);
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

            var am = new Chapi.Presentation.Features.Agent.Views.AddMethodView(projectDirectory);
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

            var rollbackView = new Chapi.Presentation.Features.Agent.Views.RollbackSelectorView();
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

            _changesViewModel?.SetLiveRefreshEnabled(GitTabs.SelectedItem == ChangesTab);

            if (GitTabs.SelectedItem == ChangesTab && _changesViewModel != null)
            {
                await _changesViewModel.RefreshIfNecessaryAsync();
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

            var sqlView = new Chapi.Presentation.Features.Agent.Views.SqlGeneratorView();
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
            await _gitWorkflowCoordinator.CreateBranchAsync(CreateGitWorkflowContext(), sourceBranch);
        }

        private async void Branch_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.CommandParameter is not string branchName) return;
            if (!ValidateProject()) return;
            await _gitWorkflowCoordinator.DeleteBranchAsync(CreateGitWorkflowContext(), branchName);
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
            await _gitWorkflowCoordinator.ShowMergeDialogAsync(CreateGitWorkflowContext(), mergeType);
        }

        private async Task ExecuteGitMergeOperation(string mergeType, string targetBranch, bool autoDeleteBranch = false)
        {
            await _gitWorkflowCoordinator.ExecuteMergeOperationAsync(
                CreateGitWorkflowContext(),
                mergeType,
                targetBranch,
                autoDeleteBranch);
        }





        private async void btnReloadChanges_Click(object sender, RoutedEventArgs e)
        {
            await LoadChangesAsync();
        }

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
            await _gitWorkflowCoordinator.ExecuteGitActionAsync(CreateGitWorkflowContext(), action);
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
            await _gitWorkflowCoordinator.HandleMergeConflictsAsync(CreateGitWorkflowContext());
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
                        await _changesViewModel.RefreshIfNecessaryAsync();
                    }
                }
            }
            else if (GitTabs.SelectedItem == ChangesTab && _changesViewModel != null)
            {
                await _changesViewModel.RefreshIfNecessaryAsync();
            }
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









