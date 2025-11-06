

using MaterialDesignThemes.Wpf;
using System.Windows.Media;

namespace Chapi.Model;

public class GitModel
{
}
public class GitStatusItem
{
    public string Status { get; set; }
    public string FilePath { get; set; }
    public PackIconKind Icon { get; set; }
    public Brush Color { get; set; }
    public bool IsSelected { get; set; } = true;
}

public class GitLogItem
{
    public string Hash { get; set; }
    public string Author { get; set; }
    public string Date { get; set; }
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
    public string CommitMessage { get; set; }
    public string RelativeDate { get; set; }
    public string TagMessage { get; set; }
}
/// <summary>
/// Representa un proyecto en la UI, incluyendo su ícono de host.
/// </summary>
public class ProjectViewModel
{
    public string FullPath { get; set; }
    public string Name { get; set; }
    public PackIconKind Icon { get; set; }
}