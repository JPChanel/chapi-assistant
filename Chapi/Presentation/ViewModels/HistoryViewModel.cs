using Chapi.Application.UseCases.Git;
using Chapi.Domain.Entities;
using System.Collections.ObjectModel;

namespace Chapi.Presentation.ViewModels;

/// <summary>
/// ViewModel para la pestaña de historial.
/// Maneja la lista de commits y su carga.
/// </summary>
public class HistoryViewModel : ViewModelBase
{
    private readonly LoadHistoryUseCase _loadHistoryUseCase;
    private string _projectPath = string.Empty;
    private bool _isLoading;
    private CommitItemViewModel? _selectedCommit;

    private int _currentLimit = 50;
    private const int PageSize = 50;

    public HistoryViewModel(LoadHistoryUseCase loadHistoryUseCase)
    {
        _loadHistoryUseCase = loadHistoryUseCase;
        Commits = new ObservableCollection<CommitItemViewModel>();
        
        LoadHistoryCommand = new AsyncRelayCommand(async _ => await LoadHistoryAsync());
        RefreshCommand = new AsyncRelayCommand(async _ => await ReloadHistoryAsync());
        LoadMoreCommand = new AsyncRelayCommand(async _ => await LoadMoreHistoryAsync());
    }

    #region Properties

    /// <summary>
    /// Colección de commits en el historial.
    /// </summary>
    public ObservableCollection<CommitItemViewModel> Commits { get; }

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
                _ = LoadHistoryAsync();
            }
        }
    }

    /// <summary>
    /// Indica si se están cargando los commits.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    /// <summary>
    /// Commit seleccionado actualmente.
    /// </summary>
    public CommitItemViewModel? SelectedCommit
    {
        get => _selectedCommit;
        set => SetProperty(ref _selectedCommit, value);
    }

    #endregion

    #region Commands

    public AsyncRelayCommand LoadHistoryCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand LoadMoreCommand { get; }

    #endregion

    #region Methods

    /// <summary>
    /// Carga el historial de commits.
    /// </summary>
    /// <summary>
    /// Carga el historial usando el límite actual.
    /// </summary>
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

    private async Task ReloadHistoryAsync()
    {
        _currentLimit = PageSize;
        await LoadHistoryAsync();
    }

    private async Task LoadMoreHistoryAsync()
    {
        _currentLimit += PageSize;
        await LoadHistoryAsync();
    }

    private CommitItemViewModel MapToViewModel(GitCommit commit)
    {
        return new CommitItemViewModel
        {
            Hash = commit.Hash,
            ShortHash = commit.ShortHash,
            Message = commit.Message,
            Author = commit.Author,
            Date = commit.Date,
            RelativeDate = commit.RelativeDate,
            IsSynced = !commit.IsUnpushed
        };
    }

    #endregion
}
