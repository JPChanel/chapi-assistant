namespace Chapi.Domain.Entities.Assistant;

/// <summary>
/// Contexto completo de la conversación incluyendo información del proyecto actual
/// </summary>
public class ConversationContext
{
    public ProjectContext? CurrentProject { get; set; }
    public List<ChatMessage> ConversationHistory { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// Información contextual del proyecto actual
/// </summary>
public class ProjectContext
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Technology { get; set; } = string.Empty;
    public List<string> MainFolders { get; set; } = new();
    public GitContext? Git { get; set; }
    public List<string> RecentFiles { get; set; } = new();
    public ChapiCapabilities Capabilities { get; set; } = new();
}

/// <summary>
/// Capacidades disponibles en Chapi para ejecutar acciones
/// </summary>
public class ChapiCapabilities
{
    public bool CanCommit { get; set; }
    public bool CanPush { get; set; }
    public bool CanPull { get; set; }
    public bool CanCreateBranch { get; set; }
    public bool CanMergeBranch { get; set; }
    public bool CanGenerateCode { get; set; }
    public bool CanAnalyzeArchitecture { get; set; }
    public List<string> AvailableServices { get; set; } = new();
}

/// <summary>
/// Contexto Git del proyecto
/// </summary>
public class GitContext
{
    public string CurrentBranch { get; set; } = string.Empty;
    public List<CommitInfo> RecentCommits { get; set; } = new();
    public List<string> ModifiedFiles { get; set; } = new();
    public List<string> UntrackedFiles { get; set; } = new();
    public int AheadBy { get; set; }
    public int BehindBy { get; set; }
    public bool HasUncommittedChanges { get; set; }
}

/// <summary>
/// Información resumida de un commit
/// </summary>
public class CommitInfo
{
    public string Sha { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTime Date { get; set; }
}
