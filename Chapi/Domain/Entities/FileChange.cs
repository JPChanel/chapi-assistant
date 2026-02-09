using System.IO;

namespace Chapi.Domain.Entities;

/// <summary>
/// Representa un cambio en un archivo del repositorio Git.
/// </summary>
public class FileChange
{
    public string FilePath { get; set; } = string.Empty;
    public ChangeStatus Status { get; set; }
    public int Additions { get; set; }
    public int Deletions { get; set; }

    public string FileName => Path.GetFileName(FilePath);
    public bool IsValid() => !string.IsNullOrWhiteSpace(FilePath);
}

/// <summary>
/// Estados posibles de un archivo en Git.
/// </summary>
public enum ChangeStatus
{
    Modified,
    Added,
    Deleted,
    Renamed,
    Untracked,
    Conflict
}
