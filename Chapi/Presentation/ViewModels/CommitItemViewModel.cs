using System.Collections.ObjectModel;
using System.Linq;

namespace Chapi.Presentation.ViewModels;

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
    private double _graphWidth = 42;
    private double _nodeLeft = 16;
    private double _nodeTop = 34;
    private string _nodeFill = "#7EC8FF";
    private string _nodeStroke = "#0F172A";
    private bool _isMergeNode;

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
            }
        }
    }

    public bool HasTags => Tags.Count > 0;

    public ObservableCollection<CommitGraphLineViewModel> GraphLines
    {
        get => _graphLines;
        set => SetProperty(ref _graphLines, value);
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
}

public sealed class CommitGraphLineViewModel
{
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
    public string Stroke { get; init; } = "#7EC8FF";
}
