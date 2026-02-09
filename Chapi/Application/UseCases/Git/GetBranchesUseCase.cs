using Chapi.Domain.Interfaces;
using System.IO;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para obtener las ramas disponibles.
/// </summary>
public class GetBranchesUseCase
{
    private readonly IGitRepository _gitRepo;

    public GetBranchesUseCase(IGitRepository gitRepo)
    {
        _gitRepo = gitRepo;
    }

    public async Task<IEnumerable<string>> ExecuteAsync(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
            return Enumerable.Empty<string>();

        return await _gitRepo.GetBranchesAsync(projectPath);
    }
}
