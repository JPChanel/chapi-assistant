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

        var commits = await _gitRepo.GetCommitsAsync(projectPath, limit);

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
