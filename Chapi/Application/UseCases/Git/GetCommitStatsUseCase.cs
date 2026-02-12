using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para obtener las estadísticas (inserciones/eliminaciones) de un commit.
/// </summary>
public class GetCommitStatsUseCase
{
    private readonly IGitRepository _gitRepo;

    public GetCommitStatsUseCase(IGitRepository gitRepo)
    {
        _gitRepo = gitRepo;
    }

    public async Task<(int Additions, int Deletions)> ExecuteAsync(string projectPath, string hash)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(hash))
            return (0, 0);

        var stats = await _gitRepo.GetCommitNumStatAsync(projectPath, hash);

        int totalAdditions = stats.Values.Sum(s => s.Additions);
        int totalDeletions = stats.Values.Sum(s => s.Deletions);

        return (totalAdditions, totalDeletions);
    }
}
