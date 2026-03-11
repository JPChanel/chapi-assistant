using Chapi.Application.UseCases.Git;
using Chapi.Domain.Entities;
using Chapi.Infrastructure.Services;
using Chapi.Presentation.Views.Dialogs;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using System.Collections.ObjectModel;
using System.Linq;
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
    private bool _canLoadMore;

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

    public bool CanLoadMore
    {
        get => _canLoadMore;
        set => SetProperty(ref _canLoadMore, value);
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
            var commitList = commits.ToList();
            var viewModels = commitList.Select(MapToViewModel).ToList();
            ApplyLaneGraph(commitList, viewModels);

            CanLoadMore = commitList.Count >= _currentLimit;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Commits.Clear();
                foreach (var vm in viewModels)
                {
                    Commits.Add(vm);
                }

                SelectedCommit = Commits.FirstOrDefault();
            });
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
            GraphPrefix = commit.GraphPrefix,
            ShortHash = commit.ShortHash,
            Message = commit.Message,
            Description = commit.Description,
            Author = commit.Author,
            Date = commit.Date,
            RelativeDate = commit.RelativeDate,
            IsSynced = !commit.IsUnpushed,
            Tags = new ObservableCollection<string>(commit.Tags),
            LocalBranches = new ObservableCollection<string>(commit.LocalBranches),
            RemoteBranches = new ObservableCollection<string>(commit.RemoteBranches)
        };
    }

    private void ApplyLaneGraph(IReadOnlyList<GitCommit> commits, IReadOnlyList<CommitItemViewModel> viewModels)
    {
        const double laneSpacing = 12;
        const double rowHeight = 78;
        const double centerY = rowHeight / 2;
        const double padding = 10;
        const double nodeRadius = 5;
        const double overflow = 6;
        const double nodeGap = 7;
        const double badgeHeight = 18;
        var lanePalette = new[]
        {
            "#6EC1FF",
            "#F59E0B",
            "#F97316",
            "#22C55E",
            "#A78BFA",
            "#F43F5E"
        };
        var activeLanes = new List<string>();
        var rowStates = new List<(List<string> Before, List<string> After, int LaneIndex, List<string> Parents)>(commits.Count);
        var maxLaneCount = 1;

        foreach (var commit in commits)
        {
            if (!activeLanes.Contains(commit.Hash, StringComparer.OrdinalIgnoreCase))
                activeLanes.Insert(0, commit.Hash);

            var before = activeLanes.ToList();
            var laneIndex = before.FindIndex(hash => string.Equals(hash, commit.Hash, StringComparison.OrdinalIgnoreCase));
            if (laneIndex < 0)
            {
                laneIndex = 0;
                before.Insert(0, commit.Hash);
            }

            var parents = commit.ParentHashes
                .Where(hash => !string.IsNullOrWhiteSpace(hash))
                .ToList();

            var after = before.ToList();
            after.RemoveAt(laneIndex);

            if (parents.Count > 0)
            {
                after.Insert(laneIndex, parents[0]);

                for (var i = 1; i < parents.Count; i++)
                {
                    var parent = parents[i];
                    if (after.Contains(parent, StringComparer.OrdinalIgnoreCase))
                        continue;

                    after.Insert(Math.Min(laneIndex + i, after.Count), parent);
                }
            }

            var dedupedAfter = new List<string>();
            foreach (var hash in after)
            {
                if (!dedupedAfter.Contains(hash, StringComparer.OrdinalIgnoreCase))
                    dedupedAfter.Add(hash);
            }

            rowStates.Add((before, dedupedAfter, laneIndex, parents));
            maxLaneCount = Math.Max(maxLaneCount, Math.Max(before.Count, dedupedAfter.Count));
            activeLanes = dedupedAfter;
        }

        var graphStartX = padding;
        var graphWidth = Math.Max(42, graphStartX + Math.Max(0, maxLaneCount - 1) * laneSpacing + 20);

        for (var i = 0; i < viewModels.Count; i++)
        {
            var row = rowStates[i];
            var lines = new ObservableCollection<CommitGraphLineViewModel>();
            for (var lane = 0; lane < row.Before.Count; lane++)
            {
                var x = graphStartX + lane * laneSpacing;
                if (lane == row.LaneIndex)
                {
                    lines.Add(new CommitGraphLineViewModel
                    {
                        X1 = x,
                        Y1 = -overflow,
                        X2 = x,
                        Y2 = centerY - nodeGap,
                        Stroke = lanePalette[lane % lanePalette.Length]
                    });
                }
                else
                {
                    lines.Add(new CommitGraphLineViewModel
                    {
                        X1 = x,
                        Y1 = -overflow,
                        X2 = x,
                        Y2 = centerY,
                        Stroke = lanePalette[lane % lanePalette.Length]
                    });
                }
            }

            for (var lane = 0; lane < row.After.Count; lane++)
            {
                var x = graphStartX + lane * laneSpacing;
                var isNodeLane = lane == row.LaneIndex;
                var hasDiagonalFromNode = row.Parents.Any(parent =>
                {
                    var parentLane = row.After.FindIndex(hash => string.Equals(hash, parent, StringComparison.OrdinalIgnoreCase));
                    return parentLane == lane && Math.Abs((graphStartX + parentLane * laneSpacing) - (graphStartX + row.LaneIndex * laneSpacing)) > 0.01;
                });

                lines.Add(new CommitGraphLineViewModel
                {
                    X1 = x,
                    Y1 = isNodeLane && !hasDiagonalFromNode ? centerY + nodeGap : centerY,
                    X2 = x,
                    Y2 = rowHeight + overflow,
                    Stroke = lanePalette[lane % lanePalette.Length]
                });
            }

            var nodeX = graphStartX + row.LaneIndex * laneSpacing;
            var nodeColor = lanePalette[row.LaneIndex % lanePalette.Length];
            var badges = BuildBranchBadges(viewModels[i], nodeX, centerY, graphWidth, badgeHeight);
            foreach (var parent in row.Parents)
            {
                var parentLane = row.After.FindIndex(hash => string.Equals(hash, parent, StringComparison.OrdinalIgnoreCase));
                if (parentLane < 0)
                    continue;

                var parentX = graphStartX + parentLane * laneSpacing;
                if (Math.Abs(parentX - nodeX) < 0.01)
                    continue;

                lines.Add(new CommitGraphLineViewModel
                {
                    X1 = nodeX,
                    Y1 = centerY,
                    X2 = parentX,
                    Y2 = rowHeight,
                    Stroke = lanePalette[parentLane % lanePalette.Length]
                });
            }

            viewModels[i].GraphWidth = graphWidth;
            viewModels[i].NodeLeft = nodeX - nodeRadius;
            viewModels[i].NodeTop = centerY - nodeRadius;
            viewModels[i].IsMergeNode = row.Parents.Count > 1;
            viewModels[i].NodeFill = row.Parents.Count > 1 ? nodeColor : "Transparent";
            viewModels[i].NodeStroke = nodeColor;
            viewModels[i].GraphLines = lines;
            viewModels[i].BranchBadges = badges;
        }
    }

    private static ObservableCollection<CommitGraphBadgeViewModel> BuildBranchBadges(
        CommitItemViewModel viewModel,
        double nodeX,
        double centerY,
        double graphWidth,
        double badgeHeight)
    {
        var refs = viewModel.LocalBranches
            .Select(label => new { Label = label, IsRemote = false })
            .Concat(viewModel.RemoteBranches.Select(label => new { Label = label, IsRemote = true }))
            .ToList();

        var badges = new ObservableCollection<CommitGraphBadgeViewModel>();
        if (refs.Count == 0)
            return badges;

        var totalHeight = refs.Count * badgeHeight + Math.Max(0, refs.Count - 1) * 2;
        var top = centerY - totalHeight - 6;

        foreach (var reference in refs)
        {
            var width = EstimateBadgeWidth(reference.Label);
            var left = Math.Max(0, Math.Min(graphWidth - width, nodeX - (width / 2)));

            badges.Add(new CommitGraphBadgeViewModel
            {
                Label = reference.Label,
                Width = width,
                Left = left,
                Top = top,
                IsRemote = reference.IsRemote
            });

            top += badgeHeight + 2;
        }

        return badges;
    }

    private static double EstimateBadgeWidth(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return 0;

        return Math.Max(56, label.Length * 7 + 22);
    }

    private async Task ResetSoftAsync(CommitItemViewModel? commit)
    {
        if (commit == null || string.IsNullOrEmpty(ProjectPath)) return;

        // Confirmar con el usuario
        var confirm = await DialogService.ShowConfirmDialog(
            "Deshacer  šltimo Commit",
            $"¿Estas seguro de deshacer el commit '{commit.ShortHash}'?\n\nLos cambios se mantendran en el area de trabajo.",
            DialogVariant.Warning,
            DialogType.Confirm);

        if (!confirm) return;

        // Ejecutar reset soft
        var result = await _gitRepository.ResetAsync(ProjectPath, commit.Hash + "^", Chapi.Domain.Enums.ResetMode.Soft);

        if (result.IsSuccess)
        {
            Msg.Assistant($"✅ Commit '{commit.ShortHash}' deshecho. Los cambios están en el área de trabajo.");

            await ReloadHistoryAsync();

            // Notificar que se completo el reset para que los cambios se actualicen
            ResetCompleted?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            await DialogService.ShowConfirmDialog(
                "Error",
                $"No se pudo deshacer el commit:\n{result.Error}",
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

        var result = await _createTagUseCase.ExecuteAsync(ProjectPath, tagName, message, true, commitHash);
        if (result.IsSuccess)
        {
            await ReloadHistoryAsync();
        }
    }

    #endregion
}








