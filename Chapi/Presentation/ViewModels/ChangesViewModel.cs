using Chapi.Application.UseCases.Git;
using Chapi.Domain.Entities;
using Chapi.Helper.GitHelper;
using DiffPlex.DiffBuilder.Model;
using MaterialDesignThemes.Wpf;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using Chapi.Services;
using Chapi.Helper;
using Chapi.Views.Dialogs;

namespace Chapi.Presentation.ViewModels;

/// <summary>
/// ViewModel para la pestaña de cambios.
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
    private readonly GetFileDiffUseCase _getFileDiffUseCase;

    private string _projectPath = string.Empty;
    private int _totalAdditions;
    private int _totalDeletions;
    private string _commitSummary = string.Empty;
    private string _commitDescription = string.Empty;
    private ChangeItemViewModel? _selectedChange;
    private Git.StashEntry? _selectedStash;
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
        GetFileDiffUseCase getFileDiffUseCase)
    {
        _loadChangesUseCase = loadChangesUseCase;
        _commitChangesUseCase = commitChangesUseCase;
        _discardChangesUseCase = discardChangesUseCase;
        _stashChangesUseCase = stashChangesUseCase;
        _stashPopUseCase = stashPopUseCase;
        _stashDropUseCase = stashDropUseCase;
        _stashClearUseCase = stashClearUseCase;
        _getFileDiffUseCase = getFileDiffUseCase;
        
        Changes = new ObservableCollection<ChangeItemViewModel>();
        Stashes = new ObservableCollection<Git.StashEntry>();
        StashedFiles = new ObservableCollection<ChangeItemViewModel>();
        DiffLines = new ObservableCollection<DiffPiece>();
        
        LoadChangesCommand = new AsyncRelayCommand(async _ => await LoadChangesAsync());
        CommitCommand = new AsyncRelayCommand(async _ => await CommitAsync(), _ => CanCommit());
        SelectAllCommand = new RelayCommand(_ => SelectAll());
        DeselectAllCommand = new RelayCommand(_ => DeselectAll());
        
        DiscardCommand = new AsyncRelayCommand(async param => await DiscardAsync(param as ChangeItemViewModel));
        StashSelectedCommand = new AsyncRelayCommand(async _ => await StashSelectedAsync());
        PopStashCommand = new AsyncRelayCommand(async param => await PopStashAsync(param as Git.StashEntry));
        DropStashCommand = new AsyncRelayCommand(async param => await DropStashAsync(param as Git.StashEntry));
        ClearStashesCommand = new AsyncRelayCommand(async _ => await ClearStashesAsync());
        RestoreFileFromStashCommand = new AsyncRelayCommand(async param => await RestoreFileFromStashAsync(param as ChangeItemViewModel));
        GenerateCommitMessageCommand = new AsyncRelayCommand(async _ => await GenerateCommitMessageAsync());
    }

    #region Properties

    /// <summary>
    /// Colección de cambios en el repositorio.
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
                _ = LoadChangesAsync();
                _ = LoadStashesAsync();
            }
        }
    }

    /// <summary>
    /// Total de líneas añadidas.
    /// </summary>
    public int TotalAdditions
    {
        get => _totalAdditions;
        private set => SetProperty(ref _totalAdditions, value);
    }

    /// <summary>
    /// Total de líneas eliminadas.
    /// </summary>
    public int TotalDeletions
    {
        get => _totalDeletions;
        private set => SetProperty(ref _totalDeletions, value);
    }

    /// <summary>
    /// Indica si todos los cambios están seleccionados.
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
    /// Descripción detallada del commit.
    /// </summary>
    public string CommitDescription
    {
        get => _commitDescription;
        set => SetProperty(ref _commitDescription, value);
    }

    /// <summary>
    /// Colección de stashes.
    /// </summary>
    public ObservableCollection<Git.StashEntry> Stashes { get; }

    /// <summary>
    /// Líneas de diferencia del archivo seleccionado.
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
    /// Indica si la vista de stash está visible.
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
    public Git.StashEntry? SelectedStash
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
    /// Colección de archivos contenidos en el stash seleccionado.
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
    /// Indica si se está generando un mensaje de commit con IA.
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

        // Cargar stashes también
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
            var stashes = await Git.ListStashes(ProjectPath);
            Stashes.Clear();
            foreach (var stash in stashes)
            {
                Stashes.Add(stash);
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
    /// Carga el diff del archivo seleccionado.
    /// </summary>
    public async Task LoadDiffAsync()
    {
        DiffLines.Clear();
        if (SelectedChange == null || string.IsNullOrEmpty(ProjectPath)) return;

        try
        {
            var diff = await Git.EjecutarGit($"diff HEAD -- \"{SelectedChange.FilePath}\"", ProjectPath);
            if (string.IsNullOrWhiteSpace(diff))
            {
                diff = await Git.EjecutarGit($"diff --no-index /dev/null \"{SelectedChange.FilePath}\"", ProjectPath);
            }

            ParseDiffToLines(diff);
        }
        catch { /* Ignorar errores de carga de diff */ }
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
            var fileStatuses = await Git.GetFileStatusesForStash(SelectedStash.Name, ProjectPath);
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
    /// Carga el diff de un archivo dentro de un stash.
    /// </summary>
    public async Task LoadStashedFileDiffAsync()
    {
        DiffLines.Clear();
        if (SelectedStash == null || SelectedStashedFile == null || string.IsNullOrEmpty(ProjectPath)) return;

        try
        {
            // stash show -p stash@{n} -- filepath
            var diff = await Git.EjecutarGit($"stash show -p {SelectedStash.Name} -- \"{SelectedStashedFile.FilePath}\"", ProjectPath);
            ParseDiffToLines(diff);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando diff del stash: {ex.Message}");
        }
    }

    private void ParseDiffToLines(string diff)
    {
        var lines = diff.Split('\n');
        foreach (var line in lines)
        {
            var type = ChangeType.Unchanged;
            var text = line;

            if (line.StartsWith("+")) { type = ChangeType.Inserted; text = line.Substring(1); }
            else if (line.StartsWith("-")) { type = ChangeType.Deleted; text = line.Substring(1); }
            else if (line.StartsWith("@@")) type = ChangeType.Imaginary;

            DiffLines.Add(new DiffPiece(text, type, null));
        }
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
            
            // Notificar que se completó el commit para que el historial se actualice
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

    private async Task StashSelectedAsync()
    {
        if (string.IsNullOrEmpty(ProjectPath)) return;
        var selectedFiles = Changes.Where(c => c.IsSelected).Select(c => c.FilePath).ToList();
        if (!selectedFiles.Any()) return;

        var result = await _stashChangesUseCase.ExecuteAsync(ProjectPath, "Stash manual", selectedFiles);
        if (result.IsSuccess)
        {
            await LoadChangesAsync();
        }
    }

    private async Task PopStashAsync(Git.StashEntry? stash)
    {
        if (stash == null || string.IsNullOrEmpty(ProjectPath)) return;
        
        // Extraer índice del nombre "stash@{n}"
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
                Chapi.Views.Dialogs.DialogVariant.Error, DialogType.Info);
        }
    }

    private async Task RestoreFileFromStashAsync(ChangeItemViewModel? item)
    {
        if (item == null || SelectedStash == null || string.IsNullOrEmpty(ProjectPath)) return;

        try 
        {
            // git checkout stash@{n} -- <filepath>
            await Git.EjecutarGit($"checkout {SelectedStash.Name} -- \"{item.FilePath}\"", ProjectPath);
            
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

    private async Task DropStashAsync(Git.StashEntry? stash)
    {
        if (stash == null || string.IsNullOrEmpty(ProjectPath)) return;

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
                var diff = await Git.EjecutarGit($"diff HEAD -- \"{file}\"", ProjectPath);
                diffBuilder.AppendLine(diff);
            }

            var fullDiff = diffBuilder.ToString();
            if (string.IsNullOrWhiteSpace(fullDiff)) return;

            // Llamar a IA (usando helper existente por ahora para no romper nada)
            var prompt = Chapi.Helper.AI.GetPrompt.GitCommit(fullDiff);
            string jsonResponse = await AI.Clients.AIClient.SendPromptAsync(prompt);

            if (!string.IsNullOrWhiteSpace(jsonResponse))
            {
                try
                {
                    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var commitMsg = System.Text.Json.JsonSerializer.Deserialize<Chapi.Helper.Entities.CommitMessageResponse>(jsonResponse, options);
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

        // Asignar icono y color según el estado
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
            ChangeStatus.Added => "Añadido",
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
