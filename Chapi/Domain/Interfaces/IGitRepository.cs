using Chapi.Domain.Common;
using Chapi.Domain.Entities;

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

    // Branches
    Task<IEnumerable<string>> GetBranchesAsync(string projectPath);
    Task<string> GetCurrentBranchAsync(string projectPath);
    Task<Result> SwitchBranchAsync(string projectPath, string branchName);

    // Remote
    Task<Result> PushAsync(string projectPath, string branch);
    Task<Result> PullAsync(string projectPath, string branch);
    Task<Result> FetchAsync(string projectPath);
    Task<(int Ahead, int Behind)> GetAheadBehindCountAsync(string projectPath);
    
    Task<IEnumerable<string>> GetFilesChangedInCommitAsync(string projectPath, string hash);
    Task<string> GetFileContentAtCommitAsync(string projectPath, string file, string hash);
    Task<string> GetCommitParentHashAsync(string projectPath, string hash);

    // Lifecycle
    Task<Result> CloneAsync(string url, string destinationPath);
    Task<Result> InitAsync(string projectPath);
    Task<Result> AddRemoteAsync(string projectPath, string name, string url);

    // Generic command execution
    Task<string> ExecuteGitCommandAsync(string projectPath, string command);
}
