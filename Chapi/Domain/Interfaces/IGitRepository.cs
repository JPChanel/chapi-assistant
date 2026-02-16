using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Domain.Enums;
using Chapi.Domain.Models;

namespace Chapi.Domain.Interfaces;

/// <summary>
/// Repositorio para operaciones Git.
/// Define el contrato para todas las operaciones de Git.
/// </summary>
public interface IGitRepository
{
    // Commits
    Task<Result<GitCommit>> CommitAsync(string projectPath, string message, IEnumerable<string> files);
    Task<IEnumerable<GitCommit>> GetCommitsAsync(string projectPath, int limit);
    Task<HashSet<string>> GetUnpushedCommitsAsync(string projectPath, string branch);

    // Changes
    Task<IEnumerable<FileChange>> GetChangesAsync(string projectPath);
    Task<Result> StageFilesAsync(string projectPath, IEnumerable<string> files);
    Task<Result> UnstageFilesAsync(string projectPath, IEnumerable<string> files);
    Task<Result> DiscardChangesAsync(string projectPath, IEnumerable<string>? files = null);

    // Branches
    Task<IEnumerable<string>> GetBranchesAsync(string projectPath);
    Task<string> GetCurrentBranchAsync(string projectPath);
    Task<Result> SwitchBranchAsync(string projectPath, string branchName);
    Task<Result> CreateBranchAsync(string projectPath, string branchName, string? fromCommitOrBranch = null);
    Task<Result> DeleteBranchAsync(string projectPath, string branchName, bool force = false, bool deleteRemote = false);
    Task<Result> MergeBranchAsync(string projectPath, string sourceBranch, bool fastForward = true);
    Task<Result> SquashMergeBranchAsync(string projectPath, string sourceBranch, string? commitMessage = null);
    Task<Result> RebaseBranchAsync(string projectPath, string targetBranch);
    Task<(bool hasConflicts, string message)> CheckMergeConflictsAsync(string projectPath, string sourceBranch);
    Task<Result> ResetAsync(string projectPath, string target, ResetMode mode);
    Task<Result> RestoreFileFromStashAsync(string projectPath, string stashName, string filePath);

    // Remote
    Task<Result> PushAsync(string projectPath, string branch, bool force = false);
    Task<Result> PullAsync(string projectPath, string branch);
    Task<Result> FetchAsync(string projectPath);
    Task<(int Ahead, int Behind)> GetAheadBehindCountAsync(string projectPath);
    Task<string> GetRemoteUrlAsync(string projectPath, string remoteName = "origin");
    Task<Result> SetRemoteUrlAsync(string projectPath, string remoteName, string url);

    Task<IEnumerable<string>> GetFilesChangedInCommitAsync(string projectPath, string hash);
    Task<string> GetFileContentAtCommitAsync(string projectPath, string file, string hash);
    Task<string> GetCommitParentHashAsync(string projectPath, string hash);
    Task<Dictionary<string, (int Additions, int Deletions)>> GetCommitNumStatAsync(string projectPath, string hash);

    // Lifecycle
    Task<Result> CloneAsync(string url, string destinationPath);
    Task<Result> InitAsync(string projectPath);
    Task<Result> AddRemoteAsync(string projectPath, string name, string url);
    // Stash
    Task<Result> StashChangesAsync(string projectPath, string message, IEnumerable<string>? files = null);
    Task<IEnumerable<GitStash>> ListStashesAsync(string projectPath);
    Task<Dictionary<string, char>> GetFileStatusesForStashAsync(string projectPath, string stashName);
    Task<Result> StashPopAsync(string projectPath, int? index = null);
    Task<Result> StashDropAsync(string projectPath, int index);
    Task<Result> StashClearAsync(string projectPath);

    // Tags
    Task<Result> CreateTagAsync(string projectPath, string tagName, string message, string commitHash = null);
    Task<Result> DeleteTagLocalAsync(string projectPath, string tagName);
    Task<Result> DeleteTagRemoteAsync(string projectPath, string tagName);
    Task<Result> PushTagAsync(string projectPath, string tagName);
    Task<IEnumerable<GitTagItem>> GetTagsAsync(string projectPath);
    Task<Dictionary<string, List<string>>> GetTagCommitMapAsync(string projectPath);

    // Misc
    bool IsGitInstalled();
    Task<bool> HasUpstreamAsync(string projectPath, string branchName);
    Task<string> GetFileContentAsync(string projectPath, string revision, string filePath);
    Task<string> GetDiffAsync(string projectPath, string file, string? revision = null);
    Task<string> GetBranchDiffAsync(string projectPath, string sourceBranch, string targetBranch);
    Task<(int additions, int deletions)> GetFileStatsAsync(string projectPath, string filePath);

    // Config
    Task<string> GetConfigAsync(string projectPath, string key, bool isGlobal = false);
    Task<Result> SetConfigAsync(string projectPath, string key, string value, bool isGlobal = false);
    Task<Result> UnsetConfigAsync(string projectPath, string key, bool isGlobal = false);
}
