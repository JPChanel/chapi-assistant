using Chapi.Presentation.ViewModels;

namespace Chapi.Presentation.Shell.Models;

public sealed class ProjectSelectionRequest
{
    public required string ProjectPath { get; init; }
    public required string ProjectName { get; init; }
    public ChangesViewModel? ChangesViewModel { get; init; }
    public HistoryViewModel? HistoryViewModel { get; init; }
    public ReleasesViewModel? ReleasesViewModel { get; init; }
    public WorkspaceViewModel? WorkspaceViewModel { get; init; }
    public AssistantViewModel? AssistantViewModel { get; init; }
    public DocumentationViewModel? DocumentationViewModel { get; init; }
}

public sealed class ProjectSelectionSnapshot
{
    public string CurrentBranch { get; init; } = string.Empty;
    public IReadOnlyList<string> Branches { get; init; } = Array.Empty<string>();
    public int Ahead { get; init; }
    public bool NeedsPublish { get; init; }
}

public sealed class ProjectBranchSnapshot
{
    public string CurrentBranch { get; init; } = string.Empty;
    public IReadOnlyList<string> Branches { get; init; } = Array.Empty<string>();
}
