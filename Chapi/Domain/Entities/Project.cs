using System.IO;

namespace Chapi.Domain.Entities;

/// <summary>
/// Representa un proyecto en el sistema.
/// </summary>
public class Project
{
    public string FullPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CurrentBranch { get; set; } = string.Empty;
    public int AheadCount { get; set; }
    public int BehindCount { get; set; }

    public bool IsValid() => Directory.Exists(FullPath);
    public bool HasRemoteChanges() => AheadCount > 0 || BehindCount > 0;
}
