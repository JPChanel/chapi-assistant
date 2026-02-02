using Chapi.Application.UseCases.Git;
using Chapi.Domain.Entities;
using System.Collections.ObjectModel;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Chapi.Presentation.Views.Dialogs;
using Chapi.Infrastructure.Git;

using Chapi.Infrastructure.Services;
using Chapi.Infrastructure.Persistence.Settings;
namespace Chapi.Presentation.ViewModels;

/// <summary>
/// ViewModel para la pestana de historial.
/// Maneja la lista de commits, archivos cambiados y diffs.
/// </summary>
public class HistoryViewModel : ViewModelBase
{
    private readonly LoadHistoryUseCase _loadHistoryUseCase;
    private readonly GetFilesChangedInCommitUseCase _getFilesUseCase;
    private readonly GetFileDiffUseCase _getFileDiffUseCase;
    private readonly CreateBranchUseCase _createBranchUseCase;
    private readonly CreateTagUseCase _createTagUseCase;
    private readonly Domain.Interfaces.IGitRepository _gitRepository;
    
    private string _projectPath = string.Empty;
    private bool _isLoading;
    private CommitItemViewModel? _selectedCommit;
    private string? _selectedFile;
    private string _commitDetailsInfo = string.Empty;
    private int _currentLimit = 100;
    private const int PageSize = 100;

    // ...

    public event EventHandler? ResetCompleted;

    public HistoryViewModel(
        LoadHistoryUseCase loadHistoryUseCase,
        GetFilesChangedInCommitUseCase getFilesUseCase,
        GetFileDiffUseCase getFileDiffUseCase,
        CreateBranchUseCase createBranchUseCase,
        CreateTagUseCase createTagUseCase,
        Domain.Interfaces.IGitRepository gitRepository)
    {
        _loadHistoryUseCase = loadHistoryUseCase;
        _getFilesUseCase = getFilesUseCase;
        _getFileDiffUseCase = getFileDiffUseCase;
        _createBranchUseCase = createBranchUseCase;
        _createTagUseCase = createTagUseCase;
        _gitRepository = gitRepository;
        
        Commits = new ObservableCollection<CommitItemViewModel>();
        FilesChanged = new ObservableCollection<string>();
        DiffLines = new ObservableCollection<DiffPiece>();
        
        LoadHistoryCommand = new AsyncRelayCommand(async _ => await LoadHistoryAsync());
        RefreshCommand = new AsyncRelayCommand(async _ => await ReloadHistoryAsync());
        LoadMoreCommand = new AsyncRelayCommand(async _ => await LoadMoreHistoryAsync());
        ResetSoftCommand = new AsyncRelayCommand(async param => 
        {
            if (param is CommitItemViewModel commit)
                await ResetSoftAsync(commit);
        });

        CreateBranchCommand = new AsyncRelayCommand(async param => 
        {
            if (param is string hash) await CreateBranchAsync(hash);
            else if (param is CommitItemViewModel commit) await CreateBranchAsync(commit.Hash);
        });

        CreateTagCommand = new AsyncRelayCommand(async param => 
        {
            if (param is string hash) await CreateTagAsync(hash);
            else if (param is CommitItemViewModel commit) await CreateTagAsync(commit.Hash);
        });
    }

    #region Properties

    public ObservableCollection<CommitItemViewModel> Commits { get; }
    public ObservableCollection<string> FilesChanged { get; }
    public ObservableCollection<DiffPiece> DiffLines { get; }

    public string ProjectPath
    {
        get => _projectPath;
        set
        {
            if (SetProperty(ref _projectPath, value))
            {
                _ = LoadHistoryAsync();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public CommitItemViewModel? SelectedCommit
    {
        get => _selectedCommit;
        set
        {
            if (SetProperty(ref _selectedCommit, value))
            {
                UpdateCommitDetails();
                _ = LoadCommitFilesAsync();
            }
        }
    }

    public string? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (SetProperty(ref _selectedFile, value))
            {
                _ = LoadFileDiffAsync();
            }
        }
    }

    public string CommitDetailsInfo
    {
        get => _commitDetailsInfo;
        set => SetProperty(ref _commitDetailsInfo, value);
    }

    #endregion

    #region Commands

    public AsyncRelayCommand LoadHistoryCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand LoadMoreCommand { get; }
    public AsyncRelayCommand ResetSoftCommand { get; }
    public AsyncRelayCommand CreateBranchCommand { get; }
    public AsyncRelayCommand CreateTagCommand { get; }

    #endregion

    #region Methods

    public async Task LoadHistoryAsync()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath))
            return;

        IsLoading = true;
        try
        {
            var commits = await _loadHistoryUseCase.ExecuteAsync(ProjectPath, _currentLimit);

            Commits.Clear();
            foreach (var commit in commits)
            {
                Commits.Add(MapToViewModel(commit));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task ReloadHistoryAsync()
    {
        _currentLimit = PageSize;
        await LoadHistoryAsync();
    }

    private async Task LoadMoreHistoryAsync()
    {
        _currentLimit += PageSize;
        await LoadHistoryAsync();
    }

    private void UpdateCommitDetails()
    {
        if (SelectedCommit == null)
        {
            CommitDetailsInfo = string.Empty;
            return;
        }

        CommitDetailsInfo = $"{SelectedCommit.Author} comitio {SelectedCommit.ShortHash} ({SelectedCommit.RelativeDate})";
    }

    private async Task LoadCommitFilesAsync()
    {
        FilesChanged.Clear();
        SelectedFile = null;
        DiffLines.Clear();

        if (SelectedCommit == null || string.IsNullOrEmpty(ProjectPath))
            return;

        var files = await _getFilesUseCase.ExecuteAsync(ProjectPath, SelectedCommit.Hash);
        foreach (var file in files)
        {
            FilesChanged.Add(file);
        }
    }

    private async Task LoadFileDiffAsync()
    {
        DiffLines.Clear();

        if (SelectedCommit == null || string.IsNullOrEmpty(SelectedFile) || string.IsNullOrEmpty(ProjectPath))
            return;

        try
        {
            var (oldText, newText) = await _getFileDiffUseCase.ExecuteAsync(ProjectPath, SelectedFile, SelectedCommit.Hash);

            var diffBuilder = new InlineDiffBuilder(new DiffPlex.Differ());
            var diff = diffBuilder.BuildDiffModel(oldText, newText);

            // Aplicar logica de filtrado de Hunks
            var filteredLines = FilterHunks(diff.Lines);
            foreach (var line in filteredLines)
            {
                DiffLines.Add(line);
            }
        }
        catch (Exception ex)
        {
            DiffLines.Add(new DiffPiece($"ERROR AL CARGAR DIFF: {ex.Message}", ChangeType.Deleted, null));
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

    private CommitItemViewModel MapToViewModel(GitCommit commit)
    {
        return new CommitItemViewModel
        {
            Hash = commit.Hash,
            ShortHash = commit.ShortHash,
            Message = commit.Message,
            Description = commit.Description,
            Author = commit.Author,
            Date = commit.Date,
            RelativeDate = commit.RelativeDate,
            IsSynced = !commit.IsUnpushed,
            Tags = new ObservableCollection<string>(commit.Tags)
        };
    }

    private async Task ResetSoftAsync(CommitItemViewModel? commit)
    {
        if (commit == null || string.IsNullOrEmpty(ProjectPath)) return;

        // Confirmar con el usuario
        var confirm = await DialogService.ShowConfirmDialog(
            "Deshacer  šltimo Commit",
            $"Â¿Estas seguro de deshacer el commit '{commit.ShortHash}'?\n\nLos cambios se mantendran en el area de trabajo.",
            DialogVariant.Warning,
            DialogType.Confirm);

        if (!confirm) return;

        // Ejecutar reset soft
        var result = await _gitRepository.ExecuteGitCommandAsync(ProjectPath, $"reset --soft {commit.Hash}^");
        
        if (!result.Contains("fatal:") && !result.Contains("error:"))
        {
            Msg.Assistant($"âœ… Commit '{commit.ShortHash}' deshecho. Los cambios estan en el area de trabajo.");
            await ReloadHistoryAsync();
            
            // Notificar que se completo el reset para que los cambios se actualicen
            ResetCompleted?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            await DialogService.ShowConfirmDialog(
                "Error",
                $"No se pudo deshacer el commit:\n{result}",
                DialogVariant.Error,
                DialogType.Info);
        }
    }

    private async Task CreateBranchAsync(string commitHash)
    {
        if (string.IsNullOrEmpty(ProjectPath) || string.IsNullOrEmpty(commitHash)) return;

        var (ok, branchName) = await DialogService.ShowInputDialog("Crear Rama", "Ingresa el nombre de la nueva rama:");
        if (!ok || string.IsNullOrWhiteSpace(branchName)) return;

        var result = await _createBranchUseCase.ExecuteAsync(ProjectPath, branchName, commitHash);
        if (result.IsSuccess)
        {
            // Notificar que se creo una rama para actualizar combos si es necesario
            // En este caso, MainWindow deberia refrescar sus ramas.
            ResetCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task CreateTagAsync(string commitHash)
    {
        if (string.IsNullOrEmpty(ProjectPath) || string.IsNullOrEmpty(commitHash)) return;

        // Verificar si la rama esta publicada (requerimiento del usuario)
        string currentBranch = await _gitRepository.GetCurrentBranchAsync(ProjectPath);
        bool isPublished = await _gitRepository.HasUpstreamAsync(ProjectPath, currentBranch);
        
        if (!isPublished)
        {
            await DialogService.ShowConfirmDialog("Rama no publicada", 
                "Debes publicar la rama antes de crear etiquetas (tags) para asegurar la consistencia con el servidor.", 
                DialogVariant.Warning, DialogType.Info);
            return;
        }

        var (ok, tagName) = await DialogService.ShowInputDialog("Crear Etiqueta (Tag)", "Ingresa el nombre del tag:");
        if (!ok || string.IsNullOrWhiteSpace(tagName)) return;

        var (okMsg, message) = await DialogService.ShowInputDialog("Mensaje del Tag", "Ingresa un mensaje para el tag anotado:", tagName);
        if (!okMsg) return;

        var result = await _createTagUseCase.ExecuteAsync(ProjectPath, tagName, message, commitHash);
        if (result.IsSuccess)
        {
            await ReloadHistoryAsync();
        }
    }

    #endregion
}








