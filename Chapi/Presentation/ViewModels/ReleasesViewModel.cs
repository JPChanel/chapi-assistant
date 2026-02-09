using Chapi.Application.UseCases.Git;
using Chapi.Domain.Models;
using Chapi.Presentation.Views.Dialogs;
using System.Collections.ObjectModel;

namespace Chapi.Presentation.ViewModels;

public class ReleasesViewModel : ViewModelBase
{
    private readonly LoadReleasesUseCase _loadReleasesUseCase;
    private readonly CreateTagUseCase _createTagUseCase;
    private readonly DeleteTagUseCase _deleteTagUseCase;
    private readonly GetFilesChangedInCommitUseCase _getFilesChangedUseCase;
    private readonly GetCommitStatsUseCase _getCommitStatsUseCase;
    private string _projectPath = string.Empty;
    private bool _isLoading;
    private GitTagItem? _selectedRelease;
    private ObservableCollection<string> _releaseNotes = new();
    private ObservableCollection<string> _releaseFiles = new();
    private int _filesCount;
    private int _additions;
    private int _deletions;

    public ReleasesViewModel(
        LoadReleasesUseCase loadReleasesUseCase,
        CreateTagUseCase createTagUseCase,
        DeleteTagUseCase deleteTagUseCase,
        GetFilesChangedInCommitUseCase getFilesChangedUseCase,
        GetCommitStatsUseCase getCommitStatsUseCase)
    {
        _loadReleasesUseCase = loadReleasesUseCase;
        _createTagUseCase = createTagUseCase;
        _deleteTagUseCase = deleteTagUseCase;
        _getFilesChangedUseCase = getFilesChangedUseCase;
        _getCommitStatsUseCase = getCommitStatsUseCase;
        Releases = new ObservableCollection<GitTagItem>();
        LoadReleasesCommand = new AsyncRelayCommand(async _ => await LoadReleasesAsync());
        CreateTagCommand = new AsyncRelayCommand(async _ => await CreateTagAsync());
        DeleteTagCommand = new AsyncRelayCommand(async param => await DeleteTagAsync(param));
    }

    public ObservableCollection<GitTagItem> Releases { get; }
    
    public ObservableCollection<string> ReleaseNotes
    {
        get => _releaseNotes;
        set => SetProperty(ref _releaseNotes, value);
    }

    public ObservableCollection<string> ReleaseFiles
    {
        get => _releaseFiles;
        set => SetProperty(ref _releaseFiles, value);
    }

    public int FilesCount
    {
        get => _filesCount;
        set => SetProperty(ref _filesCount, value);
    }

    public int Additions
    {
        get => _additions;
        set => SetProperty(ref _additions, value);
    }

    public int Deletions
    {
        get => _deletions;
        set => SetProperty(ref _deletions, value);
    }

    public string ProjectPath
    {
        get => _projectPath;
        set
        {
            if (SetProperty(ref _projectPath, value))
            {
                _ = LoadReleasesAsync();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public GitTagItem? SelectedRelease
    {
        get => _selectedRelease;
        set
        {
            if (SetProperty(ref _selectedRelease, value))
            {
                UpdateReleaseDetails();
            }
        }
    }

    public AsyncRelayCommand LoadReleasesCommand { get; }
    public AsyncRelayCommand CreateTagCommand { get; }
    public AsyncRelayCommand DeleteTagCommand { get; }

    public async Task LoadReleasesAsync()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath)) return;

        IsLoading = true;
        try
        {
            var releases = await _loadReleasesUseCase.ExecuteAsync(ProjectPath);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                Releases.Clear();
                foreach (var release in releases)
                {
                    Releases.Add(release);
                }
                
                if (Releases.Any() && SelectedRelease == null)
                {
                    SelectedRelease = Releases.First();
                }
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task CreateTagAsync()
    {
        if (string.IsNullOrEmpty(ProjectPath)) return;

        var (okTag, tagName) = await Infrastructure.Services.DialogService.ShowInputDialog("Crear Tag", "Ingrese el nombre del tag (ej: v1.0.0):");
        if (!okTag || string.IsNullOrWhiteSpace(tagName)) return;

        var (okMsg, tagMessage) = await Infrastructure.Services.DialogService.ShowInputDialog("Mensaje del Tag", "Ingrese un mensaje para el tag:", $"Release {tagName}");
        if (!okMsg || string.IsNullOrWhiteSpace(tagMessage)) return;

        var result = await _createTagUseCase.ExecuteAsync(ProjectPath, tagName, tagMessage);
        if (result.IsSuccess)
        {
            await LoadReleasesAsync();
        }
    }

    public event EventHandler? TagDeleted;

    private async Task DeleteTagAsync(object? parameter)
    {
        string? tagName = parameter switch
        {
            string s => s,
            GitTagItem tag => tag.TagName,
            _ => SelectedRelease?.TagName
        };

        if (string.IsNullOrEmpty(tagName) || string.IsNullOrEmpty(ProjectPath)) return;

        var confirm = await Infrastructure.Services.DialogService.ShowConfirmDialog("Eliminar Tag", $"¿Estas seguro de eliminar el tag '{tagName}'?",DialogVariant.Warning, DialogType.Confirm);
        if (!confirm) return;

        var result = await _deleteTagUseCase.ExecuteAsync(ProjectPath, tagName);
        if (result.IsSuccess)
        {
            await LoadReleasesAsync();
            TagDeleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private async void UpdateReleaseDetails()
    {
        ReleaseNotes.Clear();
        ReleaseFiles.Clear();
        FilesCount = 0;
        Additions = 0;
        Deletions = 0;

        if (SelectedRelease == null) return;

        // Notas de Versión
        var details = !string.IsNullOrWhiteSpace(SelectedRelease.TagMessage) 
            ? SelectedRelease.TagMessage 
            : SelectedRelease.CommitMessage;
            
        if (!string.IsNullOrEmpty(details))
        {
            var lines = details.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                ReleaseNotes.Add(line.Trim());
            }
        }

        // Cargar Archivos Modificados
        if (!string.IsNullOrEmpty(ProjectPath) && !string.IsNullOrEmpty(SelectedRelease.CommitHash))
        {
            try
            {
                var files = await _getFilesChangedUseCase.ExecuteAsync(ProjectPath, SelectedRelease.CommitHash);
                foreach (var file in files)
                {
                    ReleaseFiles.Add(file);
                }
                FilesCount = ReleaseFiles.Count;
                
                // Obtener estadísticas reales (additions/deletions)
                var (adds, dels) = await _getCommitStatsUseCase.ExecuteAsync(ProjectPath, SelectedRelease.CommitHash);
                Additions = adds;
                Deletions = dels;
            }
            catch { }
        }
    }
}
