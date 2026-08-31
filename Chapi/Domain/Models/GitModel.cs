using MaterialDesignThemes.Wpf;
using System.Windows.Media;

namespace Chapi.Domain.Models;

public class GitModel
{
}

public class GitStatusItem
{
    public string Status { get; set; }
    public string ShortStatus { get; set; }
    public string FilePath { get; set; }
    public string FileName => System.IO.Path.GetFileName(FilePath);
    public string DirectoryPath => System.IO.Path.GetDirectoryName(FilePath);
    public PackIconKind Icon { get; set; }
    public Brush Color { get; set; }
    public bool IsSelected { get; set; } = true;
    public int Additions { get; set; } = 0;
    public int Deletions { get; set; } = 0;
}

public class GitLogItem
{
    public string Hash { get; set; }
    public string ShortHash => Hash?.Length > 7 ? Hash.Substring(0, 7) : Hash;
    public string Author { get; set; }
    public string Date { get; set; }
    public string RelativeDate { get; set; }
    public string Message { get; set; }

    public string Description { get; set; }
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool IsUnpushed { get; set; } = false;
    public List<string> Tags { get; set; } = new List<string>();
    public bool HasTags => Tags.Any();
}

public class GitTagItem
{
    public string TagName { get; set; }
    public string CommitHash { get; set; }
    public string ShortHash => CommitHash?.Length > 7 ? CommitHash.Substring(0, 7) : CommitHash;
    public string CommitMessage { get; set; }
    public string RelativeDate { get; set; }
    public string TagMessage { get; set; }
    public string AuthorName { get; set; }
    public string CommitDescription { get; set; }
    public bool IsLatest { get; set; }

    // Estadísticas
    public int FilesChanged { get; set; }
    public int Additions { get; set; }
    public int Deletions { get; set; }
    public List<string> ModifiedFiles { get; set; } = new List<string>();
}

/// <summary>
/// Representa un proyecto en la UI, incluyendo su icono de host y estado de Git.
/// </summary>
public class ProjectViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    public string FullPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PackIconKind Icon { get; set; }

    private string? _groupId;
    public string? GroupId
    {
        get => _groupId;
        set { _groupId = value; OnPropertyChanged(nameof(GroupId)); }
    }

    private string _groupName = "Sin Agrupar";
    public string GroupName
    {
        get => _groupName;
        set
        {
            _groupName = value;
            OnPropertyChanged(nameof(GroupName));
            OnPropertyChanged(nameof(GroupHeader));
        }
    }

    public string GroupHeader => string.IsNullOrWhiteSpace(GroupName) ? "Sin Agrupar" : GroupName;

    private int _groupOrder = int.MaxValue;
    public int GroupOrder
    {
        get => _groupOrder;
        set { _groupOrder = value; OnPropertyChanged(nameof(GroupOrder)); }
    }

    private int _projectOrder;
    public int ProjectOrder
    {
        get => _projectOrder;
        set { _projectOrder = value; OnPropertyChanged(nameof(ProjectOrder)); }
    }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set { _isVisible = value; OnPropertyChanged(nameof(IsVisible)); }
    }

    private int _ahead;
    public int Ahead
    {
        get => _ahead;
        set { _ahead = value; OnPropertyChanged(nameof(Ahead)); OnPropertyChanged(nameof(HasAhead)); }
    }

    private int _behind;
    public int Behind
    {
        get => _behind;
        set { _behind = value; OnPropertyChanged(nameof(Behind)); OnPropertyChanged(nameof(HasBehind)); }
    }

    private bool _hasRemote = true;
    public bool HasRemote
    {
        get => _hasRemote;
        set
        {
            _hasRemote = value;
            OnPropertyChanged(nameof(HasRemote));
            OnPropertyChanged(nameof(HasNoRemote));
        }
    }

    public bool HasAhead => Ahead > 0;
    public bool HasBehind => Behind > 0;
    public bool HasNoRemote => !HasRemote;
    public bool IsPlaceholder { get; set; } = false;
}
