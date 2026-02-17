using Chapi.Application.UseCases.Git;
using Chapi.Domain.Entities;
using Chapi.Infrastructure.Services;
using Chapi.Presentation.Views.Dialogs;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using MaterialDesignThemes.Wpf;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;

namespace Chapi.Presentation.ViewModels;

/// <summary>
/// ViewModel para la pestana de cambios.
/// Maneja la lista de archivos modificados y comandos relacionados.
/// </summary>
public class ChangesViewModel : ViewModelBase
{
    private readonly LoadChangesUseCase _loadChangesUseCase;
    private readonly CommitChangesUseCase _commitChangesUseCase;
    private readonly DiscardChangesUseCase _discardChangesUseCase;
    private readonly StashChangesUseCase _stashChangesUseCase;
    private readonly StashPopUseCase _stashPopUseCase;
    private readonly StashDropUseCase _stashDropUseCase;
    private readonly StashClearUseCase _stashClearUseCase;
    private readonly Domain.Interfaces.IGitRepository _gitRepository;
    private readonly GetFileDiffUseCase _getFileDiffUseCase;
    private readonly PushChangesUseCase _pushChangesUseCase;
    private readonly Domain.Interfaces.IGitAuthProviderFactory _authFactory;
    private readonly Domain.Interfaces.ICredentialStorageService _credentialStorage;
    private readonly Chapi.Infrastructure.Git.GitChangeWatcher _changeWatcher;
    private readonly Chapi.Infrastructure.Git.GitChangesCache _changesCache;

    private string _projectPath = string.Empty;
    private int _totalAdditions;
    private int _totalDeletions;
    private string _commitSummary = string.Empty;
    private string _commitDescription = string.Empty;
    private ChangeItemViewModel? _selectedChange;
    private GitStash? _selectedStash;
    private ChangeItemViewModel? _selectedStashedFile;
    private bool _isMassUpdating;
    private bool _isStashViewVisible;
    private bool _isGenerating;
    private bool _isSyncing;
    private CancellationTokenSource? _loadCts;

    public event EventHandler? CommitCompleted;

    private readonly Chapi.Application.UseCases.AI.GenerateCommitMessageUseCase _generateCommitMessageUseCase;

    public ChangesViewModel(
        LoadChangesUseCase loadChangesUseCase,
        CommitChangesUseCase commitChangesUseCase,
        DiscardChangesUseCase discardChangesUseCase,
        StashChangesUseCase stashChangesUseCase,
        StashPopUseCase stashPopUseCase,
        StashDropUseCase stashDropUseCase,
        StashClearUseCase stashClearUseCase,
        GetFileDiffUseCase getFileDiffUseCase,
        Domain.Interfaces.IGitRepository gitRepository,
        Domain.Interfaces.IGitAuthProviderFactory authFactory,
        Domain.Interfaces.ICredentialStorageService credentialStorage,
        PushChangesUseCase pushChangesUseCase,
        Chapi.Application.UseCases.AI.GenerateCommitMessageUseCase generateCommitMessageUseCase)
    {
        _loadChangesUseCase = loadChangesUseCase;
        _commitChangesUseCase = commitChangesUseCase;
        _discardChangesUseCase = discardChangesUseCase;
        _stashChangesUseCase = stashChangesUseCase;
        _stashPopUseCase = stashPopUseCase;
        _stashDropUseCase = stashDropUseCase;
        _stashClearUseCase = stashClearUseCase;
        _getFileDiffUseCase = getFileDiffUseCase;
        _gitRepository = gitRepository;
        _authFactory = authFactory;
        _credentialStorage = credentialStorage;
        _pushChangesUseCase = pushChangesUseCase;
        _generateCommitMessageUseCase = generateCommitMessageUseCase;

        // Inicializar watcher y caché (como GitHub Desktop)
        _changeWatcher = new Chapi.Infrastructure.Git.GitChangeWatcher();
        _changesCache = new Chapi.Infrastructure.Git.GitChangesCache();

        // Suscribirse a cambios del repositorio
        _changeWatcher.RepositoryChanged += OnRepositoryChanged;

        Changes = new ObservableCollection<ChangeItemViewModel>();
        Stashes = new ObservableCollection<GitStash>();
        StashedFiles = new ObservableCollection<ChangeItemViewModel>();
        DiffLines = new ObservableCollection<DiffPiece>();

        LoadChangesCommand = new AsyncRelayCommand(async _ => await LoadChangesAsync());
        CommitCommand = new AsyncRelayCommand(async _ => await CommitAsync(), _ => CanCommit());
        SelectAllCommand = new RelayCommand(_ => SelectAll());
        DeselectAllCommand = new RelayCommand(_ => DeselectAll());

        DiscardCommand = new AsyncRelayCommand(async param => await DiscardAsync(param as ChangeItemViewModel));
        StashSelectedCommand = new AsyncRelayCommand(async _ => await StashSelectedAsync());
        PopStashCommand = new AsyncRelayCommand(async param => await PopStashAsync(param as GitStash));
        DropStashCommand = new AsyncRelayCommand(async param => await DropStashAsync(param as GitStash));
        ClearStashesCommand = new AsyncRelayCommand(async _ => await ClearStashesAsync());
        RestoreFileFromStashCommand = new AsyncRelayCommand(async param => await RestoreFileFromStashAsync(param as ChangeItemViewModel));
        GenerateCommitMessageCommand = new AsyncRelayCommand(async _ => await GenerateCommitMessageAsync());
        DiscardAllCommand = new AsyncRelayCommand(async _ => await DiscardAllAsync());

        // Suscribirse al evento de actualización de avatares
        Chapi.Domain.Services.AvatarCacheService.Instance.AvatarUpdated += OnAvatarUpdated;
    }

    private void OnRepositoryChanged(object? sender, string projectPath)
    {
        // Solo recargar si es el proyecto actual
        if (projectPath == ProjectPath)
        {
            _changesCache.Invalidate(projectPath);

            // Ejecutar en el UI thread para evitar errores de cross-thread
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
            {
                await LoadChangesAsync();
                await LoadMetadataAsync();
            });
        }
    }

    private void OnAvatarUpdated(object sender, Chapi.Domain.Services.AvatarUpdatedEventArgs e)
    {
        // Forzar actualización del DisplayUserName para que el binding se refresque
        OnPropertyChanged(nameof(DisplayUserName));
    }

    /// <summary>
    /// Fuerza la recarga de cambios, invalidando la caché interna.
    /// Útil cuando ocurren cambios externos (como un Undo Commit) que el watcher podría no detectar a tiempo.
    /// </summary>
    public async Task ForceRefreshAsync()
    {
        if (string.IsNullOrEmpty(ProjectPath)) return;

        _changesCache.Invalidate(ProjectPath);
        await LoadChangesAsync();
        await LoadMetadataAsync();
    }

    private async Task GenerateCommitMessageAsync()
    {
        if (string.IsNullOrEmpty(ProjectPath)) return;
        var selectedFiles = Changes.Where(c => c.IsSelected).Select(c => c.FilePath).ToList();
        if (!selectedFiles.Any()) return;

        IsGenerating = true;
        try
        {
            // Obtener diff consolidado
            var diffBuilder = new System.Text.StringBuilder();
            foreach (var file in selectedFiles)
            {
                // TODO: Obtener solo staged changes si es posible, o diff local
                var diff = await _gitRepository.GetDiffAsync(ProjectPath, file);
                diffBuilder.AppendLine(diff);
            }

            var fullDiff = diffBuilder.ToString();
            if (string.IsNullOrWhiteSpace(fullDiff)) return;

            // Llamar a IA usando Use Case
            var result = await _generateCommitMessageUseCase.ExecuteAsync(fullDiff);

            if (result.IsSuccess)
            {
                var jsonResponse = result.Data;
                try
                {
                    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var commitMsg = System.Text.Json.JsonSerializer.Deserialize<Chapi.Domain.Entities.CommitMessageResponse>(jsonResponse, options);
                    if (commitMsg != null)
                    {
                        CommitSummary = commitMsg.Summary;
                        CommitDescription = commitMsg.Description;
                    }
                }
                catch
                {
                    // Fallback si no es JSON válido
                    CommitSummary = jsonResponse;
                    CommitDescription = string.Empty;
                }
            }
            else
            {
                // Manejar error (opcional: mostrar en UI)
                // CommitSummary = "Error generando mensaje";
            }
        }
        finally
        {
            IsGenerating = false;
        }
    }

    #region Properties

    /// <summary>
    /// Coleccion de cambios en el repositorio.
    /// </summary>
    public ObservableCollection<ChangeItemViewModel> Changes { get; }

    /// <summary>
    /// Ruta del proyecto actual.
    /// </summary>
    public string ProjectPath
    {
        get => _projectPath;
        set
        {
            if (SetProperty(ref _projectPath, value))
            {
                CommitSummary = string.Empty;
                CommitDescription = string.Empty;

                // Iniciar monitoreo del nuevo proyecto (como GitHub Desktop)
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _changeWatcher.WatchRepository(value);
                }

                _ = LoadChangesAsync();
            }
        }
    }

    private async Task LoadMetadataAsync()
    {
        await LoadProfileAsync();
        await LoadStashesAsync();
    }

    private async Task LoadProfileAsync()
    {
        await LoadAuthStatusAsync();
        await LoadGitUserEmailAsync();
    }

    /// <summary>
    /// Total de lineas anadidas.
    /// </summary>
    public int TotalAdditions
    {
        get => _totalAdditions;
        private set => SetProperty(ref _totalAdditions, value);
    }

    /// <summary>
    /// Total de lineas eliminadas.
    /// </summary>
    public int TotalDeletions
    {
        get => _totalDeletions;
        private set => SetProperty(ref _totalDeletions, value);
    }

    public bool IsSyncing
    {
        get => _isSyncing;
        set => SetProperty(ref _isSyncing, value);
    }

    /// <summary>
    /// Indica si todos los cambios estan seleccionados.
    /// </summary>
    public bool AreAllSelected
    {
        get => Changes.Any() && Changes.All(c => c.IsSelected);
        set
        {
            if (value)
                SelectAll();
            else
                DeselectAll();

            OnPropertyChanged();
        }
    }

    public int SelectedCount => Changes.Count(c => c.IsSelected);

    /// <summary>
    /// Resumen del commit.
    /// </summary>
    public string CommitSummary
    {
        get => _commitSummary;
        set
        {
            if (SetProperty(ref _commitSummary, value))
            {
                (CommitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Descripcion detallada del commit.
    /// </summary>
    public string CommitDescription
    {
        get => _commitDescription;
        set => SetProperty(ref _commitDescription, value);
    }

    /// <summary>
    /// Coleccion de stashes.
    /// </summary>
    /// <summary>
    /// Coleccion de stashes.
    /// </summary>
    public ObservableCollection<GitStash> Stashes { get; }

    /// <summary>
    /// Lineas de diferencia del archivo seleccionado.
    /// </summary>
    public ObservableCollection<DiffPiece> DiffLines { get; }

    /// <summary>
    /// Cambio seleccionado actualmente.
    /// </summary>
    public ChangeItemViewModel? SelectedChange
    {
        get => _selectedChange;
        set
        {
            if (SetProperty(ref _selectedChange, value))
            {
                _ = LoadDiffAsync();
            }
        }
    }

    /// <summary>
    /// Indica si la vista de stash esta visible.
    /// </summary>
    public bool IsStashViewVisible
    {
        get => _isStashViewVisible;
        set => SetProperty(ref _isStashViewVisible, value);
    }

    public bool HasStashes => Stashes.Any();

    /// <summary>
    /// Stash seleccionado actualmente.
    /// </summary>
    /// <summary>
    /// Stash seleccionado actualmente.
    /// </summary>
    public GitStash? SelectedStash
    {
        get => _selectedStash;
        set
        {
            if (SetProperty(ref _selectedStash, value))
            {
                _ = LoadStashedFilesAsync();
            }
        }
    }

    /// <summary>
    /// Coleccion de archivos contenidos en el stash seleccionado.
    /// </summary>
    public ObservableCollection<ChangeItemViewModel> StashedFiles { get; }

    /// <summary>
    /// Archivo seleccionado dentro de un stash.
    /// </summary>
    public ChangeItemViewModel? SelectedStashedFile
    {
        get => _selectedStashedFile;
        set
        {
            if (SetProperty(ref _selectedStashedFile, value))
            {
                _ = LoadStashedFileDiffAsync();
            }
        }
    }

    /// <summary>
    /// Indica si se esta generando un mensaje de commit con IA.
    /// </summary>
    public bool IsGenerating
    {
        get => _isGenerating;
        set => SetProperty(ref _isGenerating, value);
    }

    // Auth Properties
    private string _authenticatedUserName;
    private Chapi.Domain.Enums.GitProvider _authenticatedProvider;
    private bool _isAuthenticated;
    private string _gitUserEmail = string.Empty;

    public string AuthenticatedUserName
    {
        get => _authenticatedUserName;
        set
        {
            if (SetProperty(ref _authenticatedUserName, value))
            {
                OnPropertyChanged(nameof(DisplayUserName));
            }
        }
    }

    public Chapi.Domain.Enums.GitProvider AuthenticatedProvider
    {
        get => _authenticatedProvider;
        set => SetProperty(ref _authenticatedProvider, value);
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        set => SetProperty(ref _isAuthenticated, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private bool _isUserLoggedIn;
    public bool IsUserLoggedIn
    {
        get => _isUserLoggedIn;
        set
        {
            SetProperty(ref _isUserLoggedIn, value);
            OnPropertyChanged(nameof(ProviderColor));
            OnPropertyChanged(nameof(DisplayUserName));
        }
    }

    public MaterialDesignThemes.Wpf.PackIconKind ProviderIcon => AuthenticatedProvider switch
    {
        Chapi.Domain.Enums.GitProvider.GitHub => MaterialDesignThemes.Wpf.PackIconKind.Github,
        Chapi.Domain.Enums.GitProvider.GitLab => MaterialDesignThemes.Wpf.PackIconKind.Gitlab,
        _ => MaterialDesignThemes.Wpf.PackIconKind.AccountCircle
    };

    public System.Windows.Media.Brush ProviderColor => IsUserLoggedIn ? (AuthenticatedProvider switch
    {
        Chapi.Domain.Enums.GitProvider.GitHub => System.Windows.Media.Brushes.White,
        Chapi.Domain.Enums.GitProvider.GitLab => System.Windows.Media.Brushes.Orange,
        _ => System.Windows.Media.Brushes.Gray
    }) : System.Windows.Media.Brushes.Gray;

    public string GitUserEmail
    {
        get => _gitUserEmail;
        set => SetProperty(ref _gitUserEmail, value);
    }

    private string _gitUserName = string.Empty;
    public string GitUserName
    {
        get => _gitUserName;
        set
        {
            if (SetProperty(ref _gitUserName, value))
            {
                OnPropertyChanged(nameof(DisplayUserName));
            }
        }
    }

    /// <summary>
    /// Retorna el username para mostrar en el avatar
    /// Solo retorna username si está autenticado Y el provider coincide con el del proyecto
    /// </summary>
    public string DisplayUserName
    {
        get
        {
            // Solo mostrar username si:
            // 1. Está logueado
            // 2. El provider del proyecto coincide con el provider autenticado
            // 3. Tiene un username válido

            if (!IsUserLoggedIn ||
                AuthenticatedProvider == Chapi.Domain.Enums.GitProvider.Unknown ||
                string.IsNullOrWhiteSpace(AuthenticatedUserName))
            {
                // No está logueado o no hay provider válido
                // Para GitHub, podemos usar GitUserName como fallback
                // Para GitLab, NO usamos fallback (requiere username real sin espacios)
                return string.Empty;
            }

            // Retornar el username autenticado (que coincide con el provider del proyecto)
            return AuthenticatedUserName;
        }
    }

    public ICommand ConnectAccountCommand => new AsyncRelayCommand(async _ => await ConnectAccountAsync());

    #endregion

    #region Commands

    public AsyncRelayCommand LoadChangesCommand { get; }
    public AsyncRelayCommand CommitCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand DeselectAllCommand { get; }
    public AsyncRelayCommand DiscardCommand { get; }
    public AsyncRelayCommand StashSelectedCommand { get; }
    public AsyncRelayCommand PopStashCommand { get; }
    public AsyncRelayCommand DropStashCommand { get; }
    public AsyncRelayCommand ClearStashesCommand { get; }
    public AsyncRelayCommand RestoreFileFromStashCommand { get; }
    public AsyncRelayCommand GenerateCommitMessageCommand { get; }
    public AsyncRelayCommand DiscardAllCommand { get; }

    #endregion

    #region Methods

    /// <summary>
    /// Carga los cambios del repositorio.
    /// </summary>
    public async Task LoadChangesAsync()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath))
            return;
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsSyncing = true;

        // Resetear totales para evitar que se "peguen" de otros proyectos
        Changes.Clear();
        TotalAdditions = 0;
        TotalDeletions = 0;

        try
        {
            if (_changesCache.TryGetChanges(ProjectPath, out var cachedChanges, out var cachedAdditions, out var cachedDeletions))
            {

                // Usar datos del caché
                foreach (var fileChange in cachedChanges)
                {
                    var viewModel = MapToViewModel(fileChange);
                    viewModel.PropertyChanged += (s, e) =>
                    {
                        if (_isMassUpdating) return;

                        if (e.PropertyName == nameof(ChangeItemViewModel.IsSelected))
                        {
                            OnPropertyChanged(nameof(AreAllSelected));
                            OnPropertyChanged(nameof(SelectedCount));
                            (CommitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                        }
                    };
                    Changes.Add(viewModel);
                }

                TotalAdditions = cachedAdditions;
                TotalDeletions = cachedDeletions;
                OnPropertyChanged(nameof(AreAllSelected));
                OnPropertyChanged(nameof(SelectedCount));

                IsSyncing = false;

                // Cargar otras cosas en background
                _ = LoadStashesAsync();
                _ = LoadAuthStatusAsync();
                _ = LoadGitUserEmailAsync();

                return;
            }

            // Usar el Use Case para obtener cambios (ahora es MUCHO más rápido)
            var fileChanges = await _loadChangesUseCase.ExecuteAsync(ProjectPath);

            // Si se canceló durante la espera (ej: cambiamos de proyecto otra vez), salir
            if (token.IsCancellationRequested) return;

            // ⚡ OPTIMIZACIÓN: Añadir cambios por lotes para evitar saturar el hilo de UI
            var viewModels = fileChanges.Select(fileChange => 
            {
                var vm = MapToViewModel(fileChange);
                vm.PropertyChanged += (s, e) =>
                {
                    if (_isMassUpdating) return;
                    if (e.PropertyName == nameof(ChangeItemViewModel.IsSelected))
                    {
                        OnPropertyChanged(nameof(AreAllSelected));
                        OnPropertyChanged(nameof(SelectedCount));
                        (CommitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                    }
                };
                return vm;
            }).ToList();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var vm in viewModels)
                {
                    Changes.Add(vm);
                }
                OnPropertyChanged(nameof(AreAllSelected));
                OnPropertyChanged(nameof(SelectedCount));
                IsLoading = false;
            });

            // Cargar stats en background solo si faltan (en WSL ya vienen incluidos)
            _ = LoadFileStatsInBackgroundAsync(token);

            // ⚡ OPTIMIZACIÓN: Cargar metadatos secundarios en paralelo
            _ = Task.Run(async () =>
            {
                try
                {
                    await LoadMetadataAsync();
                }
                catch { }
            }, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
        }
        finally
        {
            if (_loadCts?.Token == token)
            {
                _loadCts = null;
                IsSyncing = false;
            }
        }
    }

    /// <summary>
    /// Carga las estadísticas de archivos en background sin bloquear la UI.
    /// Esto permite mostrar la lista rápidamente y luego actualizar los números.
    /// </summary>
    private async Task LoadFileStatsInBackgroundAsync(CancellationToken token)
    {
        try
        {
            // Variables locales para acumular (evita race conditions)
            int totalAdd = 0;
            int totalDel = 0;

            // Cargar stats de los primeros 20 archivos visibles primero (prioridad)
            var visibleFiles = Changes.Take(20).ToList();

            foreach (var file in visibleFiles)
            {
                if (token.IsCancellationRequested) return;

                try
                {
                    // ⚡ OPTIMIZACIÓN: Si ya tenemos stats (común en WSL), no llamar al proceso Git
                    if (file.Additions > 0 || file.Deletions > 0) continue;

                    var stats = await _gitRepository.GetFileStatsAsync(ProjectPath, file.FilePath);
                    file.Additions = stats.additions;
                    file.Deletions = stats.deletions;

                    // Acumular en variables locales
                    totalAdd += stats.additions;
                    totalDel += stats.deletions;
                }
                catch { }
            }

            // Actualizar totales después de los primeros 20
            TotalAdditions = totalAdd;
            TotalDeletions = totalDel;

            // Luego cargar el resto en background
            var remainingFiles = Changes.Skip(20).ToList();
            foreach (var file in remainingFiles)
            {
                if (token.IsCancellationRequested) return;

                try
                {
                    var stats = await _gitRepository.GetFileStatsAsync(ProjectPath, file.FilePath);
                    file.Additions = stats.additions;
                    file.Deletions = stats.deletions;

                    // Actualizar totales
                    TotalAdditions += stats.additions;
                    TotalDeletions += stats.deletions;
                }
                catch { }
            }

            // 💾 Guardar en caché para la próxima vez (como GitHub Desktop)
            if (!token.IsCancellationRequested)
            {
                var allChanges = Changes.Select(c => new FileChange
                {
                    FilePath = c.FilePath,
                    Status = MapStatusFromViewModel(c.ShortStatus),
                    Additions = c.Additions,
                    Deletions = c.Deletions
                }).ToList();

                _changesCache.SetChanges(ProjectPath, allChanges, TotalAdditions, TotalDeletions);
            }
        }
        catch (Exception ex)
        {
        }
    }

    private ChangeStatus MapStatusFromViewModel(string shortStatus)
    {
        return shortStatus switch
        {
            "A" => ChangeStatus.Added,
            "M" => ChangeStatus.Modified,
            "D" => ChangeStatus.Deleted,
            "R" => ChangeStatus.Renamed,
            "?" => ChangeStatus.Untracked,
            _ => ChangeStatus.Modified
        };
    }

    private async Task LoadAuthStatusAsync()
    {
        IsAuthenticated = false;
        AuthenticatedUserName = string.Empty;
        AuthenticatedProvider = Chapi.Domain.Enums.GitProvider.Unknown;
        IsUserLoggedIn = false;

        if (string.IsNullOrEmpty(ProjectPath)) return;

        try
        {
            var remoteUrl = await _gitRepository.GetRemoteUrlAsync(ProjectPath);
            if (string.IsNullOrEmpty(remoteUrl)) return;

            Chapi.Domain.Enums.GitProvider provider = Chapi.Domain.Enums.GitProvider.Unknown;
            if (remoteUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase)) provider = Chapi.Domain.Enums.GitProvider.GitHub;
            else if (remoteUrl.Contains("gitlab.com", StringComparison.OrdinalIgnoreCase)) provider = Chapi.Domain.Enums.GitProvider.GitLab;

            if (provider == Chapi.Domain.Enums.GitProvider.Unknown) return;

            AuthenticatedProvider = provider;
            IsAuthenticated = true;

            var cred = await _credentialStorage.GetCredentialAsync(provider.ToString());

            if (cred.HasValue)
            {
                AuthenticatedUserName = cred.Value.username;
                IsUserLoggedIn = true;
            }
            else
            {
                AuthenticatedUserName = "Conectar";
                IsUserLoggedIn = false;
            }

            OnPropertyChanged(nameof(ProviderIcon));
            OnPropertyChanged(nameof(ProviderColor));

            // Pre-cargar el avatar de GitLab si está autenticado
            if (IsUserLoggedIn &&
                AuthenticatedProvider == Chapi.Domain.Enums.GitProvider.GitLab &&
                !string.IsNullOrWhiteSpace(AuthenticatedUserName) &&
                AuthenticatedUserName != "Conectar")
            {
                _ = Task.Run(async () =>
                 {
                     await Chapi.Domain.Services.AvatarCacheService.Instance.GetGitLabAvatarUrlAsync(AuthenticatedUserName);
                 });
            }
        }
        catch (Exception ex)
        {
        }
    }

    private async Task ConnectAccountAsync()
    {
        if (AuthenticatedProvider == Chapi.Domain.Enums.GitProvider.Unknown) return;

        // Si ya está logueado, abrir configuración
        if (IsUserLoggedIn)
        {
            // Leer configuración actual de default branch
            var defaultBranch = await _gitRepository.GetConfigAsync(ProjectPath, "init.defaultBranch", isGlobal: true);
            if (string.IsNullOrWhiteSpace(defaultBranch))
            {
                defaultBranch = "main";
            }

            // Obtener avatar del usuario
            var avatarUrl = AuthenticatedProvider == Chapi.Domain.Enums.GitProvider.GitHub
                ? Chapi.Domain.Services.AvatarCacheService.Instance.GetGitHubAvatarUrl(AuthenticatedUserName)
                : Chapi.Domain.Services.AvatarCacheService.Instance.GetGitLabAvatarUrl(AuthenticatedUserName);

            System.Windows.Media.Imaging.BitmapImage avatarImage = null;
            try
            {
                avatarImage = new System.Windows.Media.Imaging.BitmapImage();
                avatarImage.BeginInit();
                avatarImage.UriSource = new Uri(avatarUrl);
                avatarImage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                avatarImage.EndInit();
            }
            catch { }

            // Crear y configurar el diálogo
            var dialog = new Chapi.Presentation.Views.Dialogs.GitConfigDialog
            {
                // Git configuration
                UserName = GitUserName,
                UserEmail = GitUserEmail,
                DefaultBranch = defaultBranch,

                // Account information
                AccountDisplayName = GitUserName,
                AccountUserName = AuthenticatedUserName,
                Provider = AuthenticatedProvider.ToString(),
                AvatarImage = avatarImage
            };

            await DialogHost.Show(dialog, "RootDialog");

            // Manejar sign out
            if (dialog.SignedOut)
            {
                // Cerrar sesión
                await _credentialStorage.DeleteCredentialAsync(AuthenticatedProvider.ToString());

                // Limpiar caché de avatares del usuario
                Chapi.Domain.Services.AvatarCacheService.Instance.ClearUserCache(
                    AuthenticatedProvider.ToString(),
                    AuthenticatedUserName
                );

                // Recargar estado
                await LoadAuthStatusAsync();
                return;
            }

            // Si el usuario guardó cambios en Git config, actualizar la configuración
            if (dialog.WasSaved)
            {
                try
                {
                    // Guardar nombre
                    if (!string.IsNullOrWhiteSpace(dialog.UserName))
                    {
                        await _gitRepository.SetConfigAsync(ProjectPath, "user.name", dialog.UserName, isGlobal: true);
                    }

                    // Guardar email
                    if (!string.IsNullOrWhiteSpace(dialog.UserEmail))
                    {
                        await _gitRepository.SetConfigAsync(ProjectPath, "user.email", dialog.UserEmail, isGlobal: true);
                    }

                    // Guardar default branch
                    if (!string.IsNullOrWhiteSpace(dialog.DefaultBranch))
                    {
                        await _gitRepository.SetConfigAsync(ProjectPath, "init.defaultBranch", dialog.DefaultBranch, isGlobal: true);
                    }

                    // Recargar configuración
                    await LoadGitUserEmailAsync();
                }
                catch (Exception ex)
                {
                    await DialogService.ShowConfirmDialog("Error", $"No se pudo guardar la configuración: {ex.Message}", DialogVariant.Error, DialogType.Info);
                }
            }

            return;
        }

        // Si no está logueado, iniciar proceso de autenticación
        IsLoading = true;
        try
        {
            // Usamos la factoría de proveedores para obtener el flujo de navegador (GitHub o GitLab)
            var provider = _authFactory.GetProvider(AuthenticatedProvider);
            var result = await provider.AuthenticateAsync();

            if (result.IsSuccess)
            {
                // Recargar el estado para mostrar el usuario logueado
                await LoadAuthStatusAsync();

                // Pre-cargar el avatar para evitar "vibración" al cambiar de proyecto
                if (AuthenticatedProvider == Chapi.Domain.Enums.GitProvider.GitLab &&
                    !string.IsNullOrWhiteSpace(AuthenticatedUserName))
                {
                    _ = Task.Run(async () =>
                    {
                        await Chapi.Domain.Services.AvatarCacheService.Instance.GetGitLabAvatarUrlAsync(AuthenticatedUserName);
                    });
                }
            }
            else if (result.Error != "Autenticación cancelada")
            {
                await DialogService.ShowConfirmDialog("Error de Conexión", result.Error, DialogVariant.Error, DialogType.Info);
            }
        }
        catch (Exception ex)
        {
            await DialogService.ShowConfirmDialog("Error", ex.Message, DialogVariant.Error, DialogType.Info);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadGitUserEmailAsync()
    {
        if (string.IsNullOrEmpty(ProjectPath)) return;

        try
        {
            // Obtener el email y nombre del usuario de Git configurado globalmente
            var email = await _gitRepository.GetConfigAsync(ProjectPath, "user.email", isGlobal: true);
            var name = await _gitRepository.GetConfigAsync(ProjectPath, "user.name", isGlobal: true);

            GitUserEmail = email ?? string.Empty;
            GitUserName = name ?? string.Empty;

        }
        catch (Exception ex)
        {
            GitUserEmail = string.Empty;
            GitUserName = string.Empty;
        }
    }

    /// <summary>
    /// Carga la lista de stashes.
    /// </summary>
    private async Task LoadStashesAsync()
    {
        if (string.IsNullOrEmpty(ProjectPath)) return;

        try
        {
            var stashes = await _gitRepository.ListStashesAsync(ProjectPath);
            var currentBranch = await _gitRepository.GetCurrentBranchAsync(ProjectPath);

            var filteredStashes = stashes.Where(stash =>
                string.IsNullOrEmpty(currentBranch) ||
                stash.Message.Contains($"on {currentBranch}", StringComparison.OrdinalIgnoreCase)).ToList();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Stashes.Clear();
                foreach (var stash in filteredStashes) Stashes.Add(stash);
                OnPropertyChanged(nameof(HasStashes));
            });
        }
        catch (Exception)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => Stashes.Clear());
        }
        finally
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => OnPropertyChanged(nameof(HasStashes)));
        }
    }

    /// <summary>
    /// Carga los archivos contenidos en el stash seleccionado.
    /// </summary>
    public async Task LoadStashedFilesAsync()
    {
        StashedFiles.Clear();
        if (SelectedStash == null || string.IsNullOrEmpty(ProjectPath)) return;

        try
        {
            var fileStatuses = await _gitRepository.GetFileStatusesForStashAsync(ProjectPath, SelectedStash.Name);
            var viewModels = new List<ChangeItemViewModel>();

            foreach (var kvp in fileStatuses)
            {
                var changeStatus = kvp.Value switch
                {
                    'A' => ChangeStatus.Added,
                    'M' => ChangeStatus.Modified,
                    'D' => ChangeStatus.Deleted,
                    'R' => ChangeStatus.Renamed,
                    '?' => ChangeStatus.Untracked,
                    _ => ChangeStatus.Modified
                };

                var viewModel = new ChangeItemViewModel
                {
                    FilePath = kvp.Key,
                    Status = GetStatusText(changeStatus),
                    ShortStatus = GetShortStatus(changeStatus),
                    IsSelected = false
                };

                (viewModel.Icon, viewModel.Color) = GetIconAndColor(changeStatus);
                viewModels.Add(viewModel);
            }

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                StashedFiles.Clear();
                foreach (var vm in viewModels) StashedFiles.Add(vm);
            });
        }
        catch (Exception) { }
    }

    /// <summary>
    /// Carga el diff del archivo seleccionado usando DiffPlex.
    /// </summary>
    public async Task LoadDiffAsync()
    {
        DiffLines.Clear();
        if (SelectedChange == null || string.IsNullOrEmpty(ProjectPath)) return;

        try
        {
            string oldText = string.Empty;
            string newText = string.Empty;

            // 1. Obtener contenido antiguo (HEAD)
            // Si el archivo es Nuevo, oldText debe ser vacio.
            if (SelectedChange.ShortStatus != "A" && SelectedChange.ShortStatus != "?")
            {
                try { oldText = await _gitRepository.GetFileContentAsync(ProjectPath, "HEAD", SelectedChange.FilePath); } catch { }
            }

            // 2. Obtener contenido nuevo (File System)
            // Si el archivo es Borrado, newText debe ser vacio.
            if (SelectedChange.ShortStatus != "D")
            {
                string fullPath = Path.Combine(ProjectPath, SelectedChange.FilePath);
                if (File.Exists(fullPath))
                {
                    newText = await File.ReadAllTextAsync(fullPath);
                }
            }

            // 3. Generar Diff
            GenerateDiff(oldText, newText);
        }
        catch (Exception ex)
        {
        }
    }

    /// <summary>
    /// Carga el diff de un archivo dentro de un stash usando DiffPlex.
    /// </summary>
    public async Task LoadStashedFileDiffAsync()
    {
        DiffLines.Clear();
        if (SelectedStash == null || SelectedStashedFile == null || string.IsNullOrEmpty(ProjectPath)) return;

        try
        {
            string newText = await _gitRepository.GetFileContentAsync(ProjectPath, SelectedStash.Name, SelectedStashedFile.FilePath);
            string oldText = string.Empty;
            // Intentar leer pariente
            try { oldText = await _gitRepository.GetFileContentAsync(ProjectPath, $"{SelectedStash.Name}^1", SelectedStashedFile.FilePath); } catch { }

            GenerateDiff(oldText, newText);
        }
        catch (Exception ex)
        {
        }
    }

    private void GenerateDiff(string oldText, string newText)
    {
        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(oldText, newText);

        var filteredLines = FilterHunks(diff.Lines);
        foreach (var line in filteredLines)
        {
            DiffLines.Add(line);
        }
    }

    private List<DiffPiece> FilterHunks(IList<DiffPiece> lines)
    {
        var filteredLines = new List<DiffPiece>();
        const int contextLines = 3;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Type == ChangeType.Unchanged)
            {
                bool isContext = false;
                for (int j = 1; j <= contextLines; j++)
                {
                    if (i - j >= 0 && lines[i - j].Type != ChangeType.Unchanged) { isContext = true; break; }
                }
                if (!isContext)
                {
                    for (int j = 1; j <= contextLines; j++)
                    {
                        if (i + j < lines.Count && lines[i + j].Type != ChangeType.Unchanged) { isContext = true; break; }
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
        return filteredLines;
    }

    /// <summary>
    /// Realiza un commit con los archivos seleccionados.
    /// </summary>
    private async Task CommitAsync()
    {
        var selectedFiles = Changes.Where(c => c.IsSelected).Select(c => c.FilePath);

        if (!selectedFiles.Any())
            return;

        string message = CommitSummary;
        if (!string.IsNullOrWhiteSpace(CommitDescription))
        {
            message += $"\n\n{CommitDescription}";
        }

        var request = new CommitRequest
        {
            ProjectPath = ProjectPath,
            Message = message,
            Files = selectedFiles
        };

        var result = await _commitChangesUseCase.ExecuteAsync(request);

        if (result.IsSuccess)
        {
            CommitSummary = string.Empty;
            CommitDescription = string.Empty;

            // Invalidar caché para forzar recarga desde Git
            _changesCache.Invalidate(ProjectPath);

            await LoadChangesAsync();

            // Notificar que se completo el commit para que el historial se actualice
            CommitCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Determina si se puede hacer commit.
    /// </summary>
    private bool CanCommit()
    {
        return !string.IsNullOrWhiteSpace(CommitSummary) &&
               Changes.Any(c => c.IsSelected);
    }

    private async Task DiscardAsync(ChangeItemViewModel? item)
    {
        if (item == null || string.IsNullOrEmpty(ProjectPath)) return;

        var confirmed = await DialogService.ShowConfirmDialog(
            "Descartar Cambios",
            $"¿Estás seguro de que deseas descartar los cambios en '{item.FileName}'? Esta acción no se puede deshacer.",
            DialogVariant.Warning);

        if (!confirmed) return;

        var result = await _discardChangesUseCase.ExecuteAsync(ProjectPath, new[] { item.FilePath });
        if (result.IsSuccess)
        {
            _changesCache.Invalidate(ProjectPath);
            await LoadChangesAsync();
        }
    }

    private async Task DiscardAllAsync()
    {
        if (string.IsNullOrEmpty(ProjectPath) || !Changes.Any()) return;

        var confirmed = await DialogService.ShowConfirmDialog(
            "Descartar TODOS los Cambios",
            "¿Estás seguro de que deseas descartar TODOS los cambios locales? Esta acción eliminará permanentemente tus modificaciones.",
            DialogVariant.Warning);

        if (!confirmed) return;

        var allFiles = Changes.Select(c => c.FilePath).ToArray();
        var result = await _discardChangesUseCase.ExecuteAsync(ProjectPath, allFiles);
        if (result.IsSuccess)
        {
            _changesCache.Invalidate(ProjectPath);
            await LoadChangesAsync();
        }
    }

    private async Task StashSelectedAsync()
    {
        if (string.IsNullOrEmpty(ProjectPath)) return;

        List<string> filesToStash;
        string message;

        filesToStash = Changes.Where(c => c.IsSelected).Select(c => c.FilePath).ToList();
        message = "Stash manual";

        if (!filesToStash.Any()) return;

        var confirmed = await DialogService.ShowConfirmDialog(
            "Guardar en Stash",
            filesToStash.Count == 1
                ? $"¿Deseas guardar '{System.IO.Path.GetFileName(filesToStash[0])}' en el stash?"
                : $"¿Deseas guardar estos {filesToStash.Count} archivos en el stash?",
            DialogVariant.Info);

        if (!confirmed) return;

        var result = await _stashChangesUseCase.ExecuteAsync(ProjectPath, message, filesToStash);
        if (result.IsSuccess)
        {
            _changesCache.Invalidate(ProjectPath);
            await LoadChangesAsync();
        }
    }

    private async Task PopStashAsync(GitStash? stash)
    {
        if (stash == null || string.IsNullOrEmpty(ProjectPath)) return;

        // Extraer indice del nombre "stash@{n}"
        int index = 0;
        var match = System.Text.RegularExpressions.Regex.Match(stash.Name, @"\{(\d+)\}");
        if (match.Success) index = int.Parse(match.Groups[1].Value);

        var result = await _stashPopUseCase.ExecuteAsync(ProjectPath, index);
        if (result.IsSuccess)
        {
            _changesCache.Invalidate(ProjectPath);
            await LoadChangesAsync();
        }
        else
        {
            await DialogService.ShowConfirmDialog("Error en Stash",
                $"No se pudo aplicar el stash: {result.Error}\n\nEs posible que existan conflictos con tus cambios actuales.",
                Chapi.Presentation.Views.Dialogs.DialogVariant.Error, DialogType.Info);
        }
    }

    private async Task RestoreFileFromStashAsync(ChangeItemViewModel? item)
    {
        if (item == null || SelectedStash == null || string.IsNullOrEmpty(ProjectPath)) return;

        try
        {
            // git checkout stash@{n} -- <filepath>
            // git checkout stash@{n} -- <filepath>
            var result = await _gitRepository.RestoreFileFromStashAsync(ProjectPath, SelectedStash.Name, item.FilePath);
            if (!result.IsSuccess)
            {
                await DialogService.ShowConfirmDialog("Error al restaurar archivo",
                    $"No se pudo restaurar el archivo '{item.FileName}': {result.Error}",
                    DialogVariant.Error, DialogType.Info);
                return;
            }

            _changesCache.Invalidate(ProjectPath);
            await LoadChangesAsync();
            IsStashViewVisible = false;
        }
        catch (Exception ex)
        {
            await DialogService.ShowConfirmDialog("Error al restaurar archivo",
                $"No se pudo restaurar el archivo '{item.FileName}': {ex.Message}",
                DialogVariant.Error, DialogType.Info);
        }
    }

    private async Task DropStashAsync(GitStash? stash)
    {
        if (stash == null || string.IsNullOrEmpty(ProjectPath)) return;

        var confirmed = await DialogService.ShowConfirmDialog(
            "Eliminar Stash",
            $"Â¿Estas seguro de eliminar el stash?\n\n'{stash.Message}'\n\nEsta accion es irreversible.",
            DialogVariant.Warning,
            DialogType.Confirm);

        if (!confirmed) return;

        int index = 0;
        var match = System.Text.RegularExpressions.Regex.Match(stash.Name, @"\{(\d+)\}");
        if (match.Success) index = int.Parse(match.Groups[1].Value);

        var result = await _stashDropUseCase.ExecuteAsync(ProjectPath, index);
        if (result.IsSuccess)
        {
            _changesCache.Invalidate(ProjectPath);
            await LoadChangesAsync();
        }
    }

    private async Task ClearStashesAsync()
    {
        if (string.IsNullOrEmpty(ProjectPath)) return;

        var confirmed = await DialogService.ShowConfirmDialog(
            "Limpiar Stashes",
            "Â¿Estas seguro de que deseas eliminar TODOS los stashes?\n\nEsta accion borrara permanentemente todas las entradas guardadas.",
            DialogVariant.Warning,
            DialogType.Confirm);

        if (!confirmed) return;

        var result = await _stashClearUseCase.ExecuteAsync(ProjectPath);
        if (result.IsSuccess)
        {
            _changesCache.Invalidate(ProjectPath);
            await LoadChangesAsync();
        }
    }




    /// <summary>
    /// Selecciona todos los cambios.
    /// </summary>
    private void SelectAll()
    {
        _isMassUpdating = true;
        try
        {
            foreach (var change in Changes) change.IsSelected = true;
        }
        finally
        {
            _isMassUpdating = false;
            OnPropertyChanged(nameof(AreAllSelected));
            OnPropertyChanged(nameof(SelectedCount));
            (CommitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Deselecciona todos los cambios.
    /// </summary>
    private void DeselectAll()
    {
        _isMassUpdating = true;
        try
        {
            foreach (var change in Changes) change.IsSelected = false;
        }
        finally
        {
            _isMassUpdating = false;
            OnPropertyChanged(nameof(AreAllSelected));
            OnPropertyChanged(nameof(SelectedCount));
            (CommitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Mapea un FileChange del dominio a un ChangeItemViewModel.
    /// </summary>
    private ChangeItemViewModel MapToViewModel(FileChange fileChange)
    {
        var viewModel = new ChangeItemViewModel
        {
            FilePath = fileChange.FilePath,
            Status = GetStatusText(fileChange.Status),
            ShortStatus = GetShortStatus(fileChange.Status),
            Additions = fileChange.Additions,
            Deletions = fileChange.Deletions,
            IsSelected = true // Por defecto seleccionado
        };

        // Asignar icono y color segun el estado
        (viewModel.Icon, viewModel.Color) = GetIconAndColor(fileChange.Status);

        return viewModel;
    }

    /// <summary>
    /// Obtiene el texto corto del estado.
    /// </summary>
    private string GetShortStatus(ChangeStatus status)
    {
        return status switch
        {
            ChangeStatus.Modified => "M",
            ChangeStatus.Added => "A",
            ChangeStatus.Deleted => "D",
            ChangeStatus.Renamed => "R",
            ChangeStatus.Untracked => "?",
            ChangeStatus.Conflict => "U",
            _ => "?"
        };
    }

    /// <summary>
    /// Obtiene el texto descriptivo del estado.
    /// </summary>
    private string GetStatusText(ChangeStatus status)
    {
        return status switch
        {
            ChangeStatus.Modified => "Modificado",
            ChangeStatus.Added => "Anadido",
            ChangeStatus.Deleted => "Eliminado",
            ChangeStatus.Renamed => "Renombrado",
            ChangeStatus.Untracked => "Sin seguimiento",
            ChangeStatus.Conflict => "Conflicto",
            _ => "Desconocido"
        };
    }

    /// <summary>
    /// Obtiene el icono y color para un estado.
    /// </summary>
    private (PackIconKind Icon, Brush Color) GetIconAndColor(ChangeStatus status)
    {
        return status switch
        {
            ChangeStatus.Modified => (PackIconKind.FileEdit, Brushes.Orange),
            ChangeStatus.Added => (PackIconKind.FilePlus, Brushes.Green),
            ChangeStatus.Deleted => (PackIconKind.FileRemove, Brushes.Red),
            ChangeStatus.Renamed => (PackIconKind.FileMove, Brushes.Blue),
            ChangeStatus.Untracked => (PackIconKind.FileQuestion, Brushes.Green),
            ChangeStatus.Conflict => (PackIconKind.AlertOctagon, Brushes.Red),
            _ => (PackIconKind.FileQuestion, Brushes.Gray)
        };
    }

    /// <summary>
    /// Limpia recursos cuando se destruye el ViewModel.
    /// </summary>
    public void Dispose()
    {
        _changeWatcher?.Dispose();
        _loadCts?.Cancel();
        _loadCts?.Dispose();
    }

    #endregion
}





