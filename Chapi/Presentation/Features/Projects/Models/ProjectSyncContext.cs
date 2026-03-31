using Chapi.Domain.Models;

namespace Chapi.Presentation.Features.Projects.Models;

public sealed class ProjectSyncContext
{
    public string ProjectPath { get; init; } = string.Empty;
    public Func<IReadOnlyList<ProjectViewModel>> GetLoadedProjects { get; init; } = null!;
    public Func<string?> GetChangesProjectPath { get; init; } = null!;
    public Func<bool> IsProjectDropdownOpen { get; init; } = null!;
    public Func<bool> IsChangesTabActive { get; init; } = null!;
    public Func<bool> IsWslProject { get; init; } = null!;
    public Func<Task> RefreshBranchesAsync { get; init; } = null!;
    public Func<Task> CheckBranchStatusAsync { get; init; } = null!;
    public Func<Task> ForceRefreshChangesAsync { get; init; } = null!;
    public Func<Task> RefreshChangesIfNecessaryAsync { get; init; } = null!;
}
