using Chapi.Domain.Entities;
using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para cargar los cambios del repositorio.
/// </summary>
public class LoadChangesUseCase
{
    private readonly IGitRepository _gitRepo;

    public LoadChangesUseCase(IGitRepository gitRepo)
    {
        _gitRepo = gitRepo;
    }

    public async Task<IEnumerable<FileChange>> ExecuteAsync(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return Enumerable.Empty<FileChange>();

        var changes = await _gitRepo.GetChangesAsync(projectPath);

        // Ordenar por ruta de archivo
        return changes.OrderBy(c => c.FilePath).ToList();
    }
}
