using AI.Clients;
using Chapi.Helper.AI;
using Chapi.Helper.Entities;
using Chapi.Helper.GitHelper;
using Chapi.Helper.Roslyn;
using Chapi.Helper.UserSettings;
using Chapi.Model;
using Chapi.Services;
using Chapi.Views;
using Chapi.Views.Dialogs;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using MaterialDesignThemes.Wpf;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using Velopack;
using Velopack.Sources;

namespace Chapi
{


    public partial class MainWindow : Window
    {
        private bool _isWindowInitialized = false;
        private string projectDirectory;
        private List<string> createdPaths = new List<string>();
        private Git.StashEntry _currentlyViewedStash = null;

        private enum GitActionState { Fetch, Pull, Push }
        private GitActionState _currentGitAction = GitActionState.Fetch;
        private Git.AheadBehindResult _currentGitStatus = new(0, 0);

        private string _currentlySelectedBranch;
        private string repoUrl = App.Configuration["AppConfig:UrlGit"] ?? throw new Exception("No se encontro Url Git");
        private string updateUrl = App.Configuration["AppConfig:UpdateUrl"] ?? throw new Exception("No se encontro Url Updater");
        public static MainWindow Instance { get; private set; }

        private List<string> _repositories = new List<string>();
        private string _activeDiffFile;
        private int? _activeDiffLine;

        private FileSystemWatcher _fileWatcher;
        private System.Threading.Timer _debounceTimer;
        private readonly object _lock = new object();
        private bool _isReloadingChanges = false;

        private bool _isGitInstalled = false;
        public MainWindow()
        {
            InitializeComponent();
            Instance = this;
            DataContext = MessageHelper.Instance;

            // Hook para hacer scroll automático cuando se agregue un nuevo mensaje
            MessageHelper.Instance.ScrollRequested += (s, e) =>
            {
                ChatScrollViewer?.ScrollToEnd();
            };
            Msg.Assistant("👋 ¡Hey! Soy Chapi 🤖 Tu dev buddy para arquitectura. Estoy listo para ayudarte hoy 🚀");


            _debounceTimer = new System.Threading.Timer(
                OnDebounceTimerElapsed,
                null,
                Timeout.Infinite,
                Timeout.Infinite);
            Task.Run(CheckForUpdates);
        }
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _isWindowInitialized = true;
            LoadProjects();
            await RunWithLoading(CheckGitInstallationAsync);
        }
        private async Task CheckForUpdates()
        {
            try
            {

                var mgr = new UpdateManager(new SimpleWebSource(updateUrl));
                var info = await mgr.CheckForUpdatesAsync();
                if (info == null)
                {
                    Msg.Assistant("✅ Chapi está actualizado.");
                    return;
                }

                Msg.Assistant($"Descargando actualización v{info.TargetFullRelease.Version}...");
                await mgr.DownloadUpdatesAsync(info);

                Msg.Assistant("✅ Actualización descargada. Chapi se reiniciará ahora.");
                mgr.ApplyUpdatesAndRestart(info);
            }
            catch (Exception ex)
            {
                Msg.Assistant($"⚠️ No se pudo comprobar actualizaciones: {ex.Message}");
            }
        }
        /// <summary>
        /// Se dispara al hacer clic en el logo principal.
        /// Abre la ventana de Servicios y Administración (UpdateView).
        /// </summary>
        private void LogoButton_Click(object sender, RoutedEventArgs e)
        {
            ShowUpdateView();
        }
        public void ShowUpdateView()
        {
            var updateView = new Chapi.Views.UpdateView(projectDirectory);
            updateView.Owner = this;
            updateView.ShowDialog();
        }
        private void LoadProjects()
        {
            _repositories = ProjectSettings.LoadProjects();

            var projectVMs = new List<ProjectViewModel>();

            foreach (var r in _repositories)
            {
                projectVMs.Add(new ProjectViewModel
                {
                    FullPath = r,
                    Name = new DirectoryInfo(r).Name,
                    Icon = PackIconKind.FolderOutline
                });
            }

            ProjectsComboBox.ItemsSource = projectVMs;
            if (App.TrayIconManager != null)
            {
                App.TrayIconManager.UpdateProjectList(projectVMs);
            }
        }
        /// <summary>
        /// Cambia el proyecto activo desde una llamada externa (como el TrayIcon).
        /// </summary>
        public void SwitchToProject(string projectPath)
        {
            // 1. Asegúrate de que la ventana esté visible
            if (!IsVisible) Show();
            Activate();

            // 2. Encuentra el proyecto en el ComboBox
            var projectToSelect = (ProjectsComboBox.ItemsSource as List<ProjectViewModel>)?.FirstOrDefault(p => p.FullPath == projectPath);
            // 3. Selecciónalo (esto disparará 'ProjectsComboBox_SelectionChanged'
            if (projectToSelect != null)
            {
                ProjectsComboBox.SelectedItem = projectToSelect;
            }
        }
        /// <summary>
        /// Este método se ejecuta cuando el timer de "debounce" (500ms) se completa.
        /// </summary>
        private void OnDebounceTimerElapsed(object state)
        {
            // Prevenir que se ejecute varias veces si ya está cargando
            lock (_lock)
            {
                if (_isReloadingChanges) return;
                _isReloadingChanges = true;
            }

            // Volver al hilo de la UI para tocar la lista de cambios
            Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await LoadChangesAsync();
                }
                finally
                {
                    lock (_lock)
                    {
                        _isReloadingChanges = false;
                    }
                }
            });
        }

        /// <summary>
        /// Se dispara CADA VEZ que un archivo cambia.
        /// Su único trabajo es resetear el timer de "debounce".
        /// </summary>
        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            // Si el cambio está dentro de la carpeta .git, ignorarlo.
            if (projectDirectory == null || e.FullPath.Contains(Path.Combine(projectDirectory, ".git")))
            {
                return;
            }

            // Reinicia el timer para que espere 500ms MÁS.
            _debounceTimer?.Change(500, Timeout.Infinite);
        }

        /// <summary>
        /// Inicializa el FileSystemWatcher para el proyecto actual.
        /// </summary>
        private void InitializeFileSystemWatcher(string path)
        {
            // 1. Limpia el watcher anterior (si existe)
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Dispose();
            }

            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                return;
            }

            // 2. Crea uno nuevo para la ruta del proyecto
            _fileWatcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                // Filtros sensibles
                NotifyFilter = NotifyFilters.LastWrite
                               | NotifyFilters.FileName
                               | NotifyFilters.DirectoryName
                               | NotifyFilters.CreationTime
                               | NotifyFilters.Size,
                InternalBufferSize = 65536 // 64KB para evitar desbordes
            };

            // 3. Conecta los eventos al handler que inicia el timer
            _fileWatcher.Changed += OnFileSystemChanged;
            _fileWatcher.Created += OnFileSystemChanged;
            _fileWatcher.Deleted += OnFileSystemChanged;
            _fileWatcher.Renamed += (s, e) => OnFileSystemChanged(s, e);

            // 4. ¡Encenderlo!
            _fileWatcher.EnableRaisingEvents = true;
        }
        private async void ProjectsComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ProjectsComboBox.SelectedItem == null) return;

            var selectedProject = ProjectsComboBox.SelectedItem as ProjectViewModel;
            if (selectedProject == null) return;
            projectDirectory = selectedProject.FullPath;
            InitializeFileSystemWatcher(projectDirectory);
            if (!_isGitInstalled)
            { var msg = "Seleccionaste un proyecto, pero Git sigue sin detectarse\nChapi necesita Git para rastrear cambios, ver el historial y gestionar commits. Parece que no está instalado o no se agregó al PATH del sistema.";
                Msg.Assistant(msg);
                await DialogService.ShowConfirmDialog("Alerta", msg, DialogVariant.Warning, DialogType.Info);
                return; 
            }
            string projectName = new DirectoryInfo(projectDirectory).Name;
            App.TrayIconManager.UpdateProjectMenuItem(projectName, false);
            await RunWithLoading(async () =>
            {
                if (projectDirectory != null)
                {
                    var branches = Git.GetBranches(projectDirectory);
                    BranchesComboBox.ItemsSource = branches;

                    if (branches.Any())
                    {
                        string activeBranch = await Git.GetCurrentBranch(projectDirectory);

                        if (!string.IsNullOrEmpty(activeBranch) && branches.Contains(activeBranch))
                        {
                            _currentlySelectedBranch = activeBranch;
                            BranchesComboBox.SelectedItem = activeBranch;
                        }
                        else
                        {
                            var defaultBranch = branches.FirstOrDefault(b => b.Contains("master") || b.Contains("main")) ?? branches.First();
                            _currentlySelectedBranch = defaultBranch;
                            BranchesComboBox.SelectedItem = defaultBranch;
                        }
                    }

                    await LoadChangesAsync();
                    await LoadHistoryAsync();
                    await LoadTagsAsync();
                }
            });
        }

        private async void BranchesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BranchesComboBox.SelectedItem == null) return;

            string newBranch = BranchesComboBox.SelectedItem.ToString();
            UpdateChangesCount();
            if (newBranch == _currentlySelectedBranch)
            {
                await LoadChangesAsync();
                await LoadHistoryAsync();
                return;
            }
            var statusOutput = await Git.EjecutarGit("status --porcelain", projectDirectory);

            if (!string.IsNullOrWhiteSpace(statusOutput))
            {
                // ¡Hay cambios sin "commitear"!
                var stashResult = await DialogService.ShowConfirmDialog(
                    "Cambios sin guardar",
                    "Tienes cambios sin guardar. ¿Quieres guardarlos en el stash antes de cambiar de rama?",
                    DialogVariant.Warning,
                    DialogType.Confirm
                );

                if (!stashResult) // Si el usuario presiona "Cancelar"
                {
                    // Revertimos la selección del ComboBox a la rama anterior
                    BranchesComboBox.SelectedItem = _currentlySelectedBranch;
                    return;
                }

                // Si el usuario presionó "Guardar (Stash)"
                await RunWithLoading(async () =>
                {
                    await Git.EjecutarGit("stash save \"[Chapi] Stash automático por cambio de rama\"", projectDirectory);
                    Msg.Assistant("✅ Cambios guardados en el stash.");
                });
            }

            await RunWithLoading(async () =>
            {
                Msg.Assistant($"Cambiando a la rama {newBranch}...");
                var checkoutResult = await Git.EjecutarGit($"checkout {newBranch}", projectDirectory);

                if (checkoutResult.Contains("error:") || checkoutResult.Contains("fatal:"))
                {
                    Msg.Assistant($"❌ Error al cambiar de rama: {checkoutResult}");
                    await DialogService.ShowConfirmDialog("Error", $"No se pudo cambiar de rama:\n{checkoutResult}", DialogVariant.Error, DialogType.Info);
                    // Revertimos la selección
                    BranchesComboBox.SelectedItem = _currentlySelectedBranch;
                }
                else
                {
                    _currentlySelectedBranch = newBranch;
                    Msg.Assistant($"✅ Estás en la rama {newBranch}.");
                }
            });

            await LoadChangesAsync();
            await LoadHistoryAsync();

        }

        private async Task LoadChangesAsync()
        {
            if (!ValidateProject())
            {
                ChangesListView.ItemsSource = null;

                StashExpander.Visibility = Visibility.Collapsed;
                return;
            }
            await UpdateBranchIndicatorsAsync();
            try
            {
                var stashes = await Git.ListStashes(projectDirectory);

                if (stashes.Any())
                {
                    StashListView.ItemsSource = stashes; // Llenar la lista
                    StashExpander.Header = $"📦 Stashed Changes ({stashes.Count})"; // Actualizar contador
                    StashExpander.Visibility = Visibility.Visible;
                }
                else
                {
                    StashListView.ItemsSource = null;
                    StashExpander.Header = "📦 Stashed Changes (0)";
                    StashExpander.Visibility = Visibility.Collapsed; // Ocultar si no hay stashes
                }
            }
            catch (Exception ex)
            {

                StashExpander.Visibility = Visibility.Collapsed;
                Msg.Assistant($"⚠️ Error al comprobar stashes: {ex.Message}");
            }


            var statusOutput = await Git.EjecutarGit("status --porcelain -uall", projectDirectory);
            var changes = new List<GitStatusItem>();

            if (string.IsNullOrWhiteSpace(statusOutput))
            {
                ChangesListView.ItemsSource = changes; // Lista vacía
                UpdateChangesCount();
                return;
            }

            var lines = statusOutput
              .Split('\n', StringSplitOptions.RemoveEmptyEntries)
              .Select(l => l.TrimEnd('\r'))
              .ToList();

            var regex = new Regex(@"^(?<status>[A-Z\?]{1,2})\s+(?<file>.+)$");
            foreach (var line in lines)
            {
                var match = regex.Match(line.Trim());
                if (match.Success)
                {

                    var status = match.Groups["status"].Value.Trim();
                    var filePath = match.Groups["file"].Value.Trim().Replace('/', Path.DirectorySeparatorChar).Trim('"');

                    var item = new GitStatusItem { FilePath = filePath };

                    switch (status.Trim()) // Tu lógica de switch está bien
                    {
                        case "M":
                            item.Status = "Modificado";
                            item.Icon = PackIconKind.FileEdit;
                            item.Color = Brushes.Orange;
                            break;
                        case "A":
                            item.Status = "Añadido";
                            item.Icon = PackIconKind.FilePlus;
                            item.Color = Brushes.Green;
                            break;
                        case "D":
                            item.Status = "Eliminado";
                            item.Icon = PackIconKind.FileRemove;
                            item.Color = Brushes.Red;
                            break;
                        case "R":
                            item.Status = "Renombrado";
                            item.Icon = PackIconKind.FileMove;
                            item.Color = Brushes.Blue;
                            break;
                        case "??":
                            item.Status = "Sin seguimiento";
                            item.Icon = PackIconKind.FileQuestion;
                            item.Color = Brushes.Green;
                            break;
                        case "UU":
                            item.Status = "Conflicto";
                            item.Icon = PackIconKind.AlertOctagon;
                            item.Color = Brushes.Red;
                            break;
                        case "AU":
                            item.Status = "Conflicto (Añadido por ti)";
                            item.Icon = PackIconKind.Alert;
                            item.Color = Brushes.Red;
                            break;
                        case "UA":
                            item.Status = "Conflicto (Añadido por ellos)";
                            item.Icon = PackIconKind.Alert;
                            item.Color = Brushes.Red;
                            break;
                        default:
                            item.Status = "Desconocido";
                            item.Icon = PackIconKind.FileQuestion;
                            item.Color = Brushes.Gray;
                            break;
                    }
                    changes.Add(item);
                }
            }
            var sortedChanges = changes.OrderBy(c => c.FilePath).ToList();
            ChangesListView.ItemsSource = sortedChanges;
            SelectAllCheckBox.IsChecked = sortedChanges.Any() && sortedChanges.All(c => c.IsSelected);
            UpdateChangesCount();
        }
        private async Task LoadHistoryAsync()
        {
            if (!ValidateProject())
            {
                HistoryListView.ItemsSource = null;
                return;
            }
            await UpdateBranchIndicatorsAsync();
            string currentBranch = BranchesComboBox.SelectedItem as string;
            HashSet<string> unpushedHashes = new HashSet<string>();

            if (!string.IsNullOrEmpty(currentBranch))
            {
                // Obtenemos la lista de commits que no están en el remoto
                unpushedHashes = await Git.GetUnpushedCommitHashes(currentBranch, projectDirectory);
            }

            var tagMap = await Git.GetTagCommitMap(projectDirectory);

            const string fieldSeparator = "\x1f";
            const string recordSeparator = "\x1e";

            string logFormat = $"%h{fieldSeparator}%an{fieldSeparator}%ar{fieldSeparator}%s{fieldSeparator}%b{recordSeparator}";
            var logOutput = await Git.EjecutarGit($"log --pretty=format:\"{logFormat}\" -n 50", projectDirectory);
            var commits = new List<GitLogItem>();

            if (string.IsNullOrWhiteSpace(logOutput))
            {
                HistoryListView.ItemsSource = commits; // Lista vacía
                return;
            }
            var commitRecords = logOutput.Split(new[] { recordSeparator }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in commitRecords)
            {
                var parts = line.Trim().Trim('"').Split(new[] { fieldSeparator }, StringSplitOptions.None);
                if (parts.Length == 5)
                {
                    var hash = parts[0];
                    var commit = new GitLogItem
                    {
                        Hash = hash,
                        Author = parts[1],
                        Date = parts[2],
                        Message = parts[3],
                        Description = parts[4].Trim(),
                        IsUnpushed = unpushedHashes.Contains(hash)
                    };
                    var tagEntry = tagMap.Keys.FirstOrDefault(k => k.StartsWith(hash));
                    if (tagEntry != null)
                    {
                        commit.Tags = tagMap[tagEntry];
                    }
                    commits.Add(commit);
                }
            }

            HistoryListView.ItemsSource = commits;
        }

        #region ✅ UI Helpers (Loading + DialogHost)
        public void ShowLoading() => LoadingOverlay.Visibility = Visibility.Visible;
        public void HideLoading() => LoadingOverlay.Visibility = Visibility.Collapsed;

        public async Task<T> RunWithLoading<T>(Func<Task<T>> action)
        {
            try
            {
                ShowLoading();
                return await action();
            }
            finally
            {
                HideLoading();
            }
        }

        public async Task RunWithLoading(Func<Task> action)
        {
            try
            {
                ShowLoading();
                await action();
            }
            finally
            {
                HideLoading();
            }
        }
        #endregion
        #region ✅ Proyecto Base
        public async void CreateNewTemplate()
        {

            if (!IsVisible) Show();

            var (ok, projectName) = await DialogService.ShowInputDialog("Nuevo Proyecto", "Ingrese nombre del proyecto");
            if (!ok || string.IsNullOrWhiteSpace(projectName))
                return;

            var folderDialog = new FolderBrowserDialog();
            if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            projectDirectory = folderDialog.SelectedPath;
            DialogService.ShowTrayNotification("Nuevo Proyecto", $"Se creará: {projectName}\nDestino: {projectDirectory}");


            string rutaProyecto = Path.Combine(projectDirectory, projectName);



            await RunWithLoading(async () =>
            {
                Msg.Assistant("Clonando repositorio base...");
                await Git.CloneRepo(repoUrl, rutaProyecto);

                Msg.Assistant("Eliminando .git...");
                Git.DeleteGitFolder(rutaProyecto);

                Msg.Assistant("Renombrando estructura...");
                string oldName = Path.GetFileNameWithoutExtension(repoUrl);
                RenameDirectoryAndFiles.RenombrarRecursivamente(rutaProyecto, oldName, projectName);

                projectDirectory = rutaProyecto;

                Msg.Assistant("Inicializando nuevo repo Git...");
                await Git.InitGit(rutaProyecto);

                var asociar = await DialogService.ShowConfirmDialog("¿Deseas asociar un repositorio remoto ahora?", "Asociar Git");
                if (asociar)
                {
                    var (assocOk, urlGit) = await DialogService.ShowInputDialog("Repositorio Git", "Ingrese la URL del repositorio remoto:");
                    if (assocOk && !string.IsNullOrWhiteSpace(urlGit))
                    {
                        Msg.Assistant("Asociando repositorio remoto...");
                        await Git.EjecutarGit($"remote add origin {urlGit}", rutaProyecto);
                        Msg.Assistant("Repositorio remoto asociado correctamente.");
                    }
                    else
                    {
                        Msg.Assistant("URL vacía u operación cancelada.");
                    }
                }
                else
                {
                    Msg.Assistant("Asociación omitida por el usuario.");
                }

                ProjectSettings.AddProject(rutaProyecto);
                LoadProjects();
                ProjectsComboBox.SelectedItem = new DirectoryInfo(rutaProyecto).Name;

                App.TrayIconManager.UpdateProjectMenuItem($"{Path.GetFileName(rutaProyecto)}", true);
                FileHelper.DeleteRollbackFiles();
                Msg.Assistant("Proyecto creado exitosamente en: " + rutaProyecto);

            });
        }
        #endregion
        #region ✅ Selección de Proyecto Existente
        public async void SelectProjectMenu_Click(object sender, RoutedEventArgs e)
        {
            using var folderDialog = new FolderBrowserDialog
            {
                Description = "Dale sin Miedo al éxito",
                ShowNewFolderButton = false
            };
            using var owner = new Form { TopMost = true, StartPosition = FormStartPosition.CenterScreen };
            if (folderDialog.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK)
                return;
            await RunWithLoading(async () =>
            {
                projectDirectory = folderDialog.SelectedPath;
                ProjectSettings.AddProject(projectDirectory);
                LoadProjects();
                ProjectsComboBox.SelectedItem = new DirectoryInfo(projectDirectory).Name;

                string projectName = Path.GetFileName(projectDirectory);
                DialogService.ShowTrayNotification("Proyecto Existente", $"Seleccionado: {projectDirectory}");
                App.TrayIconManager.UpdateProjectMenuItem(projectName, false);
                FileHelper.DeleteRollbackFiles();
                await Task.Delay(100);
            });
        }
        #endregion
        #region ✅ Git - Asociar y Commit Asistido
        public async void AsociateGitMenu_Click()
        {
            if (!ValidateProject()) return;

            var asociar = await DialogService.ShowConfirmDialog("¿Deseas asociar un repositorio remoto ahora?", "Asociar Git");
            if (!asociar)
            {
                Msg.Assistant("Asociación omitida por el usuario.");
                return;
            }

            var (ok, urlGit) = await DialogService.ShowInputDialog("Repositorio Git", "Ingrese la URL del repositorio remoto:");
            if (ok && !string.IsNullOrWhiteSpace(urlGit))
            {
                Msg.Assistant("Asociando repositorio remoto...");
                await Git.EjecutarGit($"remote add origin {urlGit}", projectDirectory);
                Msg.Assistant("Repositorio remoto asociado correctamente.");
            }
            else
            {
                Msg.Assistant("URL vacía o cancelado.");
            }
        }
        public async void GitCommitAsistance()
        {
            if (!ValidateProject()) return;
            Msg.User("Genera Commit");
            await RunWithLoading(async () =>
            {
                var head = await Git.EjecutarGit("rev-parse --quiet --verify HEAD", projectDirectory);
                if (string.IsNullOrWhiteSpace(head))
                {
                    await Git.EjecutarGit("add .", projectDirectory);
                    await Git.EjecutarGit("commit --allow-empty -m \"first commit\"", projectDirectory);
                    await DialogService.ShowConfirmDialog("Listo", "🚀 Primer commit creado.", DialogVariant.Success, DialogType.Info);
                    return;
                }

                await Git.EjecutarGit("add .", projectDirectory);
                string diff = await Git.EjecutarGit("diff --cached", projectDirectory);
                if (string.IsNullOrWhiteSpace(diff))
                {
                    await DialogService.ShowConfirmDialog("Alerta", "No hay cambios para commitear.", DialogVariant.Warning, DialogType.Info);
                    return;
                }
                var prompt = GetPrompt.GitCommit(diff);
                string commitMsg = await AIClient.SendPromptAsync(prompt);
                if (string.IsNullOrWhiteSpace(commitMsg)) return;

                var (confirm, msg) = await DialogService.ShowInputDialog("¿Desea realizar el commit?", "Mensaje generado por IA:", commitMsg);
                if (!confirm)
                {
                    Msg.Assistant("Commit cancelado por el usuario.");
                    return;
                }

                await Git.EjecutarGit($"commit -m  \"{msg}\"", projectDirectory);
                var response = await DialogService.ShowConfirmDialog(
                     "Confirmación",
                     "Commit realizado exitosamente.\n¿Desea subir los cambios al repositorio?",
                     DialogVariant.Success,
                     DialogType.Confirm
                 );
                Msg.Assistant("Commit realizado exitosamente.");

                if (response)
                {
                    var result = await Git.EjecutarGit("push", projectDirectory);

                    if (string.IsNullOrWhiteSpace(result))
                    {
                        await DialogService.ShowConfirmDialog(
                            "Advertencia",
                            "No se recibió respuesta del comando Git Push.",
                            DialogVariant.Warning,
                            DialogType.Info
                        );
                    }
                    else if (result.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                             result.Contains("fatal", StringComparison.OrdinalIgnoreCase) ||
                             result.Contains("rejected", StringComparison.OrdinalIgnoreCase))
                    {
                        await DialogService.ShowConfirmDialog(
                            "Error al subir",
                            $"El push no se realizó correctamente:\n\n{result}",
                            DialogVariant.Error,
                            DialogType.Info
                        );
                    }
                    else if (result.Contains("To ") && result.Contains("->"))
                    {
                        await DialogService.ShowConfirmDialog(
                            "Éxito",
                            $"Los cambios se subieron correctamente al repositorio.\n\n{result}",
                            DialogVariant.Success,
                            DialogType.Info
                        );
                        Msg.Assistant($"Los cambios se subieron correctamente al repositorio.\n\n{result}");
                    }
                    else
                    {
                        await DialogService.ShowConfirmDialog(
                            "Resultado del Push",
                            $"Git devolvió la siguiente respuesta:\n\n{result}",
                            DialogVariant.Info,
                            DialogType.Info
                        );
                    }
                }
            });
        }

        #endregion

        #region ✅ Generación de Módulos y Métodos
        public async void GenerateModuleMenu_Click()
        {
            if (!IsVisible) Show();
            if (!ValidateProject()) return;

            var (result, inputModules) = await DialogService.ShowInputDialog("Crear Módulo", "Ingrese los nombres de los módulos separados por ';':");
            if (!result || string.IsNullOrWhiteSpace(inputModules)) return;

            var modules = inputModules.Split(';').Select(m => m.Trim()).Where(m => m.Length > 0).ToArray();
            var (dbOk, dbChoice) = await DialogService.ShowInputDialog("Seleccionar Base de Datos", "Ingrese 'S' para Sybase o 'P' para Postgres:");
            if (!dbOk || string.IsNullOrWhiteSpace(dbChoice)) return;

            string dbName = dbChoice.ToUpper() == "S" ? "Sybase" : "Postgres";

            foreach (var module in modules)
                await RunWithLoading(() => GenerateModule(module, dbName));
        }
        private async Task GenerateModule(string moduleName, string dbName)
        {
            moduleName = char.ToUpper(moduleName[0]) + moduleName[1..];
            Msg.Assistant($"Generando módulo: {moduleName}");


            string basePath = projectDirectory;
            string apiProjectPath = FindApiDirectory.GetDirectory(basePath);

            if (apiProjectPath == null)
            {
                DialogService.ShowTrayNotification("Error", "No se pudo detectar el proyecto API.");
                return;
            }

            string apiPath = Path.Combine(basePath, Path.GetFileName(apiProjectPath), "Controllers", moduleName);
            string appPath = Path.Combine(basePath, "Application", moduleName);
            string domainPath = Path.Combine(basePath, "Domain", moduleName);
            string infraPath = Path.Combine(basePath, "Infrastructure", dbName, "Repositories", moduleName);

            Msg.Assistant("Creando carpetas...");
            Directory.CreateDirectory(apiPath);
            Directory.CreateDirectory(appPath);
            Directory.CreateDirectory(domainPath);
            Directory.CreateDirectory(infraPath);

            createdPaths.AddRange(new[] { apiPath, appPath, domainPath, infraPath });
            Msg.Assistant("Carpetas creadas.");

            // ✅ OPERACIONES POR DEFECTO DEL MÓDULO
            var defaultOperations = new[] { "Get", "Post", "GetById" };

            Msg.Assistant("Generando clases base con Roslyn...");

            foreach (var operation in defaultOperations)
            {
                var rollbackEntry = RollbackManager.StartTransaction(moduleName, moduleName, operation);
                try
                {
                    // API Controller
                    AddApiControllerMethod.Add(apiPath, moduleName, operation, moduleName, rollbackEntry);

                    // Application Layer
                    AddApplicationMethod.Add(appPath, moduleName, operation, moduleName, rollbackEntry);

                    // Domain Layer
                    await AddDomainMethod.Add(domainPath, moduleName, operation, moduleName, rollbackEntry);

                    // Infrastructure Layer
                    await AddInfrastructureMethod.Add(infraPath, moduleName, dbName, operation, moduleName, rollbackEntry);


                    Msg.Assistant("Inyectando Servicios...");

                    #region Servicios Injection
                    string dependencyInjectionPath = Path.Combine(basePath, Path.GetFileName(apiProjectPath), "Config", "DependencyInjection.cs");

                    // ✅ USAR EL MÉTODO ROSLYN PARA DEPENDENCY INJECTION
                    var diContent = File.ReadAllText(dependencyInjectionPath);
                    RollbackManager.RecordFileModification(rollbackEntry, dependencyInjectionPath, diContent);

                    AddDependencyInjection.Add(dependencyInjectionPath, moduleName, defaultOperations);
                    // 💾 GUARDAR ROLLBACK
                    RollbackManager.CommitTransaction(rollbackEntry);
                }
                catch (Exception ex)
                {
                    Msg.Assistant($"❌ Error al agregar mudulo base {moduleName}: {ex.Message}");
                    Msg.Assistant($"🔄 Ejecutando rollback automático...");

                    // Si algo falla, hacer rollback automático de los cambios parciales
                    var tempPath = RollbackManager.GetRollbackFilePathForEntry(rollbackEntry);
                    RollbackManager.CommitTransaction(rollbackEntry); // Guardar para poder hacer rollback
                    RollbackManager.ExecuteRollback(tempPath);

                    throw new Exception($"Error al agregar mudulo: {ex.Message}\nSe ha realizado rollback de los cambios.");
                }
            }
            await DialogService.ShowConfirmDialog(
                "Confirmación",
                $"✅ Métodos Agregados Correctamente en Módulo {moduleName}",
                DialogVariant.Info,
                DialogType.Info
            );

            Msg.Assistant("✅ Servicios inyectados correctamente.");
            #endregion

            Msg.Assistant($"✅ Módulo '{moduleName}' generado correctamente.");

        }
        public async Task RollbackSelectModule()
        {
            if (!IsVisible) Show();

            var rollbacks = RollbackManager.GetAvailableRollbacks();

            if (!rollbacks.Any())
            {
                await DialogService.ShowConfirmDialog(
                    "Información",
                    "No hay rollbacks disponibles.",
                    Views.Dialogs.DialogVariant.Info,
                    Views.Dialogs.DialogType.Info
                );
                return;
            }

            // Abrir ventana de selección de rollback
            RollbackSelectorView rollbackView = new RollbackSelectorView();
            var result = rollbackView.ShowDialog();

            if (result == true)
            {
                Msg.Assistant("✅ Rollback ejecutado correctamente.");
            }
        }

        #endregion

        public async void AddMethod_Click()
        {
            if (!Directory.Exists(projectDirectory))
            {
                DialogService.ShowTrayNotification("Error", "Por favor selecciona un proyecto.");
                return;
            }
            if (!this.IsVisible)
            {
                this.Show();
            }
            AddMethodView am = new AddMethodView(projectDirectory);
            am.Owner = this;
            am.ShowDialog();
        }



        private void btnNuevo_Click(object sender, RoutedEventArgs e)
        {
            AddMethod_Click();
        }
        private void btnRecuve_Click(object sender, RoutedEventArgs e)
        {
            RollbackSelectModule();
        }


        public void AddClassLog_Click()
        {
            this.Show();
            this.Activate();
            GitTabs.SelectedItem = AssistantTab;
        }



        #region ✅ Utilidades y Validaciones
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private bool ValidateProject()
        {
            if (string.IsNullOrEmpty(projectDirectory) || !Directory.Exists(projectDirectory))
            {
                DialogService.ShowTrayNotification("Error", "Por favor selecciona un proyecto primero.");
                return false;
            }
            return true;
        }

        private async void CloneProject_Click(object sender, RoutedEventArgs e)
        {
            var (ok, repoUrl) = await DialogService.ShowInputDialog("Clonar Repositorio", "Ingrese la URL del repositorio:");
            if (!ok || string.IsNullOrWhiteSpace(repoUrl))
                return;

            var folderDialog = new FolderBrowserDialog();
            if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            string projectPath = Path.Combine(folderDialog.SelectedPath, Path.GetFileNameWithoutExtension(repoUrl));

            await RunWithLoading(async () =>
            {
                Msg.Assistant("Clonando repositorio...");
                var result = await Git.CloneRepo(repoUrl, projectPath);
                if (result.Success)
                {
                    ProjectSettings.AddProject(projectPath);
                    LoadProjects();
                    ProjectsComboBox.SelectedItem = new DirectoryInfo(projectPath).Name;
                    Msg.Assistant("Repositorio clonado exitosamente.");
                }
                else
                {
                    Msg.Assistant($"Error al clonar el repositorio: {result.Output}");
                }
            });
        }



        private async Task DoFetchAsync()
        {
            if (!ValidateProject()) return;

            await RunWithLoading(async () =>
            {
                Msg.Assistant("Realizando fetch de los cambios remotos...");
                var result = await Git.EjecutarGit("fetch", projectDirectory);

                if (result.Contains("error") || result.Contains("fatal"))
                {
                    Msg.Assistant($"❌ Error al realizar fetch: {result}");
                    await DialogService.ShowConfirmDialog("Error", $"No se pudo completar la operación de fetch.\n\n{result}", DialogVariant.Error, DialogType.Info);
                }
                else
                {
                    Msg.Assistant("✅ Fetch completado exitosamente.");

                    // 1. Obtener tus cambios locales (con rutas Git '/')
                    var statusOutput = await Git.EjecutarGit("status --porcelain -uall", projectDirectory);
                    var localChanges = new HashSet<string>();
                    if (!string.IsNullOrWhiteSpace(statusOutput))
                    {
                        var regex = new Regex(@"^(?<status>[A-Z\?]{1,2})\s+(?<file>.+)$");
                        var lines = statusOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            var match = regex.Match(line.Trim());
                            if (match.Success)
                            {
                                localChanges.Add(match.Groups["file"].Value.Trim().Trim('"'));
                            }
                        }
                    }

                    // 2. Obtener los archivos cambiados en el remoto (con rutas Git '/')
                    var remoteDiffOutput = await Git.EjecutarGit("diff --name-only HEAD...@{u}", projectDirectory);
                    var remoteChanges = new HashSet<string>();
                    if (!string.IsNullOrWhiteSpace(remoteDiffOutput) && !remoteDiffOutput.Contains("fatal:"))
                    {
                        remoteChanges = remoteDiffOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                                     .Select(f => f.Trim().Trim('"'))
                                                     .ToHashSet();
                    }

                    // 3. Encontrar la intersección (archivos modificados en AMBOS lados)
                    var conflictingFiles = localChanges.Intersect(remoteChanges).ToList();

                    // 4. ¡Mostrar la advertencia!
                    if (conflictingFiles.Any())
                    {
                        string fileList = string.Join("\n- ", conflictingFiles);
                        await DialogService.ShowConfirmDialog(
                            "Aviso de Conflicto Potencial",
                            $"Tu 'pull' puede fallar. Tienes cambios locales en archivos que también cambiaron en el remoto:\n\n- {fileList}\n\n" +
                            "Se recomienda hacer 'Stash' de tus cambios antes de hacer 'Pull'.",
                            DialogVariant.Warning, DialogType.Info);
                    }

                    // (El resto de tu código para actualizar ramas no cambia)
                    var branches = Git.GetBranches(projectDirectory);
                    var currentBranch = BranchesComboBox.SelectedItem as string;
                    BranchesComboBox.ItemsSource = branches;
                    if (!string.IsNullOrEmpty(currentBranch) && branches.Contains(currentBranch))
                    {
                        BranchesComboBox.SelectedItem = currentBranch;
                    }
                }
                await LoadChangesAsync();
                await LoadHistoryAsync(); // Esto actualizará los indicadores
            });
        }
        private async Task DoPushAsync()
        {
            if (!ValidateProject()) return;
            var originalIcon = DefaultGitActionIcon.Kind;
            var originalText = DefaultGitActionText.Text;
            GitActionsComboBox.IsEnabled = false;
            await RunWithLoading(async () =>
            {
                try
                {
                    Msg.Assistant("Subiendo cambios al repositorio remoto...");

                    // 2. Define la acción que se ejecutará con cada línea de progreso
                    Action<string> progressCallback = (line) =>
                    {
                        // ¡Importante! Debemos usar el Dispatcher para actualizar la UI
                        // desde un hilo de fondo.
                        Dispatcher.Invoke(() =>
                        {
                            // Busca la línea de progreso (ej: "Writing objects: 30%")
                            if (line.Contains("Writing objects:") || line.Contains("Compressing objects:"))
                            {
                                // Extrae el texto principal (ej: "Writing objects: 30%")
                                var progressText = line.Split(',').FirstOrDefault()?.Trim();

                                // Actualiza el botón para que actúe como un spinner
                                DefaultGitActionIcon.Kind = PackIconKind.Refresh;
                                DefaultGitActionText.Text = progressText ?? "Subiendo...";
                            }
                            else if (line.StartsWith("Pushing to"))
                            {
                                DefaultGitActionIcon.Kind = PackIconKind.Refresh;
                                DefaultGitActionText.Text = "Subiendo...";
                            }
                        });
                    };

                    // 3. Llama al nuevo método de Git
                    var result = await Git.EjecutarGitConProgreso("push", projectDirectory, progressCallback);

                    // 4. Analiza el resultado final (como hacías antes)
                    if (result.Contains("error") || result.Contains("fatal") || result.Contains("rejected"))
                    {
                        Msg.Assistant($"❌ Error al realizar push: {result}");
                        await DialogService.ShowConfirmDialog("Error", $"No se pudo completar la operación de push.\n\n{result}", DialogVariant.Error, DialogType.Info);
                    }
                    else if (result.Contains("Everything up-to-date"))
                    {
                        Msg.Assistant("✅ Todo está actualizado. No hay nada para subir.");
                    }
                    else
                    {
                        Msg.Assistant($"✅ Push completado exitosamente.\n{result}");
                    }
                }
                catch (Exception ex)
                {
                    Msg.Assistant($"❌ Error fatal durante el push: {ex.Message}");
                }
                finally
                {
                    // 5. Restaura el botón a su estado original
                    GitActionsComboBox.IsEnabled = true;

                    // Recargamos el estado (que pondrá el texto e icono correctos)
                    await LoadChangesAsync();
                    await LoadHistoryAsync();
                }
            });
        }
        private async Task DoPullAsync()
        {
            if (!ValidateProject()) return;

            await RunWithLoading(async () =>
            {
                Msg.Assistant("Realizando pull de los cambios remotos...");
                var result = await Git.EjecutarGit("pull", projectDirectory);
                if (result.Contains("Automatic merge failed") || result.Contains("CONFLICT"))
                {
                    Msg.Assistant("❌ ¡Conflicto! Se detectaron conflictos de merge.");
                    await DialogService.ShowConfirmDialog("Conflicto de Merge",
                        "No se pudo completar el pull automáticamente. Tienes conflictos:\n\n" + result,
                        DialogVariant.Error, DialogType.Info);
                }
                else if (result.Contains("error") || result.Contains("fatal"))
                {
                    Msg.Assistant($"❌ Error al realizar pull: {result}");
                    await DialogService.ShowConfirmDialog("Error", $"No se pudo completar la operación de pull.\n\n{result}", DialogVariant.Error, DialogType.Info);
                }
                else if (result.Contains("Already up to date"))
                {
                    Msg.Assistant("✅ El repositorio ya está actualizado.");
                }
                else
                {
                    Msg.Assistant("✅ Pull completado exitosamente.");
                }

                await LoadChangesAsync();
                await LoadHistoryAsync();
            });
        }
        #endregion

        private async void GitTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is System.Windows.Controls.TabControl)
            {
                if (GitTabs.SelectedItem is TabItem tabItem)
                {
                    string header = tabItem.Header.ToString();
                    if (header == "Cambios")
                    {
                        await LoadChangesAsync();
                    }
                    else if (header == "Historial")
                    {
                        await LoadHistoryAsync();
                    }
                    // --- NUEVO ---
                    else if (header == "Tags")
                    {
                        await LoadTagsAsync();
                    }
                }
            }
        }

        private async void ChangesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Limpiar el visor antes de empezar
            DiffLinesItemsControl.ItemsSource = null;

            var selectedItem = e.AddedItems.OfType<GitStatusItem>().FirstOrDefault();

            if (selectedItem == null)
            {
                return;
            }

            if (!ValidateProject())
            {
                return;
            }

            try
            {
                string oldText = await Git.GetFileContentAtCommitish(selectedItem.FilePath, "HEAD", projectDirectory);
                string fullPath = Path.Combine(projectDirectory, selectedItem.FilePath);
                string newText = string.Empty;

                if (File.Exists(fullPath) && selectedItem.Status != "Eliminado")
                {
                    newText = await File.ReadAllTextAsync(fullPath);
                }

                var diffBuilder = new InlineDiffBuilder(new DiffPlex.Differ());
                var diff = diffBuilder.BuildDiffModel(oldText, newText);

                // (Lógica de Hunks)
                var filteredLines = new List<DiffPiece>();
                const int contextLines = 3;

                for (int i = 0; i < diff.Lines.Count; i++)
                {
                    var line = diff.Lines[i];
                    if (line.Type == ChangeType.Unchanged)
                    {
                        bool isContext = false;
                        for (int j = 1; j <= contextLines; j++)
                        {
                            if (i - j >= 0 && diff.Lines[i - j].Type != ChangeType.Unchanged) { isContext = true; break; }
                        }
                        if (!isContext)
                        {
                            for (int j = 1; j <= contextLines; j++)
                            {
                                if (i + j < diff.Lines.Count && diff.Lines[i + j].Type != ChangeType.Unchanged) { isContext = true; break; }
                            }
                        }
                        if (isContext) { filteredLines.Add(line); }
                        else if (filteredLines.Count > 0 && filteredLines.Last().Type != ChangeType.Imaginary)
                        {
                            filteredLines.Add(new DiffPiece("...", ChangeType.Imaginary, null));
                        }
                    }
                    else { filteredLines.Add(line); }
                }

                DiffLinesItemsControl.ItemsSource = filteredLines;
            }
            catch (Exception ex)
            {
                Msg.Assistant($"--- !!! CATCH BLOCK ERROR: {ex.Message} ---");
                DiffLinesItemsControl.ItemsSource = new List<DiffPiece>
                {
                    new DiffPiece($"ERROR AL CARGAR DIFF: {ex.Message}", ChangeType.Deleted)
                };
            }
        }
        // --- NUEVO: Lógica para el botón de Commit Manual ---
        private async void btnCommit_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateProject()) return;
            var selectedItems = (ChangesListView.ItemsSource as List<GitStatusItem>)?.Where(i => i.IsSelected).ToList();
            if (selectedItems == null || !selectedItems.Any())
            {
                await DialogService.ShowConfirmDialog("Alerta", "No hay archivos seleccionados para el commit.", DialogVariant.Warning, DialogType.Info);
                return;
            }
            string summary = txtCommitSummary.Text.Trim();
            string description = txtCommitDescription.Text.Trim();

            if (string.IsNullOrWhiteSpace(summary))
            {
                await DialogService.ShowConfirmDialog("Alerta", "El resumen del commit no puede estar vacío.", DialogVariant.Warning, DialogType.Info);
                return;
            }

            string commitMessage = summary;
            if (!string.IsNullOrWhiteSpace(description))
            {
                commitMessage += $"\n\n{description}";
            }

            await RunWithLoading(async () =>
            {
                Msg.Assistant("Agregando archivos seleccionados al stage...");
                await Git.EjecutarGit("reset", projectDirectory);
                string filePaths = string.Join(" ", selectedItems.Select(i => $"\"{i.FilePath.Replace(Path.DirectorySeparatorChar, '/')}\""));
                await Git.EjecutarGit($"add -- {filePaths}", projectDirectory);

                Msg.Assistant("Realizando commit...");
                var commitResult = await Git.EjecutarGit($"commit -m \"{commitMessage.Replace("\"", "'")}\"", projectDirectory);

                if (commitResult.Contains("No changes added to commit") || commitResult.Contains("nothing to commit"))
                {
                    Msg.Assistant("No hay cambios para commitear.");
                    await DialogService.ShowConfirmDialog("Información", "No se encontraron cambios para registrar.", DialogVariant.Info, DialogType.Info);
                }
                else
                {
                    Msg.Assistant($"Commit realizado: {summary}");
                    txtCommitSummary.Text = "";
                    txtCommitDescription.Text = "";
                    await LoadChangesAsync();
                    await LoadHistoryAsync();
                }
            });
        }

        private async void btnGitCommit_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateProject()) return;
            Msg.User("Genera Commit con IA");

            await RunWithLoading(async () =>
            {
                // 1. Obtener archivos seleccionados
                var selectedItems = (ChangesListView.ItemsSource as List<GitStatusItem>)?.Where(i => i.IsSelected).ToList();
                if (selectedItems == null || !selectedItems.Any())
                {
                    await DialogService.ShowConfirmDialog("Alerta", "No hay archivos seleccionados para analizar.", DialogVariant.Warning, DialogType.Info);
                    return;
                }

                string filePaths = string.Join(" ", selectedItems.Select(i => $"\"{i.FilePath.Replace(Path.DirectorySeparatorChar, '/')}\""));

                string diff = await Git.EjecutarGit($"diff HEAD -- {filePaths}", projectDirectory);


                if (string.IsNullOrWhiteSpace(diff))
                {
                    await DialogService.ShowConfirmDialog("Alerta", "No se encontraron cambios en los archivos seleccionados.", DialogVariant.Warning, DialogType.Info);
                    return;
                }

                var prompt = GetPrompt.GitCommit(diff);
                string jsonResponse = await AIClient.SendPromptAsync(prompt);
                if (string.IsNullOrWhiteSpace(jsonResponse))
                {
                   
                    Msg.Assistant("La IA no pudo generar un mensaje.");
                    return;
                }

                try
                {
                    // La IA a veces devuelve JSON inválido, lo envolvemos en un try-catch
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var commitMsg = JsonSerializer.Deserialize<CommitMessageResponse>(jsonResponse, options);

                    if (commitMsg != null)
                    {
                        txtCommitSummary.Text = commitMsg.Summary;
                        txtCommitDescription.Text = commitMsg.Description;
                        Msg.Assistant("Mensaje de commit y descripción generados por IA.");
                    }
                    else
                    {
                        txtCommitSummary.Text = jsonResponse;
                        txtCommitDescription.Text = "";
                    }
                }
                catch (Exception ex)
                {
                    Msg.Assistant($"Error al procesar respuesta de IA. Se usará respuesta en crudo: {ex.Message}");
                    txtCommitSummary.Text = jsonResponse;
                    txtCommitDescription.Text = "";
                }
            });
        }

        private async Task LoadTagsAsync()
        {
            if (!ValidateProject())
            {
                TagsListView.ItemsSource = null;
                return;
            }

            var tags = await Git.GetTags(projectDirectory);
            TagsListView.ItemsSource = tags;
        }

        private async void btnCrearTag_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateProject()) return;

            // 1. Pedir nombre del Tag
            var (okTag, tagName) = await DialogService.ShowInputDialog("Crear Tag", "Ingrese el nombre del tag (ej: v1.0.0):");
            if (!okTag || string.IsNullOrWhiteSpace(tagName)) return;

            // 2. Pedir mensaje del Tag
            var (okMsg, tagMessage) = await DialogService.ShowInputDialog("Crear Tag", "Ingrese un mensaje para el tag (anotación):", $"Release {tagName}");
            if (!okMsg || string.IsNullOrWhiteSpace(tagMessage)) return;

            await RunWithLoading(async () =>
            {
                Msg.Assistant($"Creando tag {tagName}...");
                var result = await Git.CreateTag(tagName, tagMessage, projectDirectory);

                if (!result.Success)
                {
                    Msg.Assistant($"Error al crear tag: {result.Output}");
                    await DialogService.ShowConfirmDialog("Error", $"No se pudo crear el tag:\n{result.Output}", DialogVariant.Error, DialogType.Info);
                    return;
                }

                Msg.Assistant($"Tag {tagName} creado localmente.");
                await LoadTagsAsync(); // Recargar lista de tags

                // 3. Preguntar si desea subirlo
                var push = await DialogService.ShowConfirmDialog("Tag Creado",
                    $"El tag '{tagName}' se creó localmente.\n\n¿Desea subir (push) este tag al repositorio remoto (origin) ahora?",
                    DialogVariant.Info, DialogType.Confirm);

                if (push)
                {
                    Msg.Assistant($"Subiendo tag {tagName}...");
                    var pushResult = await Git.PushTag(tagName, projectDirectory);

                    if (!pushResult.Success)
                    {
                        Msg.Assistant($"Error al subir tag: {pushResult.Output}");
                        await DialogService.ShowConfirmDialog("Error", $"No se pudo subir el tag:\n{pushResult.Output}", DialogVariant.Error, DialogType.Info);
                    }
                    else
                    {
                        Msg.Assistant($"Tag {tagName} subido al remoto.");
                        await DialogService.ShowConfirmDialog("Éxito", $"Tag '{tagName}' subido al repositorio.", DialogVariant.Success, DialogType.Info);
                    }
                }
            });
        }


        private async void HistoryListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Limpiar las listas de abajo
            HistoryFilesListView.ItemsSource = null;
            HistoryDiffLinesItemsControl.ItemsSource = null;

            var selectedCommit = e.AddedItems.OfType<GitLogItem>().FirstOrDefault();
            if (selectedCommit == null)
            {
                CommitSummaryHeader.Visibility = Visibility.Collapsed;
                return;
            }

            // Poblar los campos
            CommitSummaryMessage.Text = selectedCommit.Message;
            CommitSummaryInfo.Text = $"{selectedCommit.Author} cometió {selectedCommit.Hash} ({selectedCommit.Date})";
            CommitSummaryDescription.Text = selectedCommit.Description;


            CommitSummaryHeader.Visibility = Visibility.Visible;


            if (!ValidateProject())
            {
                return;
            }

            try
            {
                // Cargar la lista de archivos para este commit
                var files = await Git.GetFilesChangedInCommit(selectedCommit.Hash, projectDirectory);
                HistoryFilesListView.ItemsSource = files;
            }
            catch (Exception ex)
            {
                Msg.Assistant($"Error al cargar archivos del commit: {ex.Message}");
            }
        }

        // --- NUEVO EVENTO: Cuando se hace clic en un ARCHIVO DEL HISTORIAL ---
        private async void HistoryFilesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HistoryDiffLinesItemsControl.ItemsSource = null;

            var selectedFile = e.AddedItems.OfType<string>().FirstOrDefault();
            var selectedCommit = HistoryListView.SelectedItem as GitLogItem; // Obtener el commit seleccionado

            if (selectedFile == null || selectedCommit == null || !ValidateProject())
            {
                return;
            }

            try
            {
                // 1. Obtener el commit "padre"
                string parentHash = await Git.GetCommitParentHash(selectedCommit.Hash, projectDirectory);
                // 2. Obtener el texto del archivo en el commit PADRE (el "antes")
                string oldText = await Git.GetFileContentAtCommitish(selectedFile, parentHash, projectDirectory);
                // 3. Obtener el texto del archivo en el commit ACTUAL (el "después")
                string newText = await Git.GetFileContentAtCommitish(selectedFile, selectedCommit.Hash, projectDirectory);



                // 4. Generar el DiffModel (Lógica de Hunks copiada de ChangesListView_SelectionChanged)
                var diffBuilder = new InlineDiffBuilder(new DiffPlex.Differ());
                var diff = diffBuilder.BuildDiffModel(oldText, newText);

                var filteredLines = new List<DiffPiece>();
                const int contextLines = 3;

                for (int i = 0; i < diff.Lines.Count; i++)
                {
                    var line = diff.Lines[i];
                    if (line.Type == ChangeType.Unchanged)
                    {
                        bool isContext = false;
                        for (int j = 1; j <= contextLines; j++)
                        {
                            if (i - j >= 0 && diff.Lines[i - j].Type != ChangeType.Unchanged) { isContext = true; break; }
                        }
                        if (!isContext)
                        {
                            for (int j = 1; j <= contextLines; j++)
                            {
                                if (i + j < diff.Lines.Count && diff.Lines[i + j].Type != ChangeType.Unchanged) { isContext = true; break; }
                            }
                        }
                        if (isContext) { filteredLines.Add(line); }
                        else if (filteredLines.Count > 0 && filteredLines.Last().Type != ChangeType.Imaginary)
                        {
                            filteredLines.Add(new DiffPiece("...", ChangeType.Imaginary, null));
                        }
                    }
                    else { filteredLines.Add(line); }
                }

                HistoryDiffLinesItemsControl.ItemsSource = filteredLines;
            }
            catch (Exception ex)
            {
                HistoryDiffLinesItemsControl.ItemsSource = new List<DiffPiece>
                    { new DiffPiece($"ERROR AL CARGAR DIFF: {ex.Message}", ChangeType.Deleted) };
            }
        }


        private void SelectAllCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (ChangesListView.ItemsSource is List<GitStatusItem> items)
            {
                foreach (var item in items)
                {
                    item.IsSelected = true;
                }
                ChangesListView.Items.Refresh();
                UpdateChangesCount();
            }

        }

        private void SelectAllCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (ChangesListView.ItemsSource is List<GitStatusItem> items)
            {
                foreach (var item in items)
                {
                    item.IsSelected = false;
                }
                ChangesListView.Items.Refresh();
                UpdateChangesCount();
            }
        }
        private async void DiscardChangesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateProject()) return;

            if (sender is MenuItem menuItem && menuItem.CommandParameter is GitStatusItem itemToDiscard)
            {
                bool confirm = await DialogService.ShowConfirmDialog(
                    "Descartar Cambios",
                    $"¿Estás seguro de que deseas descartar los cambios en '{itemToDiscard.FilePath}'?\nEsta acción no se puede deshacer.",
                    DialogVariant.Warning, DialogType.Confirm);

                if (!confirm) return;

                await RunWithLoading(async () =>
                {
                    string gitPath = itemToDiscard.FilePath.Replace(Path.DirectorySeparatorChar, '/'); // Use Git's path format

                    if (itemToDiscard.Status == "Sin seguimiento" || itemToDiscard.Status == "Añadido")
                    {
                        Msg.Assistant($"Eliminando archivo nuevo/sin seguimiento: {itemToDiscard.FilePath}");
                        await Git.EjecutarGit($"checkout -- \"{gitPath}\"", projectDirectory); // Try checkout first
                        await Git.EjecutarGit($"clean -fd -- \"{gitPath}\"", projectDirectory); // Then clean if needed
                    }
                    else
                    {
                        // For modified, deleted, renamed files, revert to HEAD
                        Msg.Assistant($"Descartando cambios en: {itemToDiscard.FilePath}");
                        await Git.EjecutarGit($"checkout -- \"{gitPath}\"", projectDirectory);
                    }

                    Msg.Assistant("✅ Cambios descartados.");
                    await LoadChangesAsync(); // Refresh the list
                });
            }
        }
        private void CommitCheckbox_Click(object sender, RoutedEventArgs e)
        {
            UpdateChangesCount();
        }

        private async void StashSelectedMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateProject()) return;

            // Get all *selected* items from the list
            var selectedItems = (ChangesListView.ItemsSource as List<GitStatusItem>)?.Where(i => i.IsSelected).ToList();

            if (selectedItems == null || !selectedItems.Any())
            {
                await DialogService.ShowConfirmDialog("Stash", "No hay archivos seleccionados para guardar en el stash.", DialogVariant.Info, DialogType.Info);
                return;
            }

            // Ask for an optional stash message
            var (ok, message) = await DialogService.ShowInputDialog("Stash", "Mensaje opcional para el stash:", $"Stash parcial ({selectedItems.Count} archivos)");
            if (!ok) return; // User cancelled

            string stashMessage = string.IsNullOrWhiteSpace(message) ? "" : $"-m \"{message.Replace("\"", "'")}\""; // Add message flag if provided
            string filePaths = string.Join(" ", selectedItems.Select(i => $"\"{i.FilePath.Replace(Path.DirectorySeparatorChar, '/')}\"")); // Get file paths in Git format

            await RunWithLoading(async () =>
            {
                Msg.Assistant($"Guardando {selectedItems.Count} archivos seleccionados en el stash...");
                // Use 'git stash push' with the message and file list
                var result = await Git.EjecutarGit($"stash push {stashMessage} -- {filePaths}", projectDirectory);

                if (result.Contains("Saved working directory and index state"))
                {
                    Msg.Assistant("✅ Cambios seleccionados guardados en el stash.");
                    await DialogService.ShowConfirmDialog("Éxito", "Los archivos seleccionados han sido guardados en el stash.", DialogVariant.Success, DialogType.Info);
                }
                else
                {
                    Msg.Assistant($"⚠️ Ocurrió un problema al guardar en el stash: {result}");
                    await DialogService.ShowConfirmDialog("Advertencia", $"Resultado de la operación stash:\n\n{result}", DialogVariant.Warning, DialogType.Info);
                }
                await LoadChangesAsync(); // Refresh the list
            });
        }
        private async void RestoreStashItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateProject() || (sender as System.Windows.Controls.Button)?.CommandParameter is not Git.StashEntry stash) return;

            bool confirm = await DialogService.ShowConfirmDialog(
                "Restaurar Stash",
                $"Esto aplicará los cambios de '{stash.Name}' ({stash.Message}) y lo eliminará.\n\n¿Continuar?",
                DialogVariant.Info, DialogType.Confirm);
            if (!confirm) return;

            await RunWithLoading(async () =>
            {
                var applyResult = await Git.ApplyStash(stash.Name, projectDirectory);
                if (applyResult.Success)
                {
                    await Git.DropStash(stash.Name, projectDirectory);
                    Msg.Assistant($"✅ Stash {stash.Name} restaurado y eliminado.");
                }
                else
                {
                    Msg.Assistant($"❌ Error al aplicar stash {stash.Name}: {applyResult.Output}");
                    await DialogService.ShowConfirmDialog("Error", $"No se pudo aplicar el stash (puede haber conflictos):\n\n{applyResult.Output}", DialogVariant.Error, DialogType.Info);
                }

                // SOLO RECARGAR, NO CAMBIAR VISTA
                await LoadChangesAsync();
            });
        }

        private async void DiscardStashItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateProject() || (sender as System.Windows.Controls.Button)?.CommandParameter is not Git.StashEntry stash) return;

            bool confirm = await DialogService.ShowConfirmDialog(
                "Descartar Stash",
                $"¿Estás seguro de que deseas eliminar permanentemente '{stash.Name}' ({stash.Message})?\nEsta acción no se puede deshacer.",
                DialogVariant.Warning, DialogType.Confirm);
            if (!confirm) return;

            await RunWithLoading(async () =>
            {
                await Git.DropStash(stash.Name, projectDirectory);
                Msg.Assistant($"✅ Stash {stash.Name} eliminado.");
                await LoadChangesAsync();
            });
        }

        private async void DiscardAllStashesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateProject()) return;

            bool confirm = await DialogService.ShowConfirmDialog(
                "Descartar TODOS los Stashes",
                $"¿Estás seguro de que deseas eliminar permanentemente TODOS los stashes guardados?\nEsta acción no se puede deshacer.",
                DialogVariant.Warning, DialogType.Confirm);

            if (!confirm) return;

            await RunWithLoading(async () =>
            {
                Msg.Assistant("Eliminando todos los stashes...");
                // Usamos EjecutarGit para 'stash clear'
                var result = await Git.EjecutarGit("stash clear", projectDirectory);
                Msg.Assistant("✅ Operación de limpieza de stash completada.");
                await LoadChangesAsync(); // Recargar todo
            });
        }


        #region ✅ Project Context Menu Handlers

        /// <summary>
        /// Helper para obtener la ruta (FullPath) desde el CommandParameter del MenuItem.
        /// </summary>
        private string GetPathFromMenuItem(object sender)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is string path)
            {
                return path;
            }
            return null; // No se pudo obtener la ruta
        }

        private async void ProjectMenuItem_OpenVisualStudio_Click(object sender, RoutedEventArgs e)
        {
            string path = GetPathFromMenuItem(sender);
            if (string.IsNullOrEmpty(path)) return;

            // Reutilizamos la lógica de 'btnAbrirSln' pero con el path específico
            try
            {
                var slnFile = Directory.GetFiles(path, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();

                if (slnFile != null)
                {
                    var vsInstances = System.Diagnostics.Process.GetProcessesByName("devenv");

                    bool estaAbierta = vsInstances.Any(p =>
                    {
                        try
                        {
                            return p.MainWindowTitle.Contains(Path.GetFileNameWithoutExtension(slnFile), StringComparison.OrdinalIgnoreCase);
                        }
                        catch { { return false; } }
                    });

                    if (estaAbierta)
                    {
                        await DialogService.ShowConfirmDialog("Información",
                            "Esta solución ya está abierta en Visual Studio."
                            ,
                            Views.Dialogs.DialogVariant.Info,
                            Views.Dialogs.DialogType.Info);
                        return;
                    }

                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = slnFile,
                        UseShellExecute = true
                    });

                }
                else
                {
                    await DialogService.ShowConfirmDialog("Alerta",
                        $"No se encontró ningún archivo .sln en el directorio"
                        ,
                        Views.Dialogs.DialogVariant.Warning,
                        Views.Dialogs.DialogType.Info);
                }
            }
            catch (Exception ex)
            {
                await DialogService.ShowConfirmDialog("Error",
                    $"Error al abrir: {ex.Message}"
                    ,
                    Views.Dialogs.DialogVariant.Error,
                    Views.Dialogs.DialogType.Info);
            }
        }

        private void ProjectMenuItem_OpenVSCode_Click(object sender, RoutedEventArgs e)
        {
            string path = GetPathFromMenuItem(sender);
            if (string.IsNullOrEmpty(path)) return;
            bool isWslPath = path.StartsWith(@"\\wsl$") || path.StartsWith(@"\\wsl.localhost");
            try
            {
                if (isWslPath)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "wsl",
                        Arguments = "code .", // Ejecuta el 'code' de Linux
                        WorkingDirectory = path, // wsl.exe entiende esta ruta UNC
                        UseShellExecute = false, // ¡Importante para que WorkingDirectory funcione!
                        CreateNoWindow = true
                    });
                }
                else
                {
                    // --- Caso Windows (Normal) ---
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "code",
                        Arguments = $"\"{path}\"",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    });
                }
            }
            catch (Exception ex)
            {
                DialogService.ShowTrayNotification("Error", $"No se pudo iniciar VS Code: {ex.Message}");
            }
        }

        private void ProjectMenuItem_OpenExplorer_Click(object sender, RoutedEventArgs e)
        {
            string path = GetPathFromMenuItem(sender);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"", // Abrir la carpeta en el explorador
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                DialogService.ShowTrayNotification("Error", $"No se pudo abrir el explorador: {ex.Message}");
            }
        }

        private void ProjectMenuItem_OpenCmd_Click(object sender, RoutedEventArgs e)
        {
            string path = GetPathFromMenuItem(sender);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    WorkingDirectory = path // Iniciar cmd en el directorio del proyecto
                });
            }
            catch (Exception ex)
            {
                DialogService.ShowTrayNotification("Error", $"No se pudo abrir cmd: {ex.Message}");
            }
        }

        private async void ProjectMenuItem_Remove_Click(object sender, RoutedEventArgs e)
        {
            string pathToRemove = GetPathFromMenuItem(sender);
            if (string.IsNullOrEmpty(pathToRemove)) return;

            var confirm = await DialogService.ShowConfirmDialog(
                "Remover Proyecto",
                $"¿Seguro que quieres remover '{new DirectoryInfo(pathToRemove).Name}' de la lista?\n(Esto no eliminará los archivos del disco).",
                DialogVariant.Warning,
                DialogType.Confirm);

            if (!confirm) return;

            ProjectSettings.RemoveProject(pathToRemove);
            LoadProjects(); // Recargar la lista

            // Si el proyecto eliminado era el activo, limpiar la UI
            if (projectDirectory == pathToRemove)
            {
                projectDirectory = null;
                ProjectsComboBox.SelectedItem = null;
                BranchesComboBox.ItemsSource = null;
                ChangesListView.ItemsSource = null;
                HistoryListView.ItemsSource = null;
                TagsListView.ItemsSource = null;
            }

            DialogService.ShowTrayNotification("Proyecto Removido", "El proyecto se quitó de la lista.");
        }

        #endregion
        #region ✅ Stash List Context Menu

        /// <summary>
        /// Helper para obtener el StashEntry desde el CommandParameter del MenuItem.
        /// </summary>
        private Git.StashEntry GetStashFromMenuItem(object sender)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is Git.StashEntry stash)
            {
                return stash;
            }
            return null;
        }

        private async void StashList_Apply_Click(object sender, RoutedEventArgs e)
        {
            var stash = GetStashFromMenuItem(sender);
            if (stash == null || !ValidateProject()) return;

            bool confirm = await DialogService.ShowConfirmDialog(
                "Aplicar Stash",
                $"Esto aplicará los cambios de '{stash.Name}: {stash.Message}' a tu directorio de trabajo.\n\n¿Continuar?",
                DialogVariant.Info, DialogType.Confirm);

            if (!confirm) return;

            await RunWithLoading(async () =>
            {
                Msg.Assistant($"Aplicando stash: {stash.Name}...");
                var applyResult = await Git.ApplyStash(stash.Name, projectDirectory);

                if (applyResult.Success)
                {
                    Msg.Assistant($"✅ Stash {stash.Name} aplicado.");
                    await DialogService.ShowConfirmDialog("Éxito", $"Stash aplicado correctamente.", DialogVariant.Success, DialogType.Info);
                }
                else
                {
                    Msg.Assistant($"❌ Error al aplicar stash {stash.Name}: {applyResult.Output}");
                    await DialogService.ShowConfirmDialog("Error", $"No se pudo aplicar el stash (puede haber conflictos):\n\n{applyResult.Output}", DialogVariant.Error, DialogType.Info);
                }
                await LoadChangesAsync();
            });
        }

        private async void StashList_Drop_Click(object sender, RoutedEventArgs e)
        {
            var stash = GetStashFromMenuItem(sender);
            if (stash == null || !ValidateProject()) return;

            bool confirm = await DialogService.ShowConfirmDialog(
                "Eliminar Stash",
                $"¿Estás seguro de que deseas eliminar permanentemente '{stash.Name}: {stash.Message}'?\nEsta acción no se puede deshacer.",
                DialogVariant.Warning, DialogType.Confirm);

            if (!confirm) return;

            await RunWithLoading(async () =>
            {
                Msg.Assistant($"Eliminando stash: {stash.Name}...");
                var dropResult = await Git.DropStash(stash.Name, projectDirectory);

                if (dropResult.Success)
                {
                    Msg.Assistant($"✅ Stash {stash.Name} eliminado.");
                    await DialogService.ShowConfirmDialog("Éxito", $"Stash eliminado.", DialogVariant.Success, DialogType.Info);
                }
                else
                {
                    Msg.Assistant($"❌ Error al eliminar stash {stash.Name}: {dropResult.Output}");
                    await DialogService.ShowConfirmDialog("Error", $"No se pudo eliminar el stash:\n\n{dropResult.Output}", DialogVariant.Error, DialogType.Info);
                }
                await LoadChangesAsync();
            });
        }
        private async void StashAllChangesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateProject()) return;

            var items = (ChangesListView.ItemsSource as List<GitStatusItem>);
            if (items == null || !items.Any())
            {
                await DialogService.ShowConfirmDialog("Stash", "No hay cambios para guardar en el stash.", DialogVariant.Info, DialogType.Info);
                return;
            }

            // Pedir un mensaje para el stash
            var (ok, message) = await DialogService.ShowInputDialog("Stash", "Mensaje para el stash:", $"Stash de {items.Count} archivos");
            if (!ok) return; // Usuario canceló

            string stashMessage = string.IsNullOrWhiteSpace(message) ? "" : $"-m \"{message.Replace("\"", "'")}\"";

            await RunWithLoading(async () =>
            {
                Msg.Assistant($"Guardando {items.Count} archivos en el stash...");

                // Usamos "git stash push" que es más moderno que "save"
                var result = await Git.EjecutarGit($"stash save {stashMessage}", projectDirectory);

                if (result.Contains("Saved working directory and index state"))
                {
                    Msg.Assistant("✅ Cambios guardados en el stash.");
                    await DialogService.ShowConfirmDialog("Éxito", "Todos los cambios han sido guardados en el stash.", DialogVariant.Success, DialogType.Info);
                }
                else
                {
                    Msg.Assistant($"⚠️ Ocurrió un problema al guardar en el stash: {result}");
                    await DialogService.ShowConfirmDialog("Advertencia", $"Resultado de la operación stash:\n\n{result}", DialogVariant.Warning, DialogType.Info);
                }
                await LoadChangesAsync(); // Recargar la lista
            });
        }

        // ✅ --- NUEVO: Descartar todos los cambios ---
        private async void DiscardAllChangesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateProject()) return;

            var items = (ChangesListView.ItemsSource as List<GitStatusItem>);
            if (items == null || !items.Any())
            {
                await DialogService.ShowConfirmDialog("Descartar", "No hay cambios para descartar.", DialogVariant.Info, DialogType.Info);
                return;
            }

            bool confirm = await DialogService.ShowConfirmDialog(
                "Descartar Todos los Cambios",
                $"¿Estás seguro de que deseas descartar TODOS los {items.Count} cambios?\nEsta acción no se puede deshacer.",
                DialogVariant.Warning, DialogType.Confirm);

            if (!confirm) return;

            await RunWithLoading(async () =>
            {
                Msg.Assistant("Descartando todos los cambios...");

                // 1. Descartar cambios en archivos rastreados (Modificados, Eliminados, etc.)
                await Git.EjecutarGit("checkout -- .", projectDirectory);

                // 2. Eliminar archivos no rastreados (??)
                await Git.EjecutarGit("clean -fd", projectDirectory);

                Msg.Assistant("✅ Todos los cambios han sido descartados.");
                await LoadChangesAsync(); // Recargar la lista
            });
        }

        private async void StashListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedStash = e.AddedItems.OfType<Git.StashEntry>().FirstOrDefault();
            // Si no hay un stash seleccionado (o se des-seleccionó),
            // volvemos a cargar los cambios reales (git status).
            if (selectedStash == null)
            {
                await LoadChangesAsync();
                return;
            }
            _currentlyViewedStash = selectedStash;
            // 1. Ocultar la vista normal, mostrar la vista de stash
            NormalChangesView.Visibility = Visibility.Collapsed;
            StashedChangesView.Visibility = Visibility.Visible;
            DiffLinesItemsControl.ItemsSource = null; // Limpiar diff

            // 2. Poblar la nueva vista
            StashView_Header.Text = selectedStash.Message;



            if (!ValidateProject()) return;

            // --- Muestra los archivos DENTRO del stash ---
            try
            {
                var fileStatuses = await Git.GetFileStatusesForStash(selectedStash.Name, projectDirectory);
                var stashChanges = new List<GitStatusItem>();

                foreach (var file in fileStatuses)
                {
                    var item = new GitStatusItem { FilePath = file.Key, IsSelected = false };
                    var status = file.Value;

                    switch (status)
                    {
                        case 'M':
                            item.Status = "Modificado (en Stash)";
                            item.Icon = PackIconKind.FileEdit;
                            item.Color = Brushes.Orange;
                            break;
                        case 'A':
                            item.Status = "Añadido (en Stash)";
                            item.Icon = PackIconKind.FilePlus;
                            item.Color = Brushes.Green;
                            break;
                        case 'D':
                            item.Status = "Eliminado (en Stash)";
                            item.Icon = PackIconKind.FileRemove;
                            item.Color = Brushes.Red;
                            break;
                        case 'R':
                            item.Status = "Renombrado (en Stash)";
                            item.Icon = PackIconKind.FileMove;
                            item.Color = Brushes.Blue;
                            break;
                        default:
                            item.Status = "Desconocido (en Stash)";
                            item.Icon = PackIconKind.FileQuestion;
                            item.Color = Brushes.Gray;
                            break;
                    }
                    stashChanges.Add(item);
                }

                StashFilesListView.ItemsSource = stashChanges; // ¡Poblamos la lista principal!
                //SelectAllCheckBox.IsChecked = false;
                // 4. Limpiar la selección del expander para que se pueda volver a clicar
                StashListView.SelectedItem = null;
            }
            catch (Exception ex)
            {
                Msg.Assistant($"Error al mostrar archivos del stash: {ex.Message}");
            }
        }

        private async void StashFilesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedFile = e.AddedItems.OfType<GitStatusItem>().FirstOrDefault();
            if (selectedFile == null || _currentlyViewedStash == null || !ValidateProject())
            {
                DiffLinesItemsControl.ItemsSource = null;
                return;
            }

            try
            {
                // "Old Text" = El commit en el que se basó el stash (stash^)
                string oldText = await Git.GetFileContentAtCommitish(selectedFile.FilePath, $"{_currentlyViewedStash.Name}^", projectDirectory);

                // "New Text" = El contenido guardado en el stash
                string newText = await Git.GetFileContentAtCommitish(selectedFile.FilePath, _currentlyViewedStash.Name, projectDirectory);

                // (Copiar la lógica de Hunks/Diff de ChangesListView_SelectionChanged)
                var diffBuilder = new InlineDiffBuilder(new DiffPlex.Differ());
                var diff = diffBuilder.BuildDiffModel(oldText, newText);

                var filteredLines = new List<DiffPiece>();
                const int contextLines = 3;

                for (int i = 0; i < diff.Lines.Count; i++)
                {
                    var line = diff.Lines[i];
                    if (line.Type == ChangeType.Unchanged)
                    {
                        bool isContext = false;
                        for (int j = 1; j <= contextLines; j++)
                        {
                            if (i - j >= 0 && diff.Lines[i - j].Type != ChangeType.Unchanged) { isContext = true; break; }
                        }
                        if (!isContext)
                        {
                            for (int j = 1; j <= contextLines; j++)
                            {
                                if (i + j < diff.Lines.Count && diff.Lines[i + j].Type != ChangeType.Unchanged) { isContext = true; break; }
                            }
                        }
                        if (isContext) { filteredLines.Add(line); }
                        else if (filteredLines.Count > 0 && filteredLines.Last().Type != ChangeType.Imaginary)
                        {
                            filteredLines.Add(new DiffPiece("...", ChangeType.Imaginary, null));
                        }
                    }
                    else { filteredLines.Add(line); }
                }

                DiffLinesItemsControl.ItemsSource = filteredLines;
            }
            catch (Exception ex)
            {
                DiffLinesItemsControl.ItemsSource = new List<DiffPiece>
        { new DiffPiece($"ERROR AL CARGAR DIFF DE STASH: {ex.Message}", ChangeType.Deleted) };
            }
        }

        // Botón "Volver" en la vista de Stash
        private void StashView_BackButton_Click(object sender, RoutedEventArgs e)
        {
            _currentlyViewedStash = null;
            NormalChangesView.Visibility = Visibility.Visible;
            StashedChangesView.Visibility = Visibility.Collapsed;
            DiffLinesItemsControl.ItemsSource = null; // Limpiar diff
        }

        // Botón "Restaurar" en la vista de Stash
        private async void StashView_RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentlyViewedStash == null) return;

            string stashName = _currentlyViewedStash.Name;

            bool confirm = await DialogService.ShowConfirmDialog(
                "Restaurar Stash",
                $"Esto aplicará los cambios de '{stashName}' y lo eliminará.\n\n¿Continuar?",
                DialogVariant.Info, DialogType.Confirm);
            if (!confirm) return;

            await RunWithLoading(async () =>
            {
                var applyResult = await Git.ApplyStash(stashName, projectDirectory);
                if (applyResult.Success)
                {
                    await Git.DropStash(stashName, projectDirectory);
                    Msg.Assistant($"✅ Stash {stashName} restaurado y eliminado.");
                }
                else
                {
                    Msg.Assistant($"❌ Error al aplicar stash {stashName}: {applyResult.Output}");
                    await DialogService.ShowConfirmDialog("Error", $"No se pudo aplicar el stash (puede haber conflictos):\n\n{applyResult.Output}", DialogVariant.Error, DialogType.Info);
                }

                // Volver a la vista normal y recargar
                StashView_BackButton_Click(null, null);
                await LoadChangesAsync();
            });
        }

        // Botón "Descartar" en la vista de Stash
        private async void StashView_DiscardButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentlyViewedStash == null) return;

            string stashName = _currentlyViewedStash.Name;

            bool confirm = await DialogService.ShowConfirmDialog(
                "Descartar Stash",
                $"¿Estás seguro de que deseas eliminar permanentemente '{stashName}'?\nEsta acción no se puede deshacer.",
                DialogVariant.Warning, DialogType.Confirm);
            if (!confirm) return;

            await RunWithLoading(async () =>
            {
                await Git.DropStash(stashName, projectDirectory);
                Msg.Assistant($"✅ Stash {stashName} eliminado.");

                // Volver a la vista normal y recargar
                StashView_BackButton_Click(null, null);
                await LoadChangesAsync();
            });
        }

        #endregion

        private async Task UpdateBranchIndicatorsAsync()
        {
            if (!_isWindowInitialized) return;

            if (!ValidateProject())
            {
                GitActionsComboBox.Visibility = Visibility.Collapsed;
                return;
            }

            _currentGitStatus = await Git.GetAheadBehindCount(projectDirectory);
            GitActionsComboBox.Visibility = Visibility.Visible;

            var activeBrush = Brushes.Orange;
            var defaultBrush = Brushes.White; // O Brushes.Gray si prefieres en el tema oscuro

            // --- Resetea el color del ComboBox ---
            GitActionsComboBox.BorderBrush = defaultBrush;
            DefaultGitActionText.Foreground = defaultBrush;
            DefaultGitActionIcon.Foreground = defaultBrush;

            // --- 2. Resetea la visibilidad (sin cambios) ---
            PullGitActionItem.Visibility = Visibility.Visible;
            PushGitActionItem.Visibility = Visibility.Visible;
            FetchGitActionItem.Visibility = Visibility.Visible;

            // --- Actualiza los textos de los items del DROPDOWN ---
            string pullText = "Pull Origin";
            if (_currentGitStatus.Behind > 0)
            {
                pullText = $"Pull Origin ({_currentGitStatus.Behind} ↓)";
            }
            PullGitActionText.Text = pullText;

            string pushText = "Push Origin";
            if (_currentGitStatus.Ahead > 0)
            {
                pushText = $"Push Origin ({_currentGitStatus.Ahead} ↑)";
            }
            PushGitActionText.Text = pushText;
            GitActionsComboBox.SelectionChanged -= GitActionsComboBox_SelectionChanged;
            // --- Lógica de Prioridad para el item SELECCIONADO (Índice 0) ---
            if (_currentGitStatus.Behind > 0) // Prioridad 1: Pull
            {
                _currentGitAction = GitActionState.Pull;
                DefaultGitActionIcon.Kind = PackIconKind.CloudDownloadOutline;
                DefaultGitActionText.Text = pullText; // Muestra "Pull Origin (2 ↓)"
                GitActionsComboBox.BorderBrush = activeBrush;
                DefaultGitActionText.Foreground = activeBrush;
                DefaultGitActionIcon.Foreground = activeBrush;

                PullGitActionItem.Visibility = Visibility.Collapsed;
            }
            else if (_currentGitStatus.Ahead > 0) // Prioridad 2: Push
            {
                _currentGitAction = GitActionState.Push;
                DefaultGitActionIcon.Kind = PackIconKind.CloudUploadOutline;
                DefaultGitActionText.Text = pushText; // Muestra "Push Origin (1 ↑)"
                GitActionsComboBox.BorderBrush = activeBrush;
                DefaultGitActionText.Foreground = activeBrush;
                DefaultGitActionIcon.Foreground = activeBrush;

                PushGitActionItem.Visibility = Visibility.Collapsed;
            }
            else // Prioridad 3: Fetch
            {
                _currentGitAction = GitActionState.Fetch;
                DefaultGitActionIcon.Kind = PackIconKind.Refresh;
                DefaultGitActionText.Text = "Fetch Origin";
                FetchGitActionItem.Visibility = Visibility.Collapsed;
            }

            // Asegurarse de que el ítem 0 esté seleccionado
            GitActionsComboBox.SelectedIndex = 0;
            GitActionsComboBox.SelectionChanged += GitActionsComboBox_SelectionChanged;
        }
        private async void GitActionsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isWindowInitialized) return;
            if (GitActionsComboBox.SelectedItem == null) return;

            var selectedItem = (ComboBoxItem)GitActionsComboBox.SelectedItem;
            int selectedIndex = GitActionsComboBox.SelectedIndex;

            if (selectedItem == PullGitActionItem)
            {
                await DoPullAsync();
            }
            else if (selectedItem == PushGitActionItem)
            {
                await DoPushAsync();
            }
            else if (selectedItem == FetchGitActionItem)
            {
                await DoFetchAsync();
            }

            // 4. Reseteo (sin cambios)
            if (selectedIndex > 0)
            {
                GitActionsComboBox.SelectionChanged -= GitActionsComboBox_SelectionChanged;
                GitActionsComboBox.SelectedIndex = 0;
                GitActionsComboBox.SelectionChanged += GitActionsComboBox_SelectionChanged;
            }
        }
        private async void GitActionsComboBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_isWindowInitialized) return;
            if (GitActionsComboBox.IsDropDownOpen)
            {
                return;
            }
            if (e.OriginalSource is System.Windows.Shapes.Path)
            {
                // Deja que el ComboBox abra el desplegable normalmente.
                return;
            }
            e.Handled = true;

            // 3. Ejecutamos la acción prioritaria (la misma lógica del switch)
            switch (_currentGitAction)
            {
                case GitActionState.Pull:
                    await DoPullAsync();
                    break;
                case GitActionState.Push:
                    await DoPushAsync();
                    break;
                case GitActionState.Fetch:
                    await DoFetchAsync();
                    break;
            }
        }

        private void GitActionsComboBox_PreviewMouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isWindowInitialized)
            {
                e.Handled = true; // Detenemos cualquier otro menú contextual
                GitActionsComboBox.IsDropDownOpen = true; // Forzamos la apertura
            }
        }
        // 1. El "Master" Click Handler para el botón principal
        private async void btnGitAction_Click(object sender, RoutedEventArgs e)
        {
            // Ejecuta la acción que esté configurada actualmente
            switch (_currentGitAction)
            {
                case GitActionState.Pull:
                    await DoPullAsync();
                    break;
                case GitActionState.Push:
                    await DoPushAsync();
                    break;
                case GitActionState.Fetch:
                    await DoFetchAsync();
                    break;
            }
        }

        // 2. El handler para el botón del menú (la flecha)
        private void btnGitActionMenu_Click(object sender, RoutedEventArgs e)
        {
            // Abre el ContextMenu que está definido en el XAML
            var button = sender as System.Windows.Controls.Button;
            if (button?.ContextMenu != null)
            {
                button.ContextMenu.IsOpen = true;
            }
        }

        // 3. El handler para el item "Fetch" DENTRO del menú
        private async void FetchMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await DoFetchAsync();
        }
        /// <summary>
        /// Actualiza el texto de la pestaña "Cambios" y el botón "Commit"
        /// según la cantidad de archivos seleccionados.
        /// </summary>
        private void UpdateChangesCount()
        {
            if (ChangesListView.ItemsSource == null)
            {
                ChangesTabHeader.Text = "Cambios";
                btnCommit.Content = "Commit";
                btnCommit.IsEnabled = false;
                return;
            }

            var allChanges = (ChangesListView.ItemsSource as List<GitStatusItem>);
            int totalCount = allChanges.Count;
            int selectedCount = allChanges.Count(i => i.IsSelected);
            string branchName = _currentlySelectedBranch ?? "main";

            // 1. Actualizar la Pestaña (muestra el total)
            ChangesTabHeader.Text = totalCount > 0 ? $"Cambios ({totalCount})" : "Cambios";

            // 2. Actualizar el Botón de Commit (muestra los seleccionados)
            if (selectedCount > 0)
            {
                btnCommit.Content = $"Commit {selectedCount} files to {branchName}";
                btnCommit.IsEnabled = true;
            }
            else
            {
                btnCommit.Content = "Commit";
                btnCommit.IsEnabled = false;
            }
        }
        // Este método se ejecuta JUSTO ANTES de que se muestre el menú contextual
        private void History_ContextMenu_Opening(object sender, ContextMenuEventArgs e)
        {
            // 1. Obtiene el item (StackPanel) donde se hizo clic derecho
            var grid = sender as Grid;
            var clickedItem = grid?.DataContext as GitLogItem;
            if (clickedItem == null || HistoryListView.ItemsSource == null) return;

            // 2. Obtiene el *primer* item de toda la lista (el HEAD)
            var items = HistoryListView.ItemsSource as List<GitLogItem>;
            var firstItem = items?.FirstOrDefault();
            if (firstItem == null) return;

            // 3. Busca el MenuItem por su nombre
            // --- ✅ CORRECCIÓN: Usamos grid.ContextMenu ---
            var contextMenu = grid.ContextMenu;
            var resetMenuItem = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "ResetSoftMenuItem");
            if (resetMenuItem == null) return;

            // 4. Habilita el botón SÓLO SI el item clickeado es el primer item
            bool isFirstItem = (clickedItem.Hash == firstItem.Hash);
            resetMenuItem.IsEnabled = isFirstItem;

            // Opcional: Cambia el texto si está deshabilitado
            if (!isFirstItem)
            {
                resetMenuItem.Header = "Solo se puede deshacer el último commit";
            }
            else
            {
                resetMenuItem.Header = "Undo Last Commit (Soft)";
            }
        }
        private async void History_ResetSoft_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateProject()) return;

            var repoStatus = _currentGitStatus;

            if (repoStatus == null)
            {
                Msg.Assistant("⚠️ No se pudo determinar el estado remoto. Actualiza primero el historial.");
                await DialogService.ShowConfirmDialog(
                    "Estado desconocido",
                    "No se puede verificar si el commit ya fue subido.\n" +
                    "Actualiza el historial o sincroniza el repositorio antes de continuar.",
                    DialogVariant.Warning,
                    DialogType.Info
                );
                return;
            }

            // Si no hay commits pendientes de subida, bloquear
            if (repoStatus.Ahead == 0)
            {
                Msg.Assistant("⚠️ No hay commits pendientes por subir. El último commit ya fue subido.");
                await DialogService.ShowConfirmDialog(
                    "Commit ya publicado",
                    "El último commit ya fue subido al repositorio remoto.\n" +
                    "No se puede deshacer un commit que ya fue compartido.",
                    DialogVariant.Warning,
                    DialogType.Info
                );
                return;
            }

            // Confirmar acción
            var confirm = await DialogService.ShowConfirmDialog(
                "Confirmar 'Undo Last Commit'",
                "¿Deseas deshacer el último commit?\n\n" +
                "Esta acción moverá el puntero de la rama hacia atrás un commit (reset --soft HEAD~1).\n" +
                "Los cambios del commit volverán a 'Changes' (Staged).",
                DialogVariant.Warning,
                DialogType.Confirm
            );

            if (!confirm)
            {
                Msg.Assistant("Operación de reset cancelada.");
                return;
            }

            await RunWithLoading(async () =>
            {
                Msg.Assistant("Ejecutando reset --soft HEAD~1...");
                var result = await Git.EjecutarGit("reset --soft HEAD~1", projectDirectory);

                if (result.Contains("fatal:") || result.Contains("error:"))
                {
                    Msg.Assistant($"❌ Error al ejecutar reset: {result}");
                    await DialogService.ShowConfirmDialog(
                        "Error",
                        $"No se pudo completar el reset:\n{result}",
                        DialogVariant.Error,
                        DialogType.Info
                    );
                }
                else
                {
                    Msg.Assistant("✅ Commit deshecho correctamente. Los cambios están en 'Changes'.");
                }
                await LoadChangesAsync();
                await LoadHistoryAsync();
            });
        }
        /// <summary>
        /// Crea un nuevo TAG a partir de un commit seleccionado en el historial.
        /// </summary>
        private async void History_CreateTag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.CommandParameter is not string commitHash) return;
            if (!ValidateProject()) return;

            // 1. Pedir nombre del Tag
            var (okTag, tagName) = await DialogService.ShowInputDialog("Crear Tag desde Commit", "Ingrese el nombre del tag (ej: v1.0.1):");
            if (!okTag || string.IsNullOrWhiteSpace(tagName)) return;

            // 2. Pedir mensaje del Tag
            var (okMsg, tagMessage) = await DialogService.ShowInputDialog("Crear Tag", "Ingrese un mensaje para el tag (anotación):", $"Release {tagName}");
            if (!okMsg || string.IsNullOrWhiteSpace(tagMessage)) return;

            await RunWithLoading(async () =>
            {
                Msg.Assistant($"Creando tag {tagName} en {commitHash}...");

                // 3. Llamar al método de Git MODIFICADO
                var result = await Git.CreateTag(tagName, tagMessage, projectDirectory, commitHash);

                if (!result.Success)
                {
                    Msg.Assistant($"Error al crear tag: {result.Output}");
                    await DialogService.ShowConfirmDialog("Error", $"No se pudo crear el tag:\n{result.Output}", DialogVariant.Error, DialogType.Info);
                    return;
                }

                Msg.Assistant($"Tag {tagName} creado localmente.");

                // 4. Recargar la lista de Tags y cambiar de pestaña
                await LoadTagsAsync();
                GitTabs.SelectedItem = TagsTab;

                // --- ✅ LÓGICA DE PUSH AÑADIDA ---
                // 5. Preguntar si desea subirlo
                var push = await DialogService.ShowConfirmDialog("Tag Creado",
                    $"El tag '{tagName}' se creó localmente.\n\n¿Desea subir (push) este tag al repositorio remoto (origin) ahora?",
                    DialogVariant.Info, DialogType.Confirm);

                if (push)
                {
                    Msg.Assistant($"Subiendo tag {tagName}...");
                    var pushResult = await Git.PushTag(tagName, projectDirectory);

                    if (!pushResult.Success)
                    {
                        Msg.Assistant($"Error al subir tag: {pushResult.Output}");
                        await DialogService.ShowConfirmDialog("Error", $"No se pudo subir el tag:\n{pushResult.Output}", DialogVariant.Error, DialogType.Info);
                    }
                    else
                    {
                        Msg.Assistant($"Tag {tagName} subido al remoto.");
                        await DialogService.ShowConfirmDialog("Éxito", $"Tag '{tagName}' subido al repositorio.", DialogVariant.Success, DialogType.Info);
                    }
                }
                // --- FIN DE LA LÓGICA DE PUSH ---
            });
        }

        /// <summary>
        /// Crea una nueva RAMA a partir de un commit seleccionado en el historial.
        /// </summary>
        private async void History_CreateBranch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.CommandParameter is not string commitHash) return;
            if (!ValidateProject()) return;

            // 1. Pedir nombre de la Rama
            var (okBranch, branchName) = await DialogService.ShowInputDialog("Crear Rama desde Commit", "Ingrese el nombre de la nueva rama:");
            if (!okBranch || string.IsNullOrWhiteSpace(branchName)) return;

            await RunWithLoading(async () =>
            {
                Msg.Assistant($"Creando rama {branchName} en {commitHash}...");

                // 2. Ejecutar comando Git
                var result = await Git.EjecutarGit($"branch {branchName} {commitHash}", projectDirectory);

                if (result.Contains("fatal:") || result.Contains("error:"))
                {
                    Msg.Assistant($"Error al crear rama: {result}");
                    await DialogService.ShowConfirmDialog("Error", $"No se pudo crear la rama:\n{result}", DialogVariant.Error, DialogType.Info);
                    return;
                }

                Msg.Assistant($"✅ Rama '{branchName}' creada.");

                // 3. Refrescar la lista de ramas
                var branches = Git.GetBranches(projectDirectory);
                BranchesComboBox.ItemsSource = branches;

                // 4. Preguntar si quiere cambiarse a la nueva rama
                var checkout = await DialogService.ShowConfirmDialog("Rama Creada",
                    $"La rama '{branchName}' se creó correctamente.\n\n¿Quieres cambiarte (checkout) a esta rama ahora?",
                    DialogVariant.Info, DialogType.Confirm);

                if (checkout)
                {

                    BranchesComboBox.SelectedItem = branchName;
                }
            });
        }
        private async void DeleteTag_Click(object sender, RoutedEventArgs e)
        {
            // 1. Obtener el nombre del tag
            if (sender is not MenuItem menuItem || menuItem.CommandParameter is not GitTagItem tag)
            {
                return;
            }
            string tagName = tag.TagName;
            // --- FIN DEL CAMBIO ---

            if (!ValidateProject()) return;
            // 2. Confirmar con el usuario
            var confirm = await DialogService.ShowConfirmDialog(
                "Confirmar Eliminación de Tag",
                $"¿Estás seguro de que deseas eliminar el tag '{tagName}'?\n\n" +
                "Esto intentará eliminarlo tanto de tu repositorio LOCAL como del REMOTO (origin/GitLab).",
                DialogVariant.Warning,
                DialogType.Confirm
            );

            if (!confirm)
            {
                Msg.Assistant("Operación de eliminación de tag cancelada.");
                return;
            }

            // 3. Ejecutar los comandos de Git
            await RunWithLoading(async () =>
            {
                Msg.Assistant($"Eliminando tag '{tagName}' localmente...");
                var localResult = await Git.DeleteTagLocal(tagName, projectDirectory);

                if (!localResult.Success)
                {
                    Msg.Assistant($"⚠️ No se pudo eliminar el tag local: {localResult.Output}");
                    // (Continuamos para intentar eliminar el remoto de todos modos)
                }

                Msg.Assistant($"Eliminando tag '{tagName}' del remoto (origin)...");
                var remoteResult = await Git.DeleteTagRemote(tagName, projectDirectory);

                if (!remoteResult.Success)
                {
                    Msg.Assistant($"⚠️ No se pudo eliminar el tag remoto: {remoteResult.Output}");
                    await DialogService.ShowConfirmDialog("Aviso", $"El tag se eliminó localmente, pero no se pudo eliminar del remoto (quizás ya no existía allí).\n\n{remoteResult.Output}", DialogVariant.Info, DialogType.Info);
                }
                else
                {
                    Msg.Assistant($"✅ Tag '{tagName}' eliminado de local y remoto.");
                }

                // 4. Refrescar la lista de tags
                await LoadTagsAsync();
            });
        }
        private async void Branch_Delete_Click(object sender, RoutedEventArgs e)
        {
            // 1. Obtener el nombre de la rama
            if (sender is not MenuItem menuItem || menuItem.CommandParameter is not string branchName)
                return;

            if (!ValidateProject()) return;

            // 2. No permitir borrar la rama activa
            if (branchName.Equals(_currentlySelectedBranch, StringComparison.OrdinalIgnoreCase))
            {
                await DialogService.ShowConfirmDialog(
                    "Error",
                    $"No puedes eliminar la rama '{branchName}' porque es la rama en la que estás trabajando actualmente.",
                    DialogVariant.Error,
                    DialogType.Info
                );
                return;
            }

            // 3. Proteger ramas principales
            if (branchName.Equals("main", StringComparison.OrdinalIgnoreCase) ||
                branchName.Equals("master", StringComparison.OrdinalIgnoreCase))
            {
                await DialogService.ShowConfirmDialog(
                    "Error",
                    $"No se puede eliminar la rama principal ('{branchName}').",
                    DialogVariant.Error,
                    DialogType.Info
                );
                return;
            }

            // 4. Confirmar eliminación local
            var confirmLocal = await DialogService.ShowConfirmDialog(
                "Eliminar Rama Local",
                $"¿Deseas eliminar la rama local '{branchName}'?\n\n" +
                "Esta acción eliminará la rama en tu repositorio local.",
                DialogVariant.Warning,
                DialogType.Confirm
            );

            if (!confirmLocal)
            {
                Msg.Assistant("Operación cancelada: no se eliminó la rama local.");
                return;
            }

            // 5. Eliminar rama local
            await RunWithLoading(async () =>
            {
                Msg.Assistant($"Eliminando rama local '{branchName}'...");
                var localResult = await Git.DeleteBranchLocal(branchName, projectDirectory);

                if (!localResult.Success)
                {
                    Msg.Assistant($"⚠️ No se pudo eliminar la rama local: {localResult.Output}");
                    await DialogService.ShowConfirmDialog(
                        "Error",
                        $"No se pudo eliminar la rama local '{branchName}'.\n\n{localResult.Output}",
                        DialogVariant.Error,
                        DialogType.Info
                    );
                    return;
                }

                Msg.Assistant($"✅ Rama local '{branchName}' eliminada correctamente.");

                // 6. Preguntar si también desea eliminar la remota
                var confirmRemote = await DialogService.ShowConfirmDialog(
                    "Eliminar Rama Remota",
                    $"¿Deseas eliminar también la rama remota 'origin/{branchName}'?",
                    DialogVariant.Warning,
                    DialogType.Confirm
                );

                if (confirmRemote)
                {
                    Msg.Assistant($"Eliminando rama remota 'origin/{branchName}'...");
                    var remoteResult = await Git.DeleteBranchRemote(branchName, projectDirectory);

                    if (!remoteResult.Success)
                    {
                        Msg.Assistant($"⚠️ No se pudo eliminar la rama remota: {remoteResult.Output}");
                        await DialogService.ShowConfirmDialog(
                            "Aviso",
                            $"No se pudo eliminar la rama remota 'origin/{branchName}'.\n\n{remoteResult.Output}",
                            DialogVariant.Warning,
                            DialogType.Info
                        );
                    }
                    else
                    {
                        Msg.Assistant($"✅ Rama remota 'origin/{branchName}' eliminada correctamente.");
                    }
                }
                else
                {
                    Msg.Assistant("Eliminación remota cancelada por el usuario.");
                }

                // 7. Refrescar lista de ramas
                var branches = Git.GetBranches(projectDirectory);
                BranchesComboBox.ItemsSource = branches;
                if (branches.Contains(_currentlySelectedBranch))
                    BranchesComboBox.SelectedItem = _currentlySelectedBranch;
            });
        }
        private async void ModoAgenteComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModoAgenteComboBox.SelectedIndex <= 0)
                return;

            var selectedItem = (ComboBoxItem)ModoAgenteComboBox.SelectedItem;
            // Detecta qué opción fue elegida por el usuario
            if (selectedItem == AddMethodItem)
            {
                await RunWithLoading(async () =>
                {
                    AddMethod_Click();
                });
            }
            else if (selectedItem == RollbackItem)
            {


                await RunWithLoading(async () =>
                {
                    RollbackSelectModule();
                });
            }
            else if (selectedItem == SqlGeneratorItem)
            {
                var sqlView = new Chapi.Views.SqlGeneratorView();
                sqlView.Owner = this;
                sqlView.ShowDialog();
            }
            ModoAgenteComboBox.SelectedIndex = 0;
        }

        private void DiffLine_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // 1. Obtener la línea (DiffPiece) donde se hizo clic
            var grid = sender as Grid;
            var diffPiece = grid?.DataContext as DiffPlex.DiffBuilder.Model.DiffPiece;

            // 2. Obtener el MenuItem
            var contextMenu = grid.ContextMenu;
            var menuItem = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "DiffLineMenu_OpenFile");
            if (menuItem == null || diffPiece == null) return;

            // 3. Obtener el número de línea
            _activeDiffLine = diffPiece.Position;
            if (!_activeDiffLine.HasValue || diffPiece.Type == DiffPlex.DiffBuilder.Model.ChangeType.Imaginary)
            {
                menuItem.IsEnabled = false;
                _activeDiffFile = null;
                return;
            }

            // 4. Determinar el archivo activo (depende de la pestaña)
            _activeDiffFile = null;
            if (GitTabs.SelectedItem == ChangesTab)
            {
                // Si estamos en "Cambios" (o viendo un Stash)
                GitStatusItem selectedChange = null;


                if (NormalChangesView.Visibility == Visibility.Visible)
                {
                    selectedChange = ChangesListView.SelectedItem as GitStatusItem;
                }
                else if (StashedChangesView.Visibility == Visibility.Visible)
                {
                    selectedChange = StashFilesListView.SelectedItem as GitStatusItem;
                }

                if (selectedChange != null)
                {
                    _activeDiffFile = selectedChange.FilePath;
                }
            }


            // 5. Actualizar el texto y estado del MenuItem
            if (!string.IsNullOrEmpty(_activeDiffFile))
            {
                menuItem.Header = $"Abrir '{Path.GetFileName(_activeDiffFile)}' en línea {_activeDiffLine.Value}";
                menuItem.IsEnabled = true;
            }
            else
            {
                menuItem.Header = "Abrir en Editor";
                menuItem.IsEnabled = false;
            }
        }

        // --- 👇 AÑADIR ESTE MÉTODO (Handler de Acción) ---
        private async void DiffLineMenu_OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeDiffFile) || !_activeDiffLine.HasValue || !ValidateProject())
            {
                Msg.Assistant("No se pudo determinar el archivo o la línea.");
                return;
            }

            try
            {
                string fullPath = Path.Combine(projectDirectory, _activeDiffFile);
                if (!File.Exists(fullPath))
                {
                    Msg.Assistant($"El archivo no existe: {fullPath}");
                    return;
                }

                // Usamos el comando "code -g" (goto)
                // Reutilizamos la lógica de 'ProjectMenuItem_OpenVSCode_Click' para WSL
                bool isWslPath = projectDirectory.StartsWith(@"\\wsl$") || projectDirectory.StartsWith(@"\\wsl.localhost");

                if (isWslPath)
                {
                    // Para WSL, 'code -g' debe ejecutarse desde wsl
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "wsl",
                        Arguments = $"code -g \"{fullPath}\":{_activeDiffLine.Value}",
                        WorkingDirectory = projectDirectory,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                else
                {
                    // Para Windows normal
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "code",
                        Arguments = $"-g \"{fullPath}\":{_activeDiffLine.Value}",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    });
                }
            }
            catch (Exception ex)
            {
                Msg.Assistant($"Error al abrir VS Code: {ex.Message}");
                await DialogService.ShowConfirmDialog("Error",
                    $"No se pudo iniciar VS Code (¿está en el PATH?):\n{ex.Message}"
                    , Views.Dialogs.DialogVariant.Error, Views.Dialogs.DialogType.Info);
            }
        }


        /// <summary>
        /// Comprueba si Git está instalado y actualiza la UI.
        /// </summary>
        private async Task CheckGitInstallationAsync()
        {
            _isGitInstalled = Git.IsGitInstalled();

            if (_isGitInstalled)
            {
                // Git está. Muestra la UI normal.
                GitControlsView.Visibility = Visibility.Visible;
                GitMissingView.Visibility = Visibility.Collapsed;

                // Habilita las otras pestañas de Git
                HistoryTab.IsEnabled = true;
                TagsTab.IsEnabled = true;

                // Intenta cargar los cambios del proyecto (si hay uno seleccionado)
                if (projectDirectory != null)
                {
                    await LoadChangesAsync();
                }
            }
            else
            {
                // Git NO está. Muestra el error.
                GitControlsView.Visibility = Visibility.Collapsed;
                GitMissingView.Visibility = Visibility.Visible;

                // Deshabilita las pestañas que dependen de Git
                ChangesTab.IsSelected = true;
                HistoryTab.IsEnabled = false;
                TagsTab.IsEnabled = false;

                Msg.Assistant("⚠️ No se pudo detectar Git. Por favor, instálalo para continuar.");
            }
        }

        /// <summary>
        /// Abre el navegador para descargar Git.
        /// </summary>
        private void btnInstallGit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://git-scm.com/downloads",
                    UseShellExecute = true // Importante para abrir en el navegador
                });
            }
            catch (Exception ex)
            {
                Msg.Assistant($"Error al abrir el navegador: {ex.Message}");
            }
        }

        /// <summary>
        /// Vuelve a comprobar si el usuario ya instaló Git.
        /// </summary>
        private async void btnRefreshGitCheck_Click(object sender, RoutedEventArgs e)
        {
            await RunWithLoading(CheckGitInstallationAsync);
        }
    }

}