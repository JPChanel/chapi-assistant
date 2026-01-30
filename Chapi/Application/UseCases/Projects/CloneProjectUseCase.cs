using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;
using System.IO;

namespace Chapi.Application.UseCases.Projects;

public class CloneProjectUseCase
{
    private readonly IGitRepository _gitRepository;
    private readonly IProjectRepository _projectRepository;

    public CloneProjectUseCase(IGitRepository gitRepository, IProjectRepository projectRepository)
    {
        _gitRepository = gitRepository;
        _projectRepository = projectRepository;
    }

    public async Task<Result<string>> ExecuteAsync(string repoUrl, string parentDirectory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(repoUrl))
                return Result<string>.Fail("La URL del repositorio no puede estar vacía");

            if (string.IsNullOrWhiteSpace(parentDirectory))
                return Result<string>.Fail("La ruta de destino no puede estar vacía");

            string projectName = Path.GetFileNameWithoutExtension(repoUrl);
            string targetPath = Path.Combine(parentDirectory, projectName);

            if (Directory.Exists(targetPath))
                return Result<string>.Fail($"El directorio ya existe: {targetPath}");

            var result = await _gitRepository.CloneAsync(repoUrl, targetPath);
            if (!result.IsSuccess) return Result<string>.Fail(result.Error);

            await _projectRepository.AddProjectAsync(targetPath);

            return Result<string>.Success(targetPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Error al clonar proyecto: {ex.Message}");
        }
    }
}
