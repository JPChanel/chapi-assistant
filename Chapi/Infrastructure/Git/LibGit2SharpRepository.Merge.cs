using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Domain.Enums;
using Chapi.Domain.Interfaces;
using Chapi.Domain.Models;
using LibGit2Sharp;

namespace Chapi.Infrastructure.Git;

/// <summary>
/// Implementación stub de métodos merge/rebase para LibGit2Sharp.
/// Estos métodos delegan al GitRepository (Git CLI) para evitar complejidad.
/// </summary>
public partial class LibGit2SharpRepository
{
    public async Task<Result> MergeBranchAsync(string projectPath, string sourceBranch, bool fastForward = true)
    {
        // Delegar al GitRepository (Git CLI) que ya tiene la implementación
        var executor = new GitCommandExecutor();
        var parser = new GitOutputParser();
        var gitRepo = new GitRepository(executor, parser);
        return await gitRepo.MergeBranchAsync(projectPath, sourceBranch, fastForward);
    }

    public async Task<Result> SquashMergeBranchAsync(string projectPath, string sourceBranch)
    {
        // Delegar al GitRepository (Git CLI)
        var executor = new GitCommandExecutor();
        var parser = new GitOutputParser();
        var gitRepo = new GitRepository(executor, parser);
        return await gitRepo.SquashMergeBranchAsync(projectPath, sourceBranch);
    }

    public async Task<Result> RebaseBranchAsync(string projectPath, string targetBranch)
    {
        // Delegar al GitRepository (Git CLI)
        var executor = new GitCommandExecutor();
        var parser = new GitOutputParser();
        var gitRepo = new GitRepository(executor, parser);
        return await gitRepo.RebaseBranchAsync(projectPath, targetBranch);
    }

    public async Task<(bool hasConflicts, string message)> CheckMergeConflictsAsync(string projectPath, string sourceBranch)
    {
        // Delegar al GitRepository (Git CLI)
        var executor = new GitCommandExecutor();
        var parser = new GitOutputParser();
        var gitRepo = new GitRepository(executor, parser);
        return await gitRepo.CheckMergeConflictsAsync(projectPath, sourceBranch);
    }
}
