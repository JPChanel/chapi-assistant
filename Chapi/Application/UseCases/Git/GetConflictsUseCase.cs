using Chapi.Domain.Entities;
using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Extrae la lista de archivos con conflictos de merge.
/// </summary>
public class GetConflictsUseCase
{
    private readonly IGitRepository _gitRepository;

    public GetConflictsUseCase(IGitRepository gitRepository)
    {
        _gitRepository = gitRepository;
    }

    public async Task<IEnumerable<GitConflict>> ExecuteAsync(string projectPath)
    {
        return await _gitRepository.GetMergeConflictsAsync(projectPath);
    }
}
