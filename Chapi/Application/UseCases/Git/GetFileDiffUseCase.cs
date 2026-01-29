using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para obtener el contenido de un archivo antes y después de un commit.
/// Esto permite generar diffs en el historial.
/// </summary>
public class GetFileDiffUseCase
{
    private readonly IGitRepository _gitRepo;

    public GetFileDiffUseCase(IGitRepository gitRepo)
    {
        _gitRepo = gitRepo;
    }

    public async Task<(string OldText, string NewText)> ExecuteAsync(string projectPath, string file, string commitHash)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(commitHash))
            return (string.Empty, string.Empty);

        // 1. Obtener el commit "padre"
        string parentHash = await _gitRepo.GetCommitParentHashAsync(projectPath, commitHash);
        
        // 2. Obtener el texto del archivo en el commit PADRE (el "antes")
        // Si no hay padre (primer commit), el texto antiguo es vacío
        string oldText = string.IsNullOrEmpty(parentHash) 
            ? string.Empty 
            : await _gitRepo.GetFileContentAtCommitAsync(projectPath, file, parentHash);

        // 3. Obtener el texto del archivo en el commit ACTUAL (el "después")
        string newText = await _gitRepo.GetFileContentAtCommitAsync(projectPath, file, commitHash);

        return (oldText, newText);
    }
}
