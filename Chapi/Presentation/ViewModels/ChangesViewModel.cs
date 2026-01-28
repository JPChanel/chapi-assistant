using Chapi.Application.UseCases.Git;
using Chapi.Domain.Entities;
using MaterialDesignThemes.Wpf;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace Chapi.Presentation.ViewModels;

/// <summary>
/// ViewModel para la pestaña de cambios.
/// Maneja la lista de archivos modificados y comandos relacionados.
/// </summary>
public class ChangesViewModel : ViewModelBase
{
    private readonly LoadChangesUseCase _loadChangesUseCase;
    private readonly CommitChangesUseCase _commitChangesUseCase;
    private string _projectPath = string.Empty;
    private int _totalAdditions;
    private int _totalDeletions;
    private string _commitSummary = string.Empty;
    private string _commitDescription = string.Empty;

    public ChangesViewModel(
        LoadChangesUseCase loadChangesUseCase,
        CommitChangesUseCase commitChangesUseCase)
    {
        _loadChangesUseCase = loadChangesUseCase;
        _commitChangesUseCase = commitChangesUseCase;
        
        Changes = new ObservableCollection<ChangeItemViewModel>();
        
        LoadChangesCommand = new AsyncRelayCommand(async _ => await LoadChangesAsync());
        CommitCommand = new AsyncRelayCommand(async _ => await CommitAsync(), _ => CanCommit());
        SelectAllCommand = new RelayCommand(_ => SelectAll());
        DeselectAllCommand = new RelayCommand(_ => DeselectAll());
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

    #endregion

    #region Commands

    public AsyncRelayCommand LoadChangesCommand { get; }
    public AsyncRelayCommand CommitCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand DeselectAllCommand { get; }

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
            Changes.Add(viewModel);
            
            totalAdd += viewModel.Additions;
            totalDel += viewModel.Deletions;
        }

        TotalAdditions = totalAdd;
        TotalDeletions = totalDel;
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

    /// <summary>
    /// Selecciona todos los cambios.
    /// </summary>
    private void SelectAll()
    {
        foreach (var change in Changes)
        {
            change.IsSelected = true;
        }
    }

    /// <summary>
    /// Deselecciona todos los cambios.
    /// </summary>
    private void DeselectAll()
    {
        foreach (var change in Changes)
        {
            change.IsSelected = false;
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
            Deletions = fileChange.Deletions
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
