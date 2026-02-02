using Chapi.Application.UseCases.Git;
using Chapi.Domain.Entities;
using Chapi.Infrastructure.Git;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using MaterialDesignThemes.Wpf;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using Chapi.Infrastructure.Services;
using Chapi.Infrastructure.AI;

using Chapi.Presentation.Views.Dialogs;

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

    public event EventHandler? CommitCompleted;

    public ChangesViewModel(
        LoadChangesUseCase loadChangesUseCase,
        CommitChangesUseCase commitChangesUseCase,
        DiscardChangesUseCase discardChangesUseCase,
        StashChangesUseCase stashChangesUseCase,
        StashPopUseCase stashPopUseCase,
        StashDropUseCase stashDropUseCase,
        StashClearUseCase stashClearUseCase,
        GetFileDiffUseCase getFileDiffUseCase,
        Domain.Interfaces.IGitRepository gitRepository)
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
        
        Changes = new ObservableCollection<ChangeItemViewModel>();
        Stashes = new ObservableCollection<GitStash>();
        StashedFiles = new ObservableCollection<ChangeItemViewModel>();
        DiffLines = new ObservableCollection<DiffPiece>();
        
        LoadChangesCommand = new AsyncRelayCommand(async _ => await LoadChangesAsync());
        CommitCommand = new AsyncRelayCommand(async _ => await CommitAsync(), _ => CanCommit());
        SelectAllCommand = new RelayCommand(_ => SelectAll());
        DeselectAllCommand = new RelayCommand(_ => DeselectAll());
        
        DiscardCommand = new AsyncRelayCommand(async param => await DiscardAsync(param as ChangeItemViewModel));
        StashSelectedCommand = new AsyncRelayCommand(async param => await StashSelectedAsync(param as ChangeItemViewModel));
        PopStashCommand = new AsyncRelayCommand(async param => await PopStashAsync(param as GitStash));
        DropStashCommand = new AsyncRelayCommand(async param => await DropStashAsync(param as GitStash));
        ClearStashesCommand = new AsyncRelayCommand(async _ => await ClearStashesAsync());
        RestoreFileFromStashCommand = new AsyncRelayCommand(async param => await RestoreFileFromStashAsync(param as ChangeItemViewModel));
        GenerateCommitMessageCommand = new AsyncRelayCommand(async _ => await GenerateCommitMessageAsync());
        DiscardAllCommand = new AsyncRelayCommand(async _ => await DiscardAllAsync());
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
                
                _ = LoadChangesAsync();
                _ = LoadStashesAsync();
            }
        }
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

        // Usar el Use Case para obtener cambios
        var fileChanges = await _loadChangesUseCase.ExecuteAsync(ProjectPath);

        // Mapear a ViewModels
        Changes.Clear();
        int totalAdd = 0;
        int totalDel = 0;

        foreach (var fileChange in fileChanges)
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
            
            totalAdd += viewModel.Additions;
            totalDel += viewModel.Deletions;
        }

        TotalAdditions = totalAdd;
        TotalDeletions = totalDel;
        OnPropertyChanged(nameof(AreAllSelected));
        OnPropertyChanged(nameof(SelectedCount));

        // Cargar stashes tambien
        await LoadStashesAsync();
    }

    /// <summary>
    /// Carga la lista de stashes.
    /// </summary>
    public async Task LoadStashesAsync()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath)) return;

        try
        {
            var currentBranch = await _gitRepository.GetCurrentBranchAsync(ProjectPath);
            var stashes = await _gitRepository.ListStashesAsync(ProjectPath);
            Stashes.Clear();
            foreach (var stash in stashes)
            {
                // Filtrar: mostrar si pertenece a la rama actual O si no se pudo determinar la rama ("Unknown")
                // Esto previene ocultar stashes antiguos sin formato "On branch", pero oculta los de otras ramas conocidas.
                if (stash.Branch == "Unknown" || stash.Branch == currentBranch)
                {
                    Stashes.Add(stash);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando stashes: {ex.Message}");
            Stashes.Clear();
        }
        finally
        {
            OnPropertyChanged(nameof(HasStashes));
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
                StashedFiles.Add(viewModel);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando archivos del stash: {ex.Message}");
        }
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
                 try { oldText = await _gitRepository.GetFileContentAsync(ProjectPath, "HEAD", SelectedChange.FilePath); } catch {}
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
            System.Diagnostics.Debug.WriteLine($"Error LoadDiffAsync: {ex.Message}");
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
            // Para stash, necesitamos comparar el archivo dentro del stash contra su version anterior en el mismo stash
            // O, simplemente visualizar que tiene el stash.
            // "stash show -p" da el diff contra el commit donde se hizo stash.
            
            // Estrategia: Obtener el diff crudo como antes, pero parsearlo es complejo.
            // Alternativa: Obtener contenido del archivo en el stash.
            // Alternativa: Obtener contenido del archivo en el stash.
            string newText = await _gitRepository.GetFileContentAsync(ProjectPath, SelectedStash.Name, SelectedStashedFile.FilePath);
            
            // Intentar obtener el contenido contra el que se compara (Parent del stash o HEAD al momento de stash)
            // Esto es mas complejo. Para simplificar y mantener consistencia visual,
            // podemos mostrar el contenido del stash como "Nuevo" y vacio como "Viejo" si es dificil obtener el base,
            // pero lo ideal es ver el DIFF.
            
            // Como fallback, volvemos a usar el comando git stash show -p y lo parseamos simple, 
            // O mejor:
            // oldText = file en stash^1 
            // newText = file en stash
            
            string oldText = string.Empty;
            // Intentar leer pariente
            try { oldText = await _gitRepository.GetFileContentAsync(ProjectPath, $"{SelectedStash.Name}^1", SelectedStashedFile.FilePath); } catch {}

            GenerateDiff(oldText, newText);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando diff del stash: {ex.Message}");
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
        
        var result = await _discardChangesUseCase.ExecuteAsync(ProjectPath, new[] { item.FilePath });
        if (result.IsSuccess)
        {
            await LoadChangesAsync();
        }
    }

    private async Task DiscardAllAsync()
    {
        if (string.IsNullOrEmpty(ProjectPath) || !Changes.Any()) return;
        
        var allFiles = Changes.Select(c => c.FilePath).ToArray();
        var result = await _discardChangesUseCase.ExecuteAsync(ProjectPath, allFiles);
        if (result.IsSuccess)
        {
            await LoadChangesAsync();
        }
    }

    private async Task StashSelectedAsync(ChangeItemViewModel? specificItem = null)
    {
        if (string.IsNullOrEmpty(ProjectPath)) return;

        List<string> filesToStash;
        string message;

        if (specificItem != null)
        {
            filesToStash = new List<string> { specificItem.FilePath };
            message = $"Stash: {specificItem.FileName}";
        }
        else
        {
            filesToStash = Changes.Where(c => c.IsSelected).Select(c => c.FilePath).ToList();
            message = "Stash manual";
        }

        if (!filesToStash.Any()) return;

        var result = await _stashChangesUseCase.ExecuteAsync(ProjectPath, message, filesToStash);
        if (result.IsSuccess)
        {
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
            await _gitRepository.ExecuteGitCommandAsync(ProjectPath, $"checkout {SelectedStash.Name} -- \"{item.FilePath}\"");
            
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
            await LoadChangesAsync();
        }
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
                var diff = await _gitRepository.ExecuteGitCommandAsync(ProjectPath, $"diff HEAD -- \"{file}\"");
                diffBuilder.AppendLine(diff);
            }

            var fullDiff = diffBuilder.ToString();
            if (string.IsNullOrWhiteSpace(fullDiff)) return;

            // Llamar a IA (usando helper existente por ahora para no romper nada)
            var prompt = Chapi.Infrastructure.AI.GetPrompt.GitCommit(fullDiff);
            string jsonResponse = await Chapi.Infrastructure.AI.AIClient.SendPromptAsync(prompt);

            if (!string.IsNullOrWhiteSpace(jsonResponse))
            {
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

    #endregion
}





