using Chapi.Application.UseCases.Git;
using Chapi.Domain.Entities;
using CommunityToolkit.Mvvm.Input;
using Chapi.Infrastructure.Services;
using Chapi.Presentation.Shared.Tasks;
using Chapi.Presentation.Shared.Dialogs.Views;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using MaterialDesignThemes.Wpf;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using Chapi.Presentation.Shared.Mvvm;

namespace Chapi.Presentation.Features.Changes.ViewModels;

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
    private readonly IAsyncRelayCommand _connectAccountCommand;
    private const int WslPollingIntervalMs = 1200;

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
    private int _ahead;
    private int _behind;
    private bool _isSyncing;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _wslPollingCts;
    private CancellationTokenSource? _diffLoadCts;
    private DateTime _lastRefreshTime = DateTime.MinValue;
    private DateTime _lastMetadataRefreshTime = DateTime.MinValue;
    private string _lastLoadedProjectPath = string.Empty;
    private string _lastAppliedChangesSignature = string.Empty;
    private string _lastRenderedDiffFingerprint = string.Empty;
    private bool _isLiveRefreshEnabled;

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

        // Inicializar watcher y cache (como GitHub Desktop)
        _changeWatcher = new Chapi.Infrastructure.Git.GitChangeWatcher();
        _changesCache = new Chapi.Infrastructure.Git.GitChangesCache();

        // Suscribirse a cambios del repositorio
        _changeWatcher.RepositoryChanged += OnRepositoryChanged;

        Changes = new ObservableCollection<ChangeItemViewModel>();
        Stashes = new ObservableCollection<GitStash>();
        StashedFiles = new ObservableCollection<ChangeItemViewModel>();
        DiffLines = new ObservableCollection<DiffPiece>();

        LoadChangesCommand = new AsyncRelayCommand(LoadChangesAsync);
        CommitCommand = new AsyncRelayCommand(CommitAsync, CanCommit);
        SelectAllCommand = new RelayCommand(SelectAll);
        DeselectAllCommand = new RelayCommand(DeselectAll);

        DiscardCommand = new AsyncRelayCommand<ChangeItemViewModel?>(DiscardAsync);
        StashSelectedCommand = new AsyncRelayCommand(StashSelectedAsync);
        PopStashCommand = new AsyncRelayCommand<GitStash?>(PopStashAsync);
        PopAllStashesCommand = new AsyncRelayCommand(PopAllStashesAsync);
        DropStashCommand = new AsyncRelayCommand<GitStash?>(DropStashAsync);
        ClearStashesCommand = new AsyncRelayCommand(ClearStashesAsync);
        RestoreFileFromStashCommand = new AsyncRelayCommand<ChangeItemViewModel?>(RestoreFileFromStashAsync);
        GenerateCommitMessageCommand = new AsyncRelayCommand(GenerateCommitMessageAsync);
        DiscardAllCommand = new AsyncRelayCommand(DiscardAllAsync);
        _connectAccountCommand = new AsyncRelayCommand(ConnectAccountAsync, () => !IsLoading);

        // Suscribirse al evento de actualizacion de avatares
        Chapi.Domain.Services.AvatarCacheService.Instance.AvatarUpdated += OnAvatarUpdated;
    }

    private void OnRepositoryChanged(object? sender, string projectPath)
    {
        // Solo recargar si es el proyecto actual
        if (string.Equals(projectPath, ProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            _changesCache.Invalidate(projectPath);

            // Ejecutar en el UI thread para evitar errores de cross-thread
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
            {
                // Solo llamamos a LoadChangesAsync, que internamente llamar? a LoadMetadataAsync
                await LoadChangesAsync();
            });
        }
    }

    private void OnAvatarUpdated(object sender, Chapi.Domain.Services.AvatarUpdatedEventArgs e)
    {
        // Forzar actualizacion del DisplayUserName para que el binding se refresque
        OnPropertyChanged(nameof(DisplayUserName));
    }

    /// <summary>
    /// Fuerza la recarga de cambios, invalidando la cache interna.
    /// Util cuando ocurren cambios externos (como un Undo Commit) que el watcher podria no detectar a tiempo.
    /// </summary>
    public async Task ForceRefreshAsync()
    {
        if (string.IsNullOrEmpty(ProjectPath)) return;

        _lastRefreshTime = DateTime.MinValue;
        _lastLoadedProjectPath = string.Empty;
        _lastMetadataRefreshTime = DateTime.MinValue;
        await LoadChangesAsync(
            bypassThrottle: true,
            invalidateCache: true,
            refreshMetadata: true,
            forceMetadataRefresh: true);
    }

    /// <summary>
    /// Suspende temporalmente las notificaciones del watcher para evitar recargas
    /// durante operaciones Git masivas (por ejemplo, checkout de rama).
    /// </summary>
    public IDisposable SuspendWatcher() => _changeWatcher.Silence();

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".ico", ".bmp", ".webp", ".svgz",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".zip", ".rar", ".7z", ".tar", ".gz",
        ".dll", ".exe", ".pdb", ".bin", ".iso", ".apk",
        ".mp3", ".wav", ".ogg", ".mp4", ".mov", ".avi", ".mkv",
        ".ttf", ".otf", ".woff", ".woff2", ".eot"
    };

    private async Task GenerateCommitMessageAsync()
    {
        if (string.IsNullOrEmpty(ProjectPath)) return;
        var selectedChanges = Changes.Where(c => c.IsSelected).ToList();
        if (!selectedChanges.Any()) return;

        IsGenerating = true;
        try
        {
            // Obtener diff optimizado para IA (maneja archivos nuevos, modificados, truncamiento y binarios)
            var fullDiff = await BuildOptimizedDiffForAIAsync(selectedChanges);
            if (string.IsNullOrWhiteSpace(fullDiff)) return;

            // Llamar a IA usando Use Case
            var result = await _generateCommitMessageUseCase.ExecuteAsync(fullDiff);

            if (result.IsSuccess)
            {
                var jsonResponse = result.Data;
                // Limpiar posibles bloques markdown ```json ... ```
                if (jsonResponse.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                {
                    jsonResponse = jsonResponse.Substring(7).Trim();
                    if (jsonResponse.EndsWith("```"))
                    {
                        jsonResponse = jsonResponse.Substring(0, jsonResponse.Length - 3).Trim();
                    }
                }
                else if (jsonResponse.StartsWith("```"))
                {
                    jsonResponse = jsonResponse.Substring(3).Trim();
                    if (jsonResponse.EndsWith("```"))
                    {
                        jsonResponse = jsonResponse.Substring(0, jsonResponse.Length - 3).Trim();
                    }
                }

                try
                {
                    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var commitMsg = System.Text.Json.JsonSerializer.Deserialize<Chapi.Domain.Entities.CommitMessageResponse>(jsonResponse, options);
                    if (commitMsg != null && !string.IsNullOrWhiteSpace(commitMsg.Summary))
                    {
                        CommitSummary = commitMsg.Summary;
                        CommitDescription = commitMsg.Description ?? string.Empty;
                    }
                    else
                    {
                        CommitSummary = jsonResponse;
                        CommitDescription = string.Empty;
                    }
                }
                catch
                {
                    // Fallback si no es JSON valido
                    CommitSummary = jsonResponse;
                    CommitDescription = string.Empty;
                }
            }
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private async Task<string> BuildOptimizedDiffForAIAsync(IReadOnlyList<ChangeItemViewModel> selectedChanges)
    {
        var sb = new System.Text.StringBuilder();

        // 1. Resumen general de archivos seleccionados (Bird's Eye View)
        sb.AppendLine($"Archivos involucrados en este commit ({selectedChanges.Count}):");
        foreach (var change in selectedChanges)
        {
            var statusLabel = change.ShortStatus switch
            {
                "A" => "[Nuevo/Añadido]",
                "?" => "[Nuevo/Sin seguimiento]",
                "M" => "[Modificado]",
                "D" => "[Eliminado]",
                "R" => "[Renombrado]",
                "U" => "[Conflicto]",
                _ => $"[{change.ShortStatus}]"
            };
            sb.AppendLine($"- {statusLabel} {change.FilePath} (+{change.Additions}, -{change.Deletions})");
        }
        sb.AppendLine();
        sb.AppendLine("=== Detalle de Cambios ===");

        const int maxTotalChars = 35000; // Presupuesto global (~7,000-8,000 tokens)
        const int maxFileChars = 5000;   // Presupuesto por archivo (~1,000 tokens)

        for (int i = 0; i < selectedChanges.Count; i++)
        {
            var change = selectedChanges[i];

            // Comprobar límite global de payload
            if (sb.Length >= maxTotalChars)
            {
                int remaining = selectedChanges.Count - i;
                sb.AppendLine($"... [Límite de contexto alcanzado: {remaining} archivo(s) restante(s) listado(s) en el resumen superior] ...");
                break;
            }

            var ext = Path.GetExtension(change.FilePath);

            // Filtro 1: Archivos Binarios por extensión
            if (BinaryExtensions.Contains(ext))
            {
                sb.AppendLine($"=== Archivo Binario: {change.FilePath} ({change.ShortStatus}) ===");
                sb.AppendLine($"[Archivo binario {ext}]");
                sb.AppendLine();
                continue;
            }

            // Filtro 2: Archivos Autogenerados o Lockfiles
            if (IsAutoGeneratedOrLockFile(change.FilePath))
            {
                sb.AppendLine($"=== Archivo Autogenerado / Lock: {change.FilePath} ({change.ShortStatus}) ===");
                sb.AppendLine($"[Archivo de dependencias/autogenerado: +{change.Additions}, -{change.Deletions} cambios omitidos para brevedad]");
                sb.AppendLine();
                continue;
            }

            // Filtro 3: Archivos Eliminados
            if (change.ShortStatus == "D")
            {
                sb.AppendLine($"=== Archivo Eliminado: {change.FilePath} ===");
                sb.AppendLine("[Archivo eliminado]");
                sb.AppendLine();
                continue;
            }

            // Intentar obtener diff de Git para archivos rastreados
            string diff = string.Empty;
            if (change.ShortStatus != "?")
            {
                try
                {
                    diff = await _gitRepository.GetDiffAsync(ProjectPath, change.FilePath);
                }
                catch { }
            }

            // Si hay diff válido de Git
            if (!string.IsNullOrWhiteSpace(diff))
            {
                sb.AppendLine($"=== Diff: {change.FilePath} ({change.ShortStatus}) ===");
                if (diff.Length > maxFileChars)
                {
                    var lines = diff.Split('\n');
                    if (lines.Length > 100)
                    {
                        sb.AppendLine(string.Join('\n', lines.Take(100)));
                        sb.AppendLine($"... [Diff truncado: {lines.Length - 100} líneas adicionales omitidas] ...");
                    }
                    else
                    {
                        sb.AppendLine(diff.Substring(0, maxFileChars));
                        sb.AppendLine("... [Diff truncado] ...");
                    }
                }
                else
                {
                    sb.AppendLine(diff.TrimEnd());
                }
                sb.AppendLine();
            }
            else
            {
                // Si es un archivo nuevo ('?' o 'A' donde git diff devuelve vacío), leemos el contenido local
                string fullPath = GetAbsoluteProjectFilePath(ProjectPath, change.FilePath);
                if (File.Exists(fullPath))
                {
                    try
                    {
                        // Detección rápida de binario por bytes nulos en los primeros 4KB
                        var fileInfo = new FileInfo(fullPath);
                        var bufferLength = Math.Min(4096, (int)fileInfo.Length);
                        if (bufferLength > 0)
                        {
                            var buffer = new byte[bufferLength];
                            using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                int bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length);
                                if (buffer.Take(bytesRead).Any(b => b == 0))
                                {
                                    sb.AppendLine($"=== Archivo Binario: {change.FilePath} ===");
                                    sb.AppendLine("[Contenido binario detectado]");
                                    sb.AppendLine();
                                    continue;
                                }
                            }
                        }

                        var allLines = await File.ReadAllLinesAsync(fullPath);
                        sb.AppendLine($"=== Archivo Nuevo: {change.FilePath} ({allLines.Length} líneas) ===");

                        if (allLines.Length <= 120)
                        {
                            foreach (var line in allLines)
                            {
                                sb.AppendLine($"+ {line}");
                            }
                        }
                        else
                        {
                            // Muestreo Inteligente: Primeras 100 líneas + últimas 20 líneas
                            for (int idx = 0; idx < 100; idx++)
                            {
                                sb.AppendLine($"+ {allLines[idx]}");
                            }
                            sb.AppendLine($"... [Contenido intermedio omitido: {allLines.Length - 120} líneas intermedias (Tamaño total: {fileInfo.Length / 1024} KB)] ...");
                            for (int idx = allLines.Length - 20; idx < allLines.Length; idx++)
                            {
                                sb.AppendLine($"+ {allLines[idx]}");
                            }
                        }
                        sb.AppendLine();
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"=== Archivo Nuevo: {change.FilePath} ===");
                        sb.AppendLine($"[No se pudo leer el contenido: {ex.Message}]");
                        sb.AppendLine();
                    }
                }
            }
        }

        return sb.ToString();
    }

    private static bool IsAutoGeneratedOrLockFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (string.Equals(fileName, "package-lock.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "yarn.lock", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "pnpm-lock.yaml", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "packages.lock.json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    #region Properties

    /// <summary>
    /// Coleccion de cambios en el repositorio.
    /// </summary>
    public ObservableCollection<ChangeItemViewModel> Changes { get; }

    /// <summary>
    private IDisposable? _manualSilencer;

    /// <summary>
    /// Ruta del proyecto actual.
    /// </summary>
    public string ProjectPath
    {
        get => _projectPath;
        set
        {
            var previousPath = _projectPath;
            if (SetProperty(ref _projectPath, value))
            {
                if (!string.IsNullOrWhiteSpace(previousPath) &&
                    !string.Equals(previousPath, value, StringComparison.OrdinalIgnoreCase))
                {
                    _changeWatcher.UnwatchRepository(previousPath);
                }

                StopWslPolling();

                // Silenciar el watcher durante la transicion para evitar que los comandos de MainWindow o la carga inicial lo disparen
                _manualSilencer?.Dispose();
                _manualSilencer = _changeWatcher.Silence();

                CommitSummary = string.Empty;
                CommitDescription = string.Empty;
                _lastRefreshTime = DateTime.MinValue;
                _lastMetadataRefreshTime = DateTime.MinValue;
                _lastLoadedProjectPath = string.Empty;
                _lastAppliedChangesSignature = string.Empty;
                _lastRenderedDiffFingerprint = string.Empty;
                _loadCts?.Cancel();
                _loadCts?.Dispose();
                _loadCts = null;
                _diffLoadCts?.Cancel();
                _diffLoadCts?.Dispose();
                _diffLoadCts = null;
                _selectedChange = null;
                DiffLines.Clear();

                // Iniciar monitoreo del nuevo proyecto
                if (!string.IsNullOrWhiteSpace(value))
                {
                    // En rutas WSL (UNC), FileSystemWatcher es costoso e inestable.
                    // En su lugar usamos polling ligero cuando la vista de cambios esta activa.
                    if (!IsWslPath(value))
                    {
                        _changeWatcher.WatchRepository(value);
                    }
                }

                UpdateWslPollingState();

                // Limpiar contadores
                TotalAdditions = 0;
                TotalDeletions = 0;
                Ahead = 0;
                Behind = 0;
                OnPropertyChanged(nameof(TotalChangesCount));

                // Lanzar carga inicial
                LoadChangesAsync().Forget("cargando cambios");

                // Programar el fin del silencio manual despues de un tiempo prudencial o cuando Load termine
                // Esto protege los comandos Git que corran en paralelo en MainWindow
                Task.Run(async () =>
                {
                    await Task.Delay(3000); // 3 segundos de gracia para que MainWindow termine sus comandos
                    _manualSilencer?.Dispose();
                    _manualSilencer = null;
                }).Forget("restaurando watcher de cambios");
            }
        }
    }

    private async Task LoadMetadataAsync(CancellationToken token = default)
    {
        try
        {
            var result = await _gitRepository.GetMetadataAsync(ProjectPath);
            if (token.IsCancellationRequested) return;

            if (result.IsSuccess)
            {
                var m = result.Data;

                // Actualizar perfil
                GitUserName = m.UserName;
                GitUserEmail = m.UserEmail;

                // Actualizar indicadores de sincronizacion
                Ahead = m.Ahead;
                Behind = m.Behind;

                // Actualizar Auth Status
                AuthenticatedProvider = _authFactory.DetectProviderFromUrl(m.RemoteUrl);
                IsAuthenticated = AuthenticatedProvider != Chapi.Domain.Enums.GitProvider.Unknown;

                var credResult = await _credentialStorage.GetCredentialAsync(AuthenticatedProvider.ToString());
                if (credResult.HasValue)
                {
                    AuthenticatedUserName = credResult.Value.username;
                    IsUserLoggedIn = true;
                }
                else if (AuthenticatedProvider != Chapi.Domain.Enums.GitProvider.Unknown)
                {
                    // Solo resetear si realmente el proveedor es valido pero no hay credenciales
                    AuthenticatedUserName = "Conectar";
                    IsUserLoggedIn = false;
                }
                // Si el proveedor es Desconocido, no tocamos el estado actual para evitar parpadeo

                // Forzar actualizacion de iconos y colores
                OnPropertyChanged(nameof(ProviderIcon));
                OnPropertyChanged(nameof(ProviderColor));

                // Pre-cargar avatar si es necesario
                if (IsUserLoggedIn && AuthenticatedProvider == Chapi.Domain.Enums.GitProvider.GitLab && !string.IsNullOrWhiteSpace(AuthenticatedUserName))
                {
                    _ = Chapi.Domain.Services.AvatarCacheService.Instance.GetGitLabAvatarUrlAsync(AuthenticatedUserName);
                }
            }
        }
        catch { }

        await LoadStashesAsync(token);
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

    /// <summary>
    /// Total de lineas eliminadas.
    /// </summary>
    public int TotalChangesCount => Changes.Count;

    public bool HasPendingChanges => Changes.Count > 0;

    public int Ahead
    {
        get => _ahead;
        set => SetProperty(ref _ahead, value);
    }

    public int Behind
    {
        get => _behind;
        set => SetProperty(ref _behind, value);
    }

    public bool IsSyncing
    {
        get => _isSyncing;
        set => SetProperty(ref _isSyncing, value);
    }

    /// <summary>
    /// Indica si se esta generando un mensaje de commit con IA.
    /// </summary>
    public bool IsGenerating
    {
        get => _isGenerating;
        set => SetProperty(ref _isGenerating, value);
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
                CommitCommand.NotifyCanExecuteChanged();
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
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                _connectAccountCommand.NotifyCanExecuteChanged();
            }
        }
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
    /// Solo retorna username si esta autenticado Y el provider coincide con el del proyecto
    /// </summary>
    public string DisplayUserName
    {
        get
        {
            // Solo mostrar username si:
            // 1. Est? logueado
            // 2. El provider del proyecto coincide con el provider autenticado
            // 3. Tiene un username valido

            if (!IsUserLoggedIn ||
                AuthenticatedProvider == Chapi.Domain.Enums.GitProvider.Unknown ||
                string.IsNullOrWhiteSpace(AuthenticatedUserName))
            {
                // Fallback al nombre de Git local si no hay sesion iniciada
                return !string.IsNullOrWhiteSpace(GitUserName) ? GitUserName : string.Empty;
            }

            // Retornar el username autenticado (que coincide con el provider del proyecto)
            return AuthenticatedUserName;
        }
    }

    public ICommand ConnectAccountCommand => _connectAccountCommand;

    #endregion

    #region Commands

    public IAsyncRelayCommand LoadChangesCommand { get; }
    public IAsyncRelayCommand CommitCommand { get; }
    public IRelayCommand SelectAllCommand { get; }
    public IRelayCommand DeselectAllCommand { get; }
    public IAsyncRelayCommand<ChangeItemViewModel?> DiscardCommand { get; }
    public IAsyncRelayCommand StashSelectedCommand { get; }
    public IAsyncRelayCommand<GitStash?> PopStashCommand { get; }
    public IAsyncRelayCommand PopAllStashesCommand { get; }
    public IAsyncRelayCommand<GitStash?> DropStashCommand { get; }
    public IAsyncRelayCommand ClearStashesCommand { get; }
    public IAsyncRelayCommand<ChangeItemViewModel?> RestoreFileFromStashCommand { get; }
    public IAsyncRelayCommand GenerateCommitMessageCommand { get; }
    public IAsyncRelayCommand DiscardAllCommand { get; }

    #endregion

    #region Methods

    public void SetLiveRefreshEnabled(bool isEnabled)
    {
        if (_isLiveRefreshEnabled == isEnabled)
            return;

        _isLiveRefreshEnabled = isEnabled;
        UpdateWslPollingState();
    }

    /// <summary>
    /// Carga los cambios del repositorio.
    /// </summary>
    public Task LoadChangesAsync() =>
        LoadChangesAsync(
            bypassThrottle: false,
            invalidateCache: false,
            refreshMetadata: true,
            forceMetadataRefresh: false);

    private async Task LoadChangesAsync(
        bool bypassThrottle,
        bool invalidateCache,
        bool refreshMetadata,
        bool forceMetadataRefresh)
    {
        if (string.IsNullOrWhiteSpace(ProjectPath))
            return;

        if (invalidateCache)
        {
            _changesCache.Invalidate(ProjectPath);
        }

        // Throttle: Evitar recargas masivas en menos de 1.5 segundos
        // Importante: No saltar este control si Changes.Count == 0, ya que eso causa bucles en proyectos vacios.
        var now = DateTime.Now;
        var sameProjectAsLastLoad = string.Equals(ProjectPath, _lastLoadedProjectPath, StringComparison.OrdinalIgnoreCase);
        if (!bypassThrottle && sameProjectAsLastLoad && (now - _lastRefreshTime).TotalMilliseconds < 1500)
        {
            return;
        }
        _lastRefreshTime = now;
        _lastLoadedProjectPath = ProjectPath;

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsSyncing = true;

        using var silencer = _changeWatcher.Silence();

        // Resetear solo si el proyecto es nuevo o esta vacio, 
        // de lo contrario mantener los cambios actuales hasta que lleguen los nuevos (evita parpadeo)
        bool isFullReload = !sameProjectAsLastLoad || Changes.Count == 0;
        bool hadVisibleChanges = Changes.Count > 0;
        var previousSelectionByPath = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (sameProjectAsLastLoad)
        {
            foreach (var change in Changes)
            {
                if (!string.IsNullOrWhiteSpace(change.FilePath))
                {
                    previousSelectionByPath[change.FilePath] = change.IsSelected;
                }
            }
        }
        var previouslySelectedPath = sameProjectAsLastLoad ? SelectedChange?.FilePath : null;

        if (isFullReload)
        {
            Changes.Clear();
            OnPropertyChanged(nameof(TotalChangesCount));
        }

        try
        {
            if (_changesCache.TryGetChanges(ProjectPath, out var cachedChanges, out var cachedAdditions, out var cachedDeletions))
            {
                var cachedChangesList = cachedChanges.ToList();

                // Si hay cambios visibles y el cache dice "cero", preferimos consultar Git de nuevo.
                // Evita que un vacio transitorio "congele" la vista sin cambios reales.
                if (hadVisibleChanges && cachedChangesList.Count == 0)
                {
                    _changesCache.Invalidate(ProjectPath);
                }
                else
                {

                    // Actualizar de forma atomica para evitar duplicados si es un refresco
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Changes.Clear();
                        ChangeItemViewModel? restoredSelectedChange = null;
                        foreach (var fileChange in cachedChangesList)
                        {
                            var viewModel = MapToViewModel(fileChange);

                            if (previousSelectionByPath.TryGetValue(viewModel.FilePath, out var wasSelected))
                            {
                                viewModel.IsSelected = wasSelected;
                            }

                            if (!string.IsNullOrWhiteSpace(previouslySelectedPath) &&
                                string.Equals(viewModel.FilePath, previouslySelectedPath, StringComparison.OrdinalIgnoreCase))
                            {
                                restoredSelectedChange = viewModel;
                            }

                            viewModel.PropertyChanged += (s, e) =>
                            {
                                if (_isMassUpdating) return;

                                if (e.PropertyName == nameof(ChangeItemViewModel.IsSelected))
                                {
                                    OnPropertyChanged(nameof(AreAllSelected));
                                    OnPropertyChanged(nameof(SelectedCount));
                                    CommitCommand.NotifyCanExecuteChanged();
                                }
                            };
                            Changes.Add(viewModel);
                        }

                        SelectedChange = restoredSelectedChange;
                    });

                    TotalAdditions = cachedAdditions;
                    TotalDeletions = cachedDeletions;
                    _lastAppliedChangesSignature = BuildChangesSignature(cachedChangesList);
                    OnPropertyChanged(nameof(AreAllSelected));
                    OnPropertyChanged(nameof(SelectedCount));
                    OnPropertyChanged(nameof(TotalChangesCount));

                    TriggerMetadataRefreshIfNeeded(refreshMetadata, forceMetadataRefresh, sameProjectAsLastLoad, token);
                    await LoadDiffAsync();

                    IsSyncing = false;
                    return;
                }
            }

            var fileChanges = (await _loadChangesUseCase.ExecuteAsync(ProjectPath)).ToList();
            var changesSignature = BuildChangesSignature(fileChanges);

            // Si veniamos mostrando cambios y Git devuelve vacio, confirmamos una vez mas
            // para evitar "falsos vacios" por condiciones transitorias.
            if (hadVisibleChanges && fileChanges.Count == 0)
            {
                await Task.Delay(250, token);
                var retryChanges = (await _loadChangesUseCase.ExecuteAsync(ProjectPath)).ToList();
                if (retryChanges.Count > 0)
                {
                    fileChanges = retryChanges;
                    changesSignature = BuildChangesSignature(fileChanges);
                }
            }

            if (token.IsCancellationRequested) return;

            if (sameProjectAsLastLoad &&
                string.Equals(changesSignature, _lastAppliedChangesSignature, StringComparison.Ordinal))
            {
                TriggerMetadataRefreshIfNeeded(refreshMetadata, forceMetadataRefresh, sameProjectAsLastLoad, token);
                await LoadDiffAsync();
                return;
            }

            var viewModels = fileChanges.Select(fileChange =>
            {
                var vm = MapToViewModel(fileChange);

                if (previousSelectionByPath.TryGetValue(vm.FilePath, out var wasSelected))
                {
                    vm.IsSelected = wasSelected;
                }

                vm.PropertyChanged += (s, e) =>
                {
                    if (_isMassUpdating) return;
                    if (e.PropertyName == nameof(ChangeItemViewModel.IsSelected))
                    {
                        OnPropertyChanged(nameof(AreAllSelected));
                        OnPropertyChanged(nameof(SelectedCount));
                        CommitCommand.NotifyCanExecuteChanged();
                    }
                };
                return vm;
            }).ToList();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Changes.Clear();
                ChangeItemViewModel? restoredSelectedChange = null;
                foreach (var vm in viewModels)
                {
                    Changes.Add(vm);

                    if (!string.IsNullOrWhiteSpace(previouslySelectedPath) &&
                        string.Equals(vm.FilePath, previouslySelectedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        restoredSelectedChange = vm;
                    }
                }

                SelectedChange = restoredSelectedChange;
                OnPropertyChanged(nameof(AreAllSelected));
                OnPropertyChanged(nameof(SelectedCount));
                OnPropertyChanged(nameof(TotalChangesCount));
                IsLoading = false;
            });

            _lastAppliedChangesSignature = changesSignature;

            if (IsWslPath(ProjectPath))
            {
                // En WSL evitamos numstats por archivo para priorizar render inmediato.
                TotalAdditions = 0;
                TotalDeletions = 0;
            }
            else
            {
                _ = LoadFileStatsInBackgroundAsync(token);
            }

            // Si no hay cambios, no persistimos "vacio" en cache para evitar congelar estados transitorios.
            if (fileChanges.Count == 0)
            {
                _changesCache.Invalidate(ProjectPath);
            }

            TriggerMetadataRefreshIfNeeded(refreshMetadata, forceMetadataRefresh, sameProjectAsLastLoad, token);
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

    private static bool IsWslPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase);
    }

    private void TriggerMetadataRefreshIfNeeded(
        bool refreshMetadata,
        bool forceMetadataRefresh,
        bool sameProjectAsLastLoad,
        CancellationToken token)
    {
        if (!refreshMetadata)
            return;

        var metadataIsFresh =
            sameProjectAsLastLoad &&
            !forceMetadataRefresh &&
            (DateTime.Now - _lastMetadataRefreshTime).TotalSeconds < 15;

        if (metadataIsFresh)
            return;

        _lastMetadataRefreshTime = DateTime.Now;

        _ = Task.Run(async () =>
        {
            using var metadataSilencer = _changeWatcher.Silence();
            try { await LoadMetadataAsync(token); } catch { }
        }, token);
    }

    private static string BuildChangesSignature(IEnumerable<FileChange> changes)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var change in changes)
        {
            builder.Append(change.FilePath)
                .Append('|')
                .Append((int)change.Status)
                .Append('\n');
        }

        return builder.ToString();
    }

    private void UpdateWslPollingState()
    {
        if (_isLiveRefreshEnabled && IsWslPath(ProjectPath))
        {
            StartWslPolling();
        }
        else
        {
            StopWslPolling();
        }
    }

    private void StartWslPolling()
    {
        if (_wslPollingCts != null || string.IsNullOrWhiteSpace(ProjectPath) || !IsWslPath(ProjectPath))
            return;

        _wslPollingCts = new CancellationTokenSource();
        var token = _wslPollingCts.Token;

        Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(WslPollingIntervalMs, token);

                    if (token.IsCancellationRequested ||
                        !_isLiveRefreshEnabled ||
                        string.IsNullOrWhiteSpace(ProjectPath) ||
                        !IsWslPath(ProjectPath) ||
                        IsLoading ||
                        IsSyncing ||
                        _changeWatcher.IsSilenced)
                    {
                        continue;
                    }

                    await InvokeOnUiThreadAsync(() =>
                        LoadChangesAsync(
                            bypassThrottle: true,
                            invalidateCache: true,
                            refreshMetadata: false,
                            forceMetadataRefresh: false));
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, token).Forget("monitoreando cambios WSL");
    }

    private void StopWslPolling()
    {
        _wslPollingCts?.Cancel();
        _wslPollingCts?.Dispose();
        _wslPollingCts = null;
    }

    private static async Task InvokeOnUiThreadAsync(Func<Task> action)
    {
        var application = System.Windows.Application.Current;
        if (application?.Dispatcher == null || application.Dispatcher.CheckAccess())
        {
            await action();
            return;
        }

        var dispatcherOperation = application.Dispatcher.InvokeAsync(action);
        await await dispatcherOperation.Task;
    }

    /// <summary>
    /// Carga las estadisticas de archivos en background sin bloquear la UI.
    /// Esto permite mostrar la lista rapidamente y luego actualizar los numeros.
    /// </summary>
    private async Task LoadFileStatsInBackgroundAsync(CancellationToken token)
    {
        using var silencer = _changeWatcher.Silence();

        try
        {
            int totalAdd = 0;
            int totalDel = 0;

            var visibleFiles = Changes.Take(20).ToList();

            foreach (var file in visibleFiles)
            {
                if (token.IsCancellationRequested) return;

                try
                {
                    if (file.Additions > 0 || file.Deletions > 0) continue;

                    var stats = await _gitRepository.GetFileStatsAsync(ProjectPath, file.FilePath);
                    file.Additions = stats.additions;
                    file.Deletions = stats.deletions;

                    totalAdd += stats.additions;
                    totalDel += stats.deletions;
                }
                catch { }
            }

            TotalAdditions = totalAdd;
            TotalDeletions = totalDel;
            var remainingFiles = Changes.Skip(20).ToList();
            foreach (var file in remainingFiles)
            {
                if (token.IsCancellationRequested) return;

                try
                {
                    var stats = await _gitRepository.GetFileStatsAsync(ProjectPath, file.FilePath);
                    file.Additions = stats.additions;
                    file.Deletions = stats.deletions;

                    TotalAdditions += stats.additions;
                    TotalDeletions += stats.deletions;
                }
                catch { }
            }

            // ?? Guardar en cache para la proxima vez (como GitHub Desktop)
            if (!token.IsCancellationRequested)
            {
                var allChanges = Changes.Select(c => new FileChange
                {
                    FilePath = c.FilePath,
                    Status = MapStatusFromViewModel(c.ShortStatus),
                    Additions = c.Additions,
                    Deletions = c.Deletions
                }).ToList();

                if (allChanges.Count > 0)
                {
                    _changesCache.SetChanges(ProjectPath, allChanges, TotalAdditions, TotalDeletions);
                }
                else
                {
                    _changesCache.Invalidate(ProjectPath);
                }
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


    private async Task ConnectAccountAsync()
    {
        if (AuthenticatedProvider == Chapi.Domain.Enums.GitProvider.Unknown) return;

        // Si ya esta logueado, abrir configuracion
        if (IsUserLoggedIn)
        {
            // Leer configuracion actual de default branch
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

            // Crear y configurar el dialogo
            var dialog = new Chapi.Presentation.Shared.Dialogs.Views.GitConfigDialog
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
                // Cerrar sesion
                await _credentialStorage.DeleteCredentialAsync(AuthenticatedProvider.ToString());

                // Limpiar cache de avatares del usuario
                Chapi.Domain.Services.AvatarCacheService.Instance.ClearUserCache(
                    AuthenticatedProvider.ToString(),
                    AuthenticatedUserName
                );

                // Recargar estado
                _ = LoadMetadataAsync();
                return;
            }

            // Si el usuario guardo cambios en Git config, actualizar la configuracion
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

                    // Recargar configuracion
                    _ = LoadMetadataAsync();
                }
                catch (Exception ex)
                {
                    await DialogService.ShowConfirmDialog("Error", $"No se pudo guardar la configuracion: {ex.Message}", DialogVariant.Error, DialogType.Info);
                }
            }

            return;
        }

        // Si no esta logueado, iniciar proceso de autenticacion
        IsLoading = true;
        try
        {
            // Usamos la factoria de proveedores para obtener el flujo de navegador (GitHub o GitLab)
            var provider = _authFactory.GetProvider(AuthenticatedProvider);
            var result = await provider.AuthenticateAsync();

            if (result.IsSuccess)
            {
                // Recargar el estado para mostrar el usuario logueado
                _ = LoadMetadataAsync();

                // Pre-cargar el avatar para evitar "vibracion" al cambiar de proyecto
                if (AuthenticatedProvider == Chapi.Domain.Enums.GitProvider.GitLab &&
                    !string.IsNullOrWhiteSpace(AuthenticatedUserName))
                {
                    _ = Task.Run(async () =>
                    {
                        await Chapi.Domain.Services.AvatarCacheService.Instance.GetGitLabAvatarUrlAsync(AuthenticatedUserName);
                    });
                }
            }
            else if (result.Error != "Autenticacion cancelada")
            {
                await DialogService.ShowConfirmDialog("Error de Conexion", result.Error, DialogVariant.Error, DialogType.Info);
            }
        }
        catch (Exception ex)
        {
            await DialogService.ShowConfirmDialog("Error", ex.Message, DialogVariant.Error, DialogType.Info);
        }
        finally
        {
            _lastRefreshTime = DateTime.Now;
            IsLoading = false;
        }
    }


    /// <summary>
    /// Carga la lista de stashes.
    /// </summary>
    public async Task LoadStashesAsync(CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(ProjectPath)) return;

        try
        {
            var stashes = await _gitRepository.ListStashesAsync(ProjectPath);
            if (token.IsCancellationRequested) return;

            var stashesList = stashes.ToList();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Stashes.Clear();
                foreach (var stash in stashesList) Stashes.Add(stash);
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
        await InvokeOnUiThreadAsync(() =>
        {
            StashedFiles.Clear();
            SelectedStashedFile = null;
            return Task.CompletedTask;
        });

        if (SelectedStash == null || string.IsNullOrEmpty(ProjectPath)) return;

        try
        {
            var stash = SelectedStash;
            var fileStatuses = await _gitRepository.GetFileStatusesForStashAsync(ProjectPath, stash.Name);
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

            if (SelectedStash != stash) return;

            await InvokeOnUiThreadAsync(() =>
            {
                StashedFiles.Clear();
                foreach (var vm in viewModels) StashedFiles.Add(vm);
                if (StashedFiles.Count > 0 && SelectedStashedFile == null)
                {
                    SelectedStashedFile = StashedFiles[0];
                }
                return Task.CompletedTask;
            });
        }
        catch (Exception) { }
    }

    /// <summary>
    /// Carga el diff del archivo seleccionado usando DiffPlex.
    /// </summary>
    public async Task LoadDiffAsync()
    {
        if (SelectedChange == null || string.IsNullOrEmpty(ProjectPath))
        {
            _diffLoadCts?.Cancel();
            _lastRenderedDiffFingerprint = string.Empty;
            if (!IsStashViewVisible)
            {
                await InvokeOnUiThreadAsync(() =>
                {
                    DiffLines.Clear();
                    return Task.CompletedTask;
                });
            }
            return;
        }

        var selectedChange = SelectedChange;
        var diffFingerprint = BuildDiffFingerprint(selectedChange);
        if (string.Equals(diffFingerprint, _lastRenderedDiffFingerprint, StringComparison.Ordinal) &&
            DiffLines.Count > 0)
        {
            return;
        }

        _diffLoadCts?.Cancel();
        _diffLoadCts?.Dispose();
        _diffLoadCts = new CancellationTokenSource();
        var token = _diffLoadCts.Token;

        using var silencer = _changeWatcher.Silence();

        try
        {
            string oldText = string.Empty;
            string newText = string.Empty;
            if (selectedChange.ShortStatus != "A" && selectedChange.ShortStatus != "?")
            {
                try { oldText = await _gitRepository.GetFileContentAsync(ProjectPath, "HEAD", selectedChange.FilePath); } catch { }
            }
            if (selectedChange.ShortStatus != "D")
            {
                string fullPath = GetAbsoluteProjectFilePath(ProjectPath, selectedChange.FilePath);
                if (File.Exists(fullPath))
                {
                    newText = await File.ReadAllTextAsync(fullPath);
                }
            }

            if (token.IsCancellationRequested)
                return;

            var filteredLines = BuildDiffLines(oldText, newText);
            if (token.IsCancellationRequested)
                return;

            if (!IsSameSelectedChange(selectedChange, SelectedChange))
                return;

            await InvokeOnUiThreadAsync(() =>
            {
                DiffLines.Clear();
                foreach (var line in filteredLines)
                {
                    DiffLines.Add(line);
                }

                _lastRenderedDiffFingerprint = diffFingerprint;
                return Task.CompletedTask;
            });
        }
        catch (Exception)
        {
            // No interrumpir la UI por errores puntuales al renderizar diff.
        }
    }

    /// <summary>
    /// Carga el diff de un archivo dentro de un stash usando DiffPlex.
    /// </summary>
    public async Task LoadStashedFileDiffAsync()
    {
        if (SelectedStash == null || SelectedStashedFile == null || string.IsNullOrEmpty(ProjectPath))
        {
            _lastRenderedDiffFingerprint = string.Empty;
            await InvokeOnUiThreadAsync(() =>
            {
                DiffLines.Clear();
                return Task.CompletedTask;
            });
            return;
        }

        var stash = SelectedStash;
        var stashedFile = SelectedStashedFile;
        var diffFingerprint = $"STASH|{stash.Name}|{stashedFile.FilePath}|{stashedFile.ShortStatus}";

        if (string.Equals(diffFingerprint, _lastRenderedDiffFingerprint, StringComparison.Ordinal) &&
            DiffLines.Count > 0)
        {
            return;
        }

        using var silencer = _changeWatcher.Silence();

        try
        {
            string oldText = string.Empty;
            string newText = string.Empty;

            if (stashedFile.ShortStatus != "D")
            {
                try
                {
                    newText = await _gitRepository.GetFileContentAsync(ProjectPath, stash.Name, stashedFile.FilePath);
                }
                catch { }

                // Si es un archivo nuevo/no rastreado y vino vacío en stash@{n}, consultar el commit de untracked stash@{n}^3
                if (string.IsNullOrEmpty(newText) && (stashedFile.ShortStatus == "?" || stashedFile.ShortStatus == "A"))
                {
                    try
                    {
                        newText = await _gitRepository.GetFileContentAsync(ProjectPath, $"{stash.Name}^3", stashedFile.FilePath);
                    }
                    catch { }
                }
            }

            if (stashedFile.ShortStatus != "A" && stashedFile.ShortStatus != "?")
            {
                try
                {
                    oldText = await _gitRepository.GetFileContentAsync(ProjectPath, $"{stash.Name}^1", stashedFile.FilePath);
                }
                catch { }
            }

            var filteredLines = BuildDiffLines(oldText, newText);

            if (SelectedStash != stash || SelectedStashedFile != stashedFile)
                return;

            await InvokeOnUiThreadAsync(() =>
            {
                DiffLines.Clear();
                foreach (var line in filteredLines)
                {
                    DiffLines.Add(line);
                }

                _lastRenderedDiffFingerprint = diffFingerprint;
                return Task.CompletedTask;
            });
        }
        catch (Exception)
        {
            // No interrumpir la UI por errores puntuales al renderizar diff.
        }
    }

    private List<DiffPiece> BuildDiffLines(string oldText, string newText)
    {
        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(oldText, newText);
        return FilterHunks(diff.Lines);
    }

    private static bool IsSameSelectedChange(ChangeItemViewModel? left, ChangeItemViewModel? right)
    {
        if (left == null || right == null)
            return left == right;

        return string.Equals(left.FilePath, right.FilePath, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.ShortStatus, right.ShortStatus, StringComparison.Ordinal);
    }

    private string BuildDiffFingerprint(ChangeItemViewModel change)
    {
        var builder = new System.Text.StringBuilder()
            .Append(change.FilePath)
            .Append('|')
            .Append(change.ShortStatus)
            .Append('|');

        if (change.ShortStatus != "D")
        {
            var fullPath = GetAbsoluteProjectFilePath(ProjectPath, change.FilePath);
            if (File.Exists(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                builder.Append(fileInfo.LastWriteTimeUtc.Ticks)
                    .Append(':')
                    .Append(fileInfo.Length);
            }
            else
            {
                builder.Append("missing");
            }
        }
        else
        {
            builder.Append("deleted");
        }

        return builder.ToString();
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

        using var silencer = _changeWatcher.Silence();
        var result = await _commitChangesUseCase.ExecuteAsync(request);

        if (result.IsSuccess)
        {
            CommitSummary = string.Empty;
            CommitDescription = string.Empty;

            _changesCache.Invalidate(ProjectPath);

            await LoadChangesAsync();

            CommitCompleted?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            await DialogService.ShowConfirmDialog("Error al crear commit", result.Error, DialogVariant.Error, DialogType.Info);
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
            $"Estas seguro de que deseas descartar los cambios en '{item.FileName}'? Esta accion no se puede deshacer.",
            DialogVariant.Warning);

        if (!confirmed) return;

        using var silencer = _changeWatcher.Silence();
        var result = await _discardChangesUseCase.ExecuteAsync(ProjectPath, new[] { item.FilePath });
        if (result.IsSuccess)
        {
            _changesCache.Invalidate(ProjectPath);
            await LoadChangesAsync();
        }
        else
        {
            await DialogService.ShowConfirmDialog("Error al descartar", result.Error, DialogVariant.Error, DialogType.Info);
        }
    }

    private async Task DiscardAllAsync()
    {
        if (string.IsNullOrEmpty(ProjectPath) || !Changes.Any()) return;

        var confirmed = await DialogService.ShowConfirmDialog(
            "Descartar TODOS los Cambios",
            "Estas seguro de que deseas descartar TODOS los cambios locales? Esta accion eliminara permanentemente tus modificaciones.",
            DialogVariant.Warning);

        if (!confirmed) return;

        var allFiles = Changes.Select(c => c.FilePath).ToArray();
        using var silencer = _changeWatcher.Silence();
        var result = await _discardChangesUseCase.ExecuteAsync(ProjectPath, allFiles);
        if (result.IsSuccess)
        {
            _changesCache.Invalidate(ProjectPath);
            await LoadChangesAsync();
        }
        else
        {
            await DialogService.ShowConfirmDialog("Error al descartar todo", result.Error, DialogVariant.Error, DialogType.Info);
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
                ? $"Deseas guardar '{System.IO.Path.GetFileName(filesToStash[0])}' en el stash?"
                : $"Deseas guardar estos {filesToStash.Count} archivos en el stash?",
            DialogVariant.Info);

        if (!confirmed) return;

        using var silencer = _changeWatcher.Silence();
        var result = await _stashChangesUseCase.ExecuteAsync(ProjectPath, message, filesToStash);
        if (result.IsSuccess)
        {
            _changesCache.Invalidate(ProjectPath);
            await LoadChangesAsync(bypassThrottle: true, invalidateCache: true, refreshMetadata: true, forceMetadataRefresh: false);
            await LoadStashesAsync();
        }
        else
        {
            await DialogService.ShowConfirmDialog("Error al guardar stash", result.Error, DialogVariant.Error, DialogType.Info);
        }
    }

    private async Task PopStashAsync(GitStash? stash)
    {
        if (stash == null || string.IsNullOrEmpty(ProjectPath)) return;

        // Extraer indice del nombre "stash@{n}"
        int index = 0;
        var match = System.Text.RegularExpressions.Regex.Match(stash.Name, @"\{(\d+)\}");
        if (match.Success) index = int.Parse(match.Groups[1].Value);

        using var silencer = _changeWatcher.Silence();
        var result = await _stashPopUseCase.ExecuteAsync(ProjectPath, index);
        if (result.IsSuccess)
        {
            _changesCache.Invalidate(ProjectPath);
            IsStashViewVisible = false;
            SelectedStash = null;
            await LoadChangesAsync(bypassThrottle: true, invalidateCache: true, refreshMetadata: true, forceMetadataRefresh: false);
            await LoadStashesAsync();
        }
        else
        {
            await DialogService.ShowConfirmDialog("Error en Stash",
                $"No se pudo aplicar el stash: {result.Error}\n\nEs posible que existan conflictos con tus cambios actuales.",
                Chapi.Presentation.Shared.Dialogs.Views.DialogVariant.Error, DialogType.Info);
        }
    }

    public async Task PopAllStashesAsync()
    {
        if (string.IsNullOrEmpty(ProjectPath) || !Stashes.Any()) return;

        int totalToRestore = Stashes.Count;
        var confirmed = await DialogService.ShowConfirmDialog(
            "Restaurar Todos los Stashes",
            totalToRestore == 1
                ? "¿Deseas restaurar el stash guardado a tu espacio de trabajo?"
                : $"¿Deseas restaurar los {totalToRestore} stashes guardados a tu espacio de trabajo?",
            DialogVariant.Info);

        if (!confirmed) return;

        using var silencer = _changeWatcher.Silence();
        int restoredCount = 0;

        while (true)
        {
            var stashes = (await _gitRepository.ListStashesAsync(ProjectPath)).ToList();
            if (!stashes.Any()) break;

            var result = await _stashPopUseCase.ExecuteAsync(ProjectPath, 0);
            if (!result.IsSuccess)
            {
                await DialogService.ShowConfirmDialog("Conflicto al restaurar stash",
                    $"Se restauraron {restoredCount} stash(es). Ocurrió un conflicto al aplicar el siguiente stash: {result.Error}\n\nResuelve los conflictos en tus archivos antes de continuar.",
                    DialogVariant.Warning, DialogType.Info);
                break;
            }
            restoredCount++;
        }

        _changesCache.Invalidate(ProjectPath);
        IsStashViewVisible = false;
        SelectedStash = null;
        await LoadChangesAsync(bypassThrottle: true, invalidateCache: true, refreshMetadata: true, forceMetadataRefresh: false);
        await LoadStashesAsync();
    }

    private async Task RestoreFileFromStashAsync(ChangeItemViewModel? item)
    {
        if (item == null || SelectedStash == null || string.IsNullOrEmpty(ProjectPath)) return;

        try
        {
            using var silencer = _changeWatcher.Silence();
            var result = await _gitRepository.RestoreFileFromStashAsync(ProjectPath, SelectedStash.Name, item.FilePath);
            if (!result.IsSuccess)
            {
                await DialogService.ShowConfirmDialog("Error al restaurar archivo",
                    $"No se pudo restaurar el archivo '{item.FileName}': {result.Error}",
                    DialogVariant.Error, DialogType.Info);
                return;
            }

            _changesCache.Invalidate(ProjectPath);
            IsStashViewVisible = false;
            await LoadChangesAsync(bypassThrottle: true, invalidateCache: true, refreshMetadata: true, forceMetadataRefresh: false);
            await LoadStashesAsync();
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
            $"Estas seguro de eliminar el stash?\n\n'{stash.Message}'\n\nEsta accion es irreversible.",
            DialogVariant.Warning,
            DialogType.Confirm);

        if (!confirmed) return;

        int index = 0;
        var match = System.Text.RegularExpressions.Regex.Match(stash.Name, @"\{(\d+)\}");
        if (match.Success) index = int.Parse(match.Groups[1].Value);

        using var silencer = _changeWatcher.Silence();
        var result = await _stashDropUseCase.ExecuteAsync(ProjectPath, index);
        if (result.IsSuccess)
        {
            _changesCache.Invalidate(ProjectPath);
            IsStashViewVisible = false;
            SelectedStash = null;
            await LoadChangesAsync(bypassThrottle: true, invalidateCache: true, refreshMetadata: true, forceMetadataRefresh: false);
            await LoadStashesAsync();
        }
    }

    private async Task ClearStashesAsync()
    {
        if (string.IsNullOrEmpty(ProjectPath)) return;

        var confirmed = await DialogService.ShowConfirmDialog(
            "Limpiar Stashes",
            "Estas seguro de que deseas eliminar TODOS los stashes?\n\nEsta accion borrara permanentemente todas las entradas guardadas.",
            DialogVariant.Warning,
            DialogType.Confirm);

        if (!confirmed) return;

        using var silencer = _changeWatcher.Silence();
        var result = await _stashClearUseCase.ExecuteAsync(ProjectPath);
        if (result.IsSuccess)
        {
            _changesCache.Invalidate(ProjectPath);
            IsStashViewVisible = false;
            SelectedStash = null;
            await LoadChangesAsync(bypassThrottle: true, invalidateCache: true, refreshMetadata: true, forceMetadataRefresh: false);
            await LoadStashesAsync();
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
            CommitCommand.NotifyCanExecuteChanged();
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
            CommitCommand.NotifyCanExecuteChanged();
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

    private static string GetAbsoluteProjectFilePath(string projectPath, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return projectPath;

        if (Path.IsPathRooted(filePath))
            return Path.GetFullPath(filePath);

        var normalizedRelativePath = filePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.GetFullPath(Path.Combine(projectPath, normalizedRelativePath));
    }

    /// <summary>
    /// Forzar un refresco si es necesario (ej: al recuperar foco).
    /// Evita refrescar si hubo uno hace menos de 5 segundos para prevenir bucles.
    /// </summary>
    public async Task RefreshIfNecessaryAsync()
    {
        if (string.IsNullOrEmpty(ProjectPath)) return;

        var diff = DateTime.Now - _lastRefreshTime;
        var minSeconds = IsWslPath(ProjectPath) ? 2 : 5;
        if (diff.TotalSeconds > minSeconds)
        {
            await LoadChangesAsync(
                bypassThrottle: true,
                invalidateCache: true,
                refreshMetadata: !IsWslPath(ProjectPath),
                forceMetadataRefresh: false);
        }
    }

    /// <summary>
    /// Limpia recursos cuando se destruye el ViewModel.
    /// </summary>
    public void Dispose()
    {
        StopWslPolling();
        _diffLoadCts?.Cancel();
        _diffLoadCts?.Dispose();
        _changeWatcher?.Dispose();
        _loadCts?.Cancel();
        _loadCts?.Dispose();
    }

    #endregion
}





