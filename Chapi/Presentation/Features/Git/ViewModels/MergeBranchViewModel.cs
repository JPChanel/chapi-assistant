using CommunityToolkit.Mvvm.Input;
using Chapi.Domain.Interfaces;
using MaterialDesignThemes.Wpf;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Chapi.Presentation.Shared.Mvvm;

namespace Chapi.Presentation.Features.Git.ViewModels;

public class MergeBranchViewModel : ViewModelBase
{
    private readonly IGitRepository _gitRepository;
    private readonly string _projectPath;
    private bool _isDeleteOptionVisible = true;
    private string _currentBranch = string.Empty;
    private BranchItemViewModel? _selectedBranch;
    private string _searchText = string.Empty;
    private bool _isCheckingStatus;
    private bool _hasConflicts;
    private bool _isUpToDate;
    private string _statusMessage = string.Empty;
    private string _actionButtonText = "Crear commit de fusion";
    private string _mergeDescription = string.Empty;

    public MergeBranchViewModel(IGitRepository gitRepository, string projectPath, string mergeType)
    {
        _gitRepository = gitRepository;
        _projectPath = projectPath;
        MergeType = mergeType;
        UpdateActionButtonText();

        ConfirmCommand = new RelayCommand(ExecuteMerge, () => CanMerge);
        CloseCommand = DialogHost.CloseDialogCommand;
    }

    // Constructor sin parametros para el disenador XAML.
    public MergeBranchViewModel()
    {
        _gitRepository = null!;
        _projectPath = string.Empty;
        ConfirmCommand = new RelayCommand(ExecuteMerge, () => CanMerge);
        CloseCommand = DialogHost.CloseDialogCommand;
    }

    public bool IsDeleteSourceBranchChecked { get; set; } = true;

    public bool IsDeleteOptionVisible
    {
        get => _isDeleteOptionVisible;
        set => SetProperty(ref _isDeleteOptionVisible, value);
    }

    public string CurrentBranch
    {
        get => _currentBranch;
        set => SetProperty(ref _currentBranch, value);
    }

    public BranchItemViewModel? SelectedBranch
    {
        get => _selectedBranch;
        set
        {
            if (SetProperty(ref _selectedBranch, value))
            {
                OnPropertyChanged(nameof(CanMerge));
                ConfirmCommand.NotifyCanExecuteChanged();
                _ = CheckMergeStatusAsync();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                FilterBranches();
            }
        }
    }

    public bool IsCheckingStatus
    {
        get => _isCheckingStatus;
        set
        {
            if (SetProperty(ref _isCheckingStatus, value))
            {
                OnPropertyChanged(nameof(CanMerge));
                ConfirmCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasConflicts
    {
        get => _hasConflicts;
        set
        {
            if (SetProperty(ref _hasConflicts, value))
            {
                OnPropertyChanged(nameof(CanMerge));
                ConfirmCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsUpToDate
    {
        get => _isUpToDate;
        set
        {
            if (SetProperty(ref _isUpToDate, value))
            {
                OnPropertyChanged(nameof(CanMerge));
                ConfirmCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string ActionButtonText
    {
        get => _actionButtonText;
        set => SetProperty(ref _actionButtonText, value);
    }

    public string MergeDescription
    {
        get => _mergeDescription;
        set => SetProperty(ref _mergeDescription, value);
    }

    public bool CanMerge => SelectedBranch != null && !IsCheckingStatus && !HasConflicts && !IsUpToDate;

    public ObservableCollection<BranchItemViewModel> AllBranches { get; } = [];
    public ObservableCollection<BranchItemViewModel> FilteredBranches { get; } = [];

    public string MergeType { get; set; } = "Merge";

    public IRelayCommand ConfirmCommand { get; }
    public ICommand CloseCommand { get; }

    public bool DialogResultOK { get; private set; }

    private void UpdateActionButtonText()
    {
        switch (MergeType)
        {
            case "Squash":
                ActionButtonText = "Squash y Merge";
                MergeDescription = "Combina todos tus commits en uno solo. Tu historial individual se pierde, pero el destino queda mas limpio.";
                IsDeleteOptionVisible = true;
                break;
            case "Rebase":
                ActionButtonText = "Rebase";
                MergeDescription = "Actualiza tu rama actual integrando los ultimos cambios de la rama seleccionada. Reescribe el historial para que sea lineal.";
                IsDeleteOptionVisible = false;
                break;
            default:
                ActionButtonText = "Create a merge commit";
                MergeDescription = "Crea un nuevo commit de union que conserva la historia completa de ambas ramas.";
                IsDeleteOptionVisible = true;
                break;
        }
    }

    private async Task CheckMergeStatusAsync()
    {
        if (SelectedBranch == null || _gitRepository == null)
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

        try
        {
            if (MergeType == "Rebase")
            {
                var protectedBranches = new[] { "main", "master", "production", "prod" };
                if (protectedBranches.Contains(CurrentBranch.ToLowerInvariant()))
                {
                    StatusMessage = $"Proteccion: no esta permitido hacer rebase sobre '{CurrentBranch}'.";
                    HasConflicts = true;
                    return;
                }
            }

            var (conflicts, message) = await _gitRepository.CheckMergeConflictsAsync(_projectPath, SelectedBranch.Name);

            if (conflicts)
            {
                HasConflicts = true;

                if (message == "DIRTY_WORKTREE" || message.Contains("overwritten") || message.Contains("changes"))
                {
                    StatusMessage = "Cambios locales detectados. Haz commit o stash primero.";
                }
                else if (MergeType == "Rebase")
                {
                    StatusMessage = "Conflictos detectados. El rebase fallara. Sincroniza o resuelve conflictos primero.";
                }
                else
                {
                    StatusMessage = $"Conflictos detectados. Intenta sincronizar '{SelectedBranch.Name}' en '{CurrentBranch}' primero.";
                }
            }
            else
            {
                StatusMessage = MergeType == "Rebase"
                    ? $"Listo para rebasear '{CurrentBranch}' sobre '{SelectedBranch.Name}'."
                    : $"Listo para fusionar '{CurrentBranch}' en '{SelectedBranch.Name}'.";
            }
        }
        catch
        {
            StatusMessage = "No se pudo verificar el estado de la fusion.";
        }
        finally
        {
            IsCheckingStatus = false;
        }
    }

    private void ExecuteMerge()
    {
        if (SelectedBranch == null)
        {
            return;
        }

        DialogResultOK = true;
        DialogHost.CloseDialogCommand.Execute(SelectedBranch, null);
    }

    private void FilterBranches()
    {
        FilteredBranches.Clear();
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? AllBranches
            : AllBranches.Where(branch => branch.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (var branch in filtered)
        {
            FilteredBranches.Add(branch);
        }
    }

    public void LoadBranches(IEnumerable<string> branches, string currentBranch)
    {
        CurrentBranch = currentBranch;
        AllBranches.Clear();

        var branchList = branches
            .Where(branch => !branch.Equals(currentBranch, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var branch in branchList)
        {
            var isDefault = branch.Equals("main", StringComparison.OrdinalIgnoreCase) ||
                            branch.Equals("master", StringComparison.OrdinalIgnoreCase);

            AllBranches.Add(new BranchItemViewModel
            {
                Name = branch,
                IsDefault = isDefault,
                LastCommitTime = isDefault ? "Default branch" : string.Empty
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
