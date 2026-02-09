using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Projects;

public class RemoveProjectUseCase
{
    private readonly IProjectRepository _projectRepository;

    public RemoveProjectUseCase(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<Result> ExecuteAsync(string projectPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return Result.Fail("La ruta del proyecto no puede estar vacía");

            await _projectRepository.RemoveProjectAsync(projectPath);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error eliminando proyecto: {ex.Message}");
        }
    }
}
