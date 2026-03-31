using Chapi.Presentation.Features.Git.Models;
using Chapi.Presentation.Features.Git.Workflows;

namespace Chapi.Presentation.Features.Git.Services;

public sealed class GitWorkflowCoordinator
{
    private readonly BranchSwitchWorkflow _branchSwitchWorkflow;
    private readonly BranchManagementWorkflow _branchManagementWorkflow;
    private readonly MergeWorkflow _mergeWorkflow;
    private readonly GitSyncWorkflow _gitSyncWorkflow;
    private readonly ConflictResolutionWorkflow _conflictResolutionWorkflow;

    public GitWorkflowCoordinator(
        BranchSwitchWorkflow branchSwitchWorkflow,
        BranchManagementWorkflow branchManagementWorkflow,
        MergeWorkflow mergeWorkflow,
        GitSyncWorkflow gitSyncWorkflow,
        ConflictResolutionWorkflow conflictResolutionWorkflow)
    {
        _branchSwitchWorkflow = branchSwitchWorkflow;
        _branchManagementWorkflow = branchManagementWorkflow;
        _mergeWorkflow = mergeWorkflow;
        _gitSyncWorkflow = gitSyncWorkflow;
        _conflictResolutionWorkflow = conflictResolutionWorkflow;
    }

    public Task<bool> SwitchBranchAsync(GitWorkflowContext context, string newBranch)
        => _branchSwitchWorkflow.ExecuteAsync(context, newBranch);

    public Task PublishBranchAsync(GitWorkflowContext context)
        => _branchManagementWorkflow.PublishAsync(context);

    public Task CreateBranchAsync(GitWorkflowContext context, string? sourceBranch)
        => _branchManagementWorkflow.CreateAsync(context, sourceBranch);

    public Task DeleteBranchAsync(GitWorkflowContext context, string branchName)
        => _branchManagementWorkflow.DeleteAsync(context, branchName);

    public Task ShowMergeDialogAsync(GitWorkflowContext context, string mergeType)
        => _mergeWorkflow.ShowDialogAsync(context, mergeType);

    public Task ExecuteMergeOperationAsync(
        GitWorkflowContext context,
        string mergeType,
        string targetBranch,
        bool autoDeleteBranch = false)
        => _mergeWorkflow.ExecuteAsync(context, mergeType, targetBranch, autoDeleteBranch);

    public Task ExecuteGitActionAsync(GitWorkflowContext context, GitActionState action)
        => _gitSyncWorkflow.ExecuteAsync(context, action);

    public Task HandleMergeConflictsAsync(GitWorkflowContext context)
        => _conflictResolutionWorkflow.HandleAsync(context);
}
