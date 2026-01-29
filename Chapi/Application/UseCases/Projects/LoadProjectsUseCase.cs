using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;
using Chapi.Domain.Entities;

namespace Chapi.Application.UseCases.Projects;

public class LoadProjectsUseCase
{
    private readonly IProjectRepository _projectRepository;

    public LoadProjectsUseCase(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<Result<IEnumerable<Project>>> ExecuteAsync()
    {
        try
        {
            var projects = await _projectRepository.GetAllProjectsAsync();
            return Result<IEnumerable<Project>>.Success(projects);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<Project>>.Fail($"Error cargando proyectos: {ex.Message}");
        }
    }
}
