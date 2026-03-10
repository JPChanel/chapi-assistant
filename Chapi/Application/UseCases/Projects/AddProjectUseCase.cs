using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;

using System.IO;
namespace Chapi.Application.UseCases.Projects;

public class AddProjectUseCase
{
    private readonly IProjectRepository _projectRepository;

    public AddProjectUseCase(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<Result> ExecuteAsync(string projectPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return Result.Fail("La ruta del proyecto no puede estar vacía");

            if (!Directory.Exists(projectPath))
                return Result.Fail("El directorio no existe");

            await _projectRepository.AddProjectAsync(projectPath);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error agregando proyecto: {ex.Message}");
        }
    }
}

