using Chapi.Domain.Interfaces;
using Chapi.Domain.Models; // Para Result
using MaterialDesignThemes.Wpf;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace Chapi.Presentation.ViewModels;

public class MergeBranchViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private readonly IGitRepository _gitRepository;
    private readonly string _projectPath;
    private string _currentBranch = string.Empty;
    private BranchItemViewModel? _selectedBranch;
    private string _searchText = string.Empty;
    
    // Estado del Merge
    private bool _isCheckingStatus;
    private bool _hasConflicts;
    private bool _isUpToDate;
    private string _statusMessage = string.Empty;
    private string _actionButtonText = "Crear commit de fusión";

    public MergeBranchViewModel(IGitRepository gitRepository, string projectPath, string mergeType)
    {
        _gitRepository = gitRepository;
        _projectPath = projectPath;
        MergeType = mergeType;
        UpdateActionButtonText();

        ConfirmCommand = new RelayCommand(_ => ExecuteMerge(), _ => CanMerge);
        CloseCommand = DialogHost.CloseDialogCommand;
    }

    // Constructor sin parámetros para el diseñador XAML (opcional)
    public MergeBranchViewModel() { }

    public string CurrentBranch
    {
        get => _currentBranch;
        set { _currentBranch = value; OnPropertyChanged(nameof(CurrentBranch)); }
    }

    public BranchItemViewModel? SelectedBranch
    {
        get => _selectedBranch;
        set 
        { 
            _selectedBranch = value; 
            OnPropertyChanged(nameof(SelectedBranch));
            OnPropertyChanged(nameof(CanMerge));
            _ = CheckMergeStatusAsync(); // Verificar estado al seleccionar
        }
    }

    public string SearchText
    {
        get => _searchText;
        set 
        { 
            _searchText = value; 
            OnPropertyChanged(nameof(SearchText));
            FilterBranches();
        }
    }

    public bool IsCheckingStatus
    {
        get => _isCheckingStatus;
        set 
        { 
            _isCheckingStatus = value; 
            OnPropertyChanged(nameof(IsCheckingStatus)); 
            OnPropertyChanged(nameof(CanMerge)); // Actualizar estado del botón
        }
    }

    public bool HasConflicts
    {
        get => _hasConflicts;
        set 
        { 
            _hasConflicts = value; 
            OnPropertyChanged(nameof(HasConflicts)); 
            OnPropertyChanged(nameof(CanMerge)); // Actualizar estado del botón
        }
    }

    public bool IsUpToDate
    {
        get => _isUpToDate;
        set 
        { 
            _isUpToDate = value; 
            OnPropertyChanged(nameof(IsUpToDate)); 
            OnPropertyChanged(nameof(CanMerge)); // Actualizar estado del botón
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
    }

    public string ActionButtonText
    {
        get => _actionButtonText;
        set { _actionButtonText = value; OnPropertyChanged(nameof(ActionButtonText)); }
    }

    public bool CanMerge => SelectedBranch != null && !IsCheckingStatus && !HasConflicts && !IsUpToDate;

    public ObservableCollection<BranchItemViewModel> AllBranches { get; } = new();
    public ObservableCollection<BranchItemViewModel> FilteredBranches { get; } = new();

    public string MergeType { get; set; } = "Merge"; // "Merge", "Squash", "Rebase"
    
    public ICommand ConfirmCommand { get; }
    public ICommand CloseCommand { get; }

    // Resultado para devolver a la vista principal
    public bool DialogResultOK { get; private set; }

    private void UpdateActionButtonText()
    {
        // El texto ahora refleja que vamos A la rama seleccionada
        ActionButtonText = MergeType switch
        {
            "Squash" => "Squash y Merge",
            "Rebase" => "Rebase",
            _ => "Create a merge commit"
        };
    }

    private async Task CheckMergeStatusAsync()
    {
        if (SelectedBranch == null)
        {
            StatusMessage = string.Empty;
            HasConflicts = false;
            IsUpToDate = false;
            return;
        }

        IsCheckingStatus = true;
        StatusMessage = "Verificando compatibilidad...";
        HasConflicts = false;
        IsUpToDate = false;
        ((RelayCommand)ConfirmCommand).RaiseCanExecuteChanged();

        try
        {
            if (MergeType != "Rebase") 
            {
                // VALIDACIÓN INTELIGENTE:
                // Estamos validando "Current -> Selected".
                var (conflicts, msg) = await _gitRepository.CheckMergeConflictsAsync(_projectPath, SelectedBranch.Name);
                
                if (conflicts)
                {
                    HasConflicts = true;
                    
                    if (msg == "DIRTY_WORKTREE" || msg.Contains("overwritten") || msg.Contains("changes"))
                    {
                         StatusMessage = "⚠️ Cambios locales detectados. Por favor haz commit o stash primero.";
                    }
                    else
                    {
                         StatusMessage = $"Conflictos detectados. Intenta sincronizar '{SelectedBranch.Name}' en '{CurrentBranch}' primero.";
                    }
                }
                else
                {
                    // Si no hay conflictos trayendo el destino, es seguro intentar enviar.
                    StatusMessage = $"Listo para fusionar '{CurrentBranch}' en '{SelectedBranch.Name}'.";
                }
            }
            else
            {
                StatusMessage = "El rebase reescribirá el historial de commits.";
            }
        }
        catch
        {
            StatusMessage = "No se pudo verificar el estado de la fusión.";
        }
        finally
        {
            IsCheckingStatus = false;
            ((RelayCommand)ConfirmCommand).RaiseCanExecuteChanged();
        }
    }

    private void ExecuteMerge()
    {
        if (SelectedBranch != null)
        {
            DialogResultOK = true;
            DialogHost.CloseDialogCommand.Execute(SelectedBranch, null);
        }
    }

    private void FilterBranches()
    {
        FilteredBranches.Clear();
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? AllBranches
            : AllBranches.Where(b => b.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (var branch in filtered)
        {
            FilteredBranches.Add(branch);
        }
    }

    public void LoadBranches(IEnumerable<string> branches, string currentBranch)
    {
        CurrentBranch = currentBranch;
        AllBranches.Clear();

        var branchList = branches.Where(b => !b.Equals(currentBranch, StringComparison.OrdinalIgnoreCase)).ToList();
        
        foreach (var branch in branchList)
        {
            bool isDefault = branch.Equals("main", StringComparison.OrdinalIgnoreCase) || 
                           branch.Equals("master", StringComparison.OrdinalIgnoreCase);
            
            AllBranches.Add(new BranchItemViewModel
            {
                Name = branch,
                IsDefault = isDefault,
                LastCommitTime = isDefault ? "Default branch" : "" // Simplificado
            });
        }

        FilterBranches();
    }
}

public class BranchItemViewModel
{
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public string LastCommitTime { get; set; } = string.Empty;
}

