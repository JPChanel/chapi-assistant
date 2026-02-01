

using MaterialDesignThemes.Wpf;
using System.Windows.Media;

namespace Chapi.Model;

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
/// Representa un proyecto en la UI, incluyendo su ícono de host y estado de Git.
/// </summary>
public class ProjectViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    public string FullPath { get; set; }
    public string Name { get; set; }
    public PackIconKind Icon { get; set; }

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

    public bool HasAhead => Ahead > 0;
    public bool HasBehind => Behind > 0;
}