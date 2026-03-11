namespace Chapi.Domain.Entities;

/// <summary>
/// Representa un commit de Git.
/// </summary>
public class GitCommit
{
    public string Hash { get; set; } = string.Empty;
    public string GraphPrefix { get; set; } = string.Empty;
    public List<string> ParentHashes { get; set; } = new();
    public string Author { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string RelativeDate { get; set; } = string.Empty;
    public bool IsUnpushed { get; set; }
    public List<string> Tags { get; set; } = new();

    public bool HasTags => Tags != null && Tags.Any();
    public string ShortHash => Hash.Length >= 7 ? Hash.Substring(0, 7) : Hash;
    public bool IsValid() => !string.IsNullOrWhiteSpace(Hash) && !string.IsNullOrWhiteSpace(Message);
}
