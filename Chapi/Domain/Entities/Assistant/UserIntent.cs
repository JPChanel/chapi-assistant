namespace Chapi.Domain.Entities.Assistant;

/// <summary>
/// Representa la intención detectada del usuario
/// </summary>
public class UserIntent
{
    public IntentType Type { get; set; }
    public string OriginalMessage { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new();
    public double Confidence { get; set; }
}

/// <summary>
/// Tipos de intenciones que el asistente puede detectar
/// </summary>
public enum IntentType
{
    Unknown,
    
    // Git Operations
    Commit,
    Push,
    Pull,
    CreateBranch,
    SwitchBranch,
    MergeBranch,
    ViewChanges,
    ViewHistory,
    
    // Code Generation
    GenerateCode,
    RefactorCode,
    AnalyzeCode,
    
    // Project Info
    ExplainProject,
    ExplainArchitecture,
    ListFiles,
    
    // General Questions
    AskQuestion,
    Help
}
