using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para obtener la lista de archivos cambiados en un commit específico.
/// </summary>
public class GetFilesChangedInCommitUseCase
{
    private readonly IGitRepository _gitRepo;

    public GetFilesChangedInCommitUseCase(IGitRepository gitRepo)
    {
        _gitRepo = gitRepo;
    }

    public async Task<IEnumerable<string>> ExecuteAsync(string projectPath, string hash)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(hash))
            return Enumerable.Empty<string>();

        return await _gitRepo.GetFilesChangedInCommitAsync(projectPath, hash);
    }
}
