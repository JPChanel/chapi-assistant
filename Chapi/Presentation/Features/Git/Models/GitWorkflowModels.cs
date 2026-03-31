namespace Chapi.Presentation.Features.Git.Models;

public enum GitActionState
{
    Pull,
    Push,
    Fetch
}

public sealed class GitWorkflowContext
{
    public required string ProjectPath { get; init; }
    public required Func<string> GetCurrentBranch { get; init; }
    public required Action<string> SetCurrentBranch { get; init; }
    public required Action<string?> SelectBranch { get; init; }
    public required Func<Task<bool>> HasPendingChangesAsync { get; init; }
    public required Func<Func<Task>, Task> RunWithLoadingAsync { get; init; }
    public required Func<Task> LoadChangesAsync { get; init; }
    public required Func<Task> LoadHistoryAsync { get; init; }
    public required Func<Task> RefreshBranchesAsync { get; init; }
    public required Func<Task> CheckBranchStatusAsync { get; init; }
    public required Func<Task> UpdateProjectStatusesAsync { get; init; }
    public required Func<Task> ForceRefreshChangesAsync { get; init; }
    public required Func<Task> SyncProjectAsync { get; init; }
    public Func<IDisposable?>? SuspendWatcher { get; init; }
}
