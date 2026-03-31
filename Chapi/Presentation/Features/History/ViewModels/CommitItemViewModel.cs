using System.Collections.ObjectModel;
using System.Linq;
using Chapi.Presentation.Shared.Mvvm;

namespace Chapi.Presentation.Features.History.ViewModels;

/// <summary>
/// ViewModel para un item de commit individual en la lista de historial.
/// </summary>
public class CommitItemViewModel : ViewModelBase
{
    private string _hash = string.Empty;
    private string _graphPrefix = string.Empty;
    private string _shortHash = string.Empty;
    private string _message = string.Empty;
    private string _author = string.Empty;
    private DateTime _date;
    private string _relativeDate = string.Empty;
    private bool _isSynced;
    private string _description = string.Empty;
    private ObservableCollection<string> _tags = new();
    private ObservableCollection<CommitGraphLineViewModel> _graphLines = new();
    private ObservableCollection<CommitGraphBadgeViewModel> _branchBadges = new();
    private double _graphWidth = 42;
    private double _nodeLeft = 16;
    private double _nodeTop = 34;
    private string _nodeFill = "#7EC8FF";
    private string _nodeStroke = "#0F172A";
    private bool _isMergeNode;
    private ObservableCollection<string> _localBranches = new();
    private ObservableCollection<string> _remoteBranches = new();

    public string Hash
    {
        get => _hash;
        set => SetProperty(ref _hash, value);
    }

    public string GraphPrefix
    {
        get => _graphPrefix;
        set => SetProperty(ref _graphPrefix, value);
    }

    public string ShortHash
    {
        get => _shortHash;
        set => SetProperty(ref _shortHash, value);
    }

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string Author
    {
        get => _author;
        set => SetProperty(ref _author, value);
    }

    public DateTime Date
    {
        get => _date;
        set => SetProperty(ref _date, value);
    }

    public string RelativeDate
    {
        get => _relativeDate;
        set => SetProperty(ref _relativeDate, value);
    }

    public bool IsSynced
    {
        get => _isSynced;
        set
        {
            if (SetProperty(ref _isSynced, value))
            {
                OnPropertyChanged(nameof(IsUnpushed));
            }
        }
    }

    public bool IsUnpushed => !IsSynced;
    public string Summary => _message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    public ObservableCollection<string> Tags
    {
        get => _tags;
        set
        {
            if (SetProperty(ref _tags, value))
            {
                OnPropertyChanged(nameof(HasTags));
                OnPropertyChanged(nameof(GraphTooltip));
            }
        }
    }

    public bool HasTags => Tags.Count > 0;

    public ObservableCollection<string> LocalBranches
    {
        get => _localBranches;
        set
        {
            if (SetProperty(ref _localBranches, value))
            {
                OnPropertyChanged(nameof(GraphTooltip));
            }
        }
    }

    public ObservableCollection<string> RemoteBranches
    {
        get => _remoteBranches;
        set
        {
            if (SetProperty(ref _remoteBranches, value))
            {
                OnPropertyChanged(nameof(GraphTooltip));
            }
        }
    }

    public ObservableCollection<CommitGraphLineViewModel> GraphLines
    {
        get => _graphLines;
        set => SetProperty(ref _graphLines, value);
    }

    public ObservableCollection<CommitGraphBadgeViewModel> BranchBadges
    {
        get => _branchBadges;
        set => SetProperty(ref _branchBadges, value);
    }

    public double GraphWidth
    {
        get => _graphWidth;
        set => SetProperty(ref _graphWidth, value);
    }

    public double NodeLeft
    {
        get => _nodeLeft;
        set => SetProperty(ref _nodeLeft, value);
    }

    public double NodeTop
    {
        get => _nodeTop;
        set => SetProperty(ref _nodeTop, value);
    }

    public string NodeFill
    {
        get => _nodeFill;
        set => SetProperty(ref _nodeFill, value);
    }

    public string NodeStroke
    {
        get => _nodeStroke;
        set => SetProperty(ref _nodeStroke, value);
    }

    public bool IsMergeNode
    {
        get => _isMergeNode;
        set => SetProperty(ref _isMergeNode, value);
    }

    public string GraphTooltip
    {
        get
        {
            var lines = new List<string>
            {
                Message,
                $"Autor: {Author}",
                $"Fecha: {Date:dd/MM/yyyy HH:mm}"
            };

            if (LocalBranches.Count > 0)
                lines.Add($"Ramas locales: {string.Join(", ", LocalBranches)}");

            if (RemoteBranches.Count > 0)
                lines.Add($"Ramas remotas: {string.Join(", ", RemoteBranches)}");

            if (Tags.Count > 0)
                lines.Add($"Tags: {string.Join(", ", Tags)}");

            return string.Join(Environment.NewLine, lines);
        }
    }
}

public sealed class CommitGraphLineViewModel
{
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
    public string Stroke { get; init; } = "#7EC8FF";
}

public sealed class CommitGraphBadgeViewModel
{
    public string Label { get; init; } = string.Empty;
    public string DisplayLabel { get; init; } = string.Empty;
    public string VerticalLabel => string.Join("\n", DisplayLabel.ToCharArray());
    public double Left { get; init; }
    public double Top { get; init; }
    public bool HasLocal { get; init; }
    public bool HasRemote { get; init; }
    public bool HasBoth => HasLocal && HasRemote;
    public string Indicator => HasBoth ? "L+R" : HasLocal ? "L" : "R";
    public double Width { get; init; }
    public double Height { get; init; }
    public string Tooltip { get; init; } = string.Empty;
}
