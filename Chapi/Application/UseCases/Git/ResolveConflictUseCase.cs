using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Resuelve un conflicto dado su contenido resuelto y el archivo, haciendo staging automático.
/// </summary>
public class ResolveConflictUseCase
{
    private readonly IGitRepository _gitRepository;

    public ResolveConflictUseCase(IGitRepository gitRepository)
    {
        _gitRepository = gitRepository;
    }

    public async Task<Result> ExecuteAsync(string projectPath, string filePath, string resolvedContent)
    {
        return await _gitRepository.ResolveConflictAsync(projectPath, filePath, resolvedContent);
    }
}
