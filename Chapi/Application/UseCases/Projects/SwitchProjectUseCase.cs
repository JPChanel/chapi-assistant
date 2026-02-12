
using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Domain.Interfaces;

using System.IO;
namespace Chapi.Application.UseCases.Projects;

public class SwitchProjectUseCase
{
    private readonly IProjectRepository _projectRepository;

    public SwitchProjectUseCase(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<Result<Project>> ExecuteAsync(string projectPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return Result<Project>.Fail("La ruta del proyecto no puede estar vacía");

            if (!Directory.Exists(projectPath))
                return Result<Project>.Fail("El directorio no existe");

            var project = await _projectRepository.GetProjectAsync(projectPath);

            if (project == null)
                return Result<Project>.Fail("Proyecto no encontrado");

            return Result<Project>.Success(project);
        }
        catch (Exception ex)
        {
            return Result<Project>.Fail($"Error cambiando proyecto: {ex.Message}");
        }
    }
}

