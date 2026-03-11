using Chapi.Domain.Entities;
using Chapi.Domain.Interfaces;
using System.IO;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para cargar el historial de commits.
/// </summary>
public class LoadHistoryUseCase
{
    private readonly IGitRepository _gitRepo;

    public LoadHistoryUseCase(IGitRepository gitRepo)
    {
        _gitRepo = gitRepo;
    }

    public async Task<IEnumerable<GitCommit>> ExecuteAsync(string projectPath, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
            return Enumerable.Empty<GitCommit>();

        var commits = (await _gitRepo.GetCommitsAsync(projectPath, limit)).ToList();
        var tagMap = await _gitRepo.GetTagCommitMapAsync(projectPath);
        var branchRefMap = await _gitRepo.GetBranchRefCommitMapAsync(projectPath);

        foreach (var commit in commits)
        {
            if (tagMap.TryGetValue(commit.Hash, out var tags))
            {
                commit.Tags = tags;
            }

            if (branchRefMap.TryGetValue(commit.Hash, out var refs))
            {
                commit.LocalBranches = refs
                    .Where(r => r.StartsWith("head:", StringComparison.OrdinalIgnoreCase))
                    .Select(r => r["head:".Length..])
                    .ToList();

                commit.RemoteBranches = refs
                    .Where(r => r.StartsWith("remote:", StringComparison.OrdinalIgnoreCase))
                    .Select(r => r["remote:".Length..])
                    .ToList();
            }
        }

        // Obtener commits no pusheados
        var currentBranch = await _gitRepo.GetCurrentBranchAsync(projectPath);
        if (!string.IsNullOrEmpty(currentBranch))
        {
            var unpushedHashes = await _gitRepo.GetUnpushedCommitsAsync(projectPath, currentBranch);

            foreach (var commit in commits)
            {
                commit.IsUnpushed = unpushedHashes.Contains(commit.Hash);
            }
        }

        return commits;
    }
}
