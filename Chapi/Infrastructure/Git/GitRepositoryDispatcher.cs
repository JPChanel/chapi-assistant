using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Domain.Enums;
using Chapi.Domain.Interfaces;
using Chapi.Domain.Models;

namespace Chapi.Infrastructure.Git;

/// <summary>
/// Despachador inteligente que selecciona la mejor implementación de Git (Local vs WSL).
/// Aplica el patrón Strategy para optimizar el rendimiento según el sistema de archivos.
/// </summary>
public class GitRepositoryDispatcher : IGitRepository
{
    private readonly LibGit2SharpRepository _libGit2;
    private readonly WslGitRepository _wsl;

    public GitRepositoryDispatcher(LibGit2SharpRepository libGit2, WslGitRepository wsl)
    {
        _libGit2 = libGit2;
        _wsl = wsl;
    }

    private bool IsWslPath(string path) => 
        !string.IsNullOrEmpty(path) && (path.Contains(@"\\wsl$\") || path.Contains(@"\\wsl.localhost\"));

    #region Operaciones Optimizadas (WSL Fast Path)

    public async Task<Result> StashChangesAsync(string projectPath, string message, IEnumerable<string>? files = null)
    {
        if (IsWslPath(projectPath)) return await _wsl.StashChangesAsync(projectPath, message, files);
        return await _libGit2.StashChangesAsync(projectPath, message, files);
    }

    public async Task<Result> StashPopAsync(string projectPath, int? index = null)
    {
        if (IsWslPath(projectPath)) return await _wsl.StashPopAsync(projectPath, index);
        return await _libGit2.StashPopAsync(projectPath, index);
    }

    public async Task<Result> StashDropAsync(string projectPath, int index)
    {
        if (IsWslPath(projectPath)) return await _wsl.StashDropAsync(projectPath, index);
        return await _libGit2.StashDropAsync(projectPath, index);
    }

    public async Task<Result> StashClearAsync(string projectPath)
    {
        if (IsWslPath(projectPath)) return await _wsl.StashClearAsync(projectPath);
        return await _libGit2.StashClearAsync(projectPath);
    }

    public async Task<IEnumerable<FileChange>> GetChangesAsync(string projectPath)
    {
        if (IsWslPath(projectPath)) return await _wsl.GetChangesAsync(projectPath);
        return await _libGit2.GetChangesAsync(projectPath);
    }

    public async Task<IEnumerable<GitStash>> ListStashesAsync(string projectPath)
    {
        if (IsWslPath(projectPath)) return await _wsl.ListStashesAsync(projectPath);
        return await _libGit2.ListStashesAsync(projectPath);
    }

    public async Task<Dictionary<string, char>> GetFileStatusesForStashAsync(string projectPath, string stashName)
    {
        if (IsWslPath(projectPath)) return await _wsl.GetFileStatusesForStashAsync(projectPath, stashName);
        return await _libGit2.GetFileStatusesForStashAsync(projectPath, stashName);
    }

    public async Task<string> GetCurrentBranchAsync(string projectPath)
    {
        if (IsWslPath(projectPath)) return await _wsl.GetCurrentBranchAsync(projectPath);
        return await _libGit2.GetCurrentBranchAsync(projectPath);
    }

    public async Task<string> GetFileContentAsync(string projectPath, string revision, string filePath)
    {
        if (IsWslPath(projectPath)) return await _wsl.GetFileContentAsync(projectPath, revision, filePath);
        return await _libGit2.GetFileContentAsync(projectPath, revision, filePath);
    }

    public async Task<(int additions, int deletions)> GetFileStatsAsync(string projectPath, string filePath)
    {
        if (IsWslPath(projectPath)) return await _wsl.GetFileStatsAsync(projectPath, filePath);
        return await _libGit2.GetFileStatsAsync(projectPath, filePath);
    }

    public async Task<string> GetRemoteUrlAsync(string projectPath, string remoteName = "origin")
    {
        if (IsWslPath(projectPath)) return await _wsl.GetRemoteUrlAsync(projectPath, remoteName);
        return await _libGit2.GetRemoteUrlAsync(projectPath, remoteName);
    }

    public async Task<(int Ahead, int Behind)> GetAheadBehindCountAsync(string projectPath)
    {
        if (IsWslPath(projectPath)) return await _wsl.GetAheadBehindCountAsync(projectPath);
        return await _libGit2.GetAheadBehindCountAsync(projectPath);
    }

    public async Task<bool> HasUpstreamAsync(string projectPath, string branchName)
    {
        if (IsWslPath(projectPath)) return await _wsl.HasUpstreamAsync(projectPath, branchName);
        return await _libGit2.HasUpstreamAsync(projectPath, branchName);
    }

    public async Task<IEnumerable<GitCommit>> GetCommitsAsync(string projectPath, int limit)
    {
        if (IsWslPath(projectPath)) return await _wsl.GetCommitsAsync(projectPath, limit);
        return await _libGit2.GetCommitsAsync(projectPath, limit);
    }

    public async Task<HashSet<string>> GetUnpushedCommitsAsync(string projectPath, string branch)
    {
        if (IsWslPath(projectPath)) return await _wsl.GetUnpushedCommitsAsync(projectPath, branch);
        return await _libGit2.GetUnpushedCommitsAsync(projectPath, branch);
    }

    public async Task<Result> PushAsync(string projectPath, string branch, bool force = false)
    {
        // Fuerza el uso de Windows Nativo para operaciones de red (evita cuelgues de credenciales en WSL)
        return await _libGit2.PushAsync(projectPath, branch, force);
    }

    public async Task<Result> PullAsync(string projectPath, string branch)
    {
        // Fuerza el uso de Windows Nativo para operaciones de red
        return await _libGit2.PullAsync(projectPath, branch);
    }

    public async Task<Result> FetchAsync(string projectPath)
    {
        // Fuerza el uso de Windows Nativo para operaciones de red
        return await _libGit2.FetchAsync(projectPath);
    }


    public async Task<IEnumerable<string>> GetFilesChangedInCommitAsync(string projectPath, string hash)
    {
        if (IsWslPath(projectPath)) return await _wsl.GetFilesChangedInCommitAsync(projectPath, hash);
        return await _libGit2.GetFilesChangedInCommitAsync(projectPath, hash);
    }

    public async Task<string> GetFileContentAtCommitAsync(string projectPath, string file, string hash)
    {
        if (IsWslPath(projectPath)) return await _wsl.GetFileContentAtCommitAsync(projectPath, file, hash);
        return await _libGit2.GetFileContentAtCommitAsync(projectPath, file, hash);
    }

    public async Task<string> GetConfigAsync(string projectPath, string key, bool isGlobal = false)
    {
        if (IsWslPath(projectPath)) return await _wsl.GetConfigAsync(projectPath, key, isGlobal);
        return await _libGit2.GetConfigAsync(projectPath, key, isGlobal);
    }

    public async System.Threading.Tasks.Task<Chapi.Domain.Common.Result<Chapi.Domain.Models.GitRepositoryMetadata>> GetMetadataAsync(string projectPath)
    {
        if (IsWslPath(projectPath)) return await _wsl.GetMetadataAsync(projectPath);
        return await _libGit2.GetMetadataAsync(projectPath);
    }

    public bool IsGitInstalled() => true;

    #endregion

    #region Operaciones Delegadas (Fallback a LibGit2Sharp)

    public Task<Result<GitCommit>> CommitAsync(string projectPath, string message, IEnumerable<string> files) => _libGit2.CommitAsync(projectPath, message, files);
    public Task<Result> StageFilesAsync(string projectPath, IEnumerable<string> files) => _libGit2.StageFilesAsync(projectPath, files);
    public Task<Result> UnstageFilesAsync(string projectPath, IEnumerable<string> files) => _libGit2.UnstageFilesAsync(projectPath, files);
    public Task<Result> DiscardChangesAsync(string projectPath, IEnumerable<string>? files = null) => _libGit2.DiscardChangesAsync(projectPath, files);
    public Task<IEnumerable<string>> GetBranchesAsync(string projectPath) => _libGit2.GetBranchesAsync(projectPath);
    public Task<Result> SwitchBranchAsync(string projectPath, string branchName) => _libGit2.SwitchBranchAsync(projectPath, branchName);
    public Task<Result> CreateBranchAsync(string projectPath, string branchName, string? fromCommitOrBranch = null) => _libGit2.CreateBranchAsync(projectPath, branchName, fromCommitOrBranch);
    public Task<Result> DeleteBranchAsync(string projectPath, string branchName, bool force = false, bool deleteRemote = false) => _libGit2.DeleteBranchAsync(projectPath, branchName, force, deleteRemote);
    public Task<Result> MergeBranchAsync(string projectPath, string sourceBranch, bool fastForward = true) => _libGit2.MergeBranchAsync(projectPath, sourceBranch, fastForward);
    public Task<Result> SquashMergeBranchAsync(string projectPath, string sourceBranch, string? commitMessage = null) => _libGit2.SquashMergeBranchAsync(projectPath, sourceBranch, commitMessage);
    public Task<Result> RebaseBranchAsync(string projectPath, string targetBranch) => _libGit2.RebaseBranchAsync(projectPath, targetBranch);
    public Task<(bool hasConflicts, string message)> CheckMergeConflictsAsync(string projectPath, string sourceBranch) => _libGit2.CheckMergeConflictsAsync(projectPath, sourceBranch);
    public Task<Result> ResetAsync(string projectPath, string target, ResetMode mode) => _libGit2.ResetAsync(projectPath, target, mode);
    public Task<Result> RestoreFileFromStashAsync(string projectPath, string stashName, string filePath) => _libGit2.RestoreFileFromStashAsync(projectPath, stashName, filePath);
    public Task<Result> SetRemoteUrlAsync(string projectPath, string remoteName, string url) => _libGit2.SetRemoteUrlAsync(projectPath, remoteName, url);
    public Task<string> GetCommitParentHashAsync(string projectPath, string hash) => _libGit2.GetCommitParentHashAsync(projectPath, hash);
    public Task<Dictionary<string, (int Additions, int Deletions)>> GetCommitNumStatAsync(string projectPath, string hash) => _libGit2.GetCommitNumStatAsync(projectPath, hash);
    public Task<Result> CloneAsync(string url, string destinationPath) => _libGit2.CloneAsync(url, destinationPath);
    public Task<Result> InitAsync(string projectPath) => _libGit2.InitAsync(projectPath);
    public Task<Result> AddRemoteAsync(string projectPath, string name, string url) => _libGit2.AddRemoteAsync(projectPath, name, url);
    public Task<Result> CreateTagAsync(string projectPath, string tagName, string message, string commitHash = null) => _libGit2.CreateTagAsync(projectPath, tagName, message, commitHash);
    public Task<Result> DeleteTagLocalAsync(string projectPath, string tagName) => _libGit2.DeleteTagLocalAsync(projectPath, tagName);
    public Task<Result> DeleteTagRemoteAsync(string projectPath, string tagName) => _libGit2.DeleteTagRemoteAsync(projectPath, tagName);
    public Task<Result> PushTagAsync(string projectPath, string tagName) => _libGit2.PushTagAsync(projectPath, tagName);
    public Task<IEnumerable<GitTagItem>> GetTagsAsync(string projectPath) => _libGit2.GetTagsAsync(projectPath);
    public Task<Dictionary<string, List<string>>> GetTagCommitMapAsync(string projectPath) => _libGit2.GetTagCommitMapAsync(projectPath);
    public Task<string> GetDiffAsync(string projectPath, string file, string? revision = null) => _libGit2.GetDiffAsync(projectPath, file, revision);
    public Task<string> GetBranchDiffAsync(string projectPath, string sourceBranch, string targetBranch) => _libGit2.GetBranchDiffAsync(projectPath, sourceBranch, targetBranch);
    public Task<Result> SetConfigAsync(string projectPath, string key, string value, bool isGlobal = false)
    {
        if (IsWslPath(projectPath)) return _wsl.SetConfigAsync(projectPath, key, value, isGlobal);
        return _libGit2.SetConfigAsync(projectPath, key, value, isGlobal);
    }

    public Task<Result> UnsetConfigAsync(string projectPath, string key, bool isGlobal = false)
    {
        if (IsWslPath(projectPath)) return _wsl.UnsetConfigAsync(projectPath, key, isGlobal);
        return _libGit2.UnsetConfigAsync(projectPath, key, isGlobal);
    }

    #endregion
}
