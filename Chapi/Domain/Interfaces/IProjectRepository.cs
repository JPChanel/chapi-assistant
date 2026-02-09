using Chapi.Domain.Entities;

namespace Chapi.Domain.Interfaces;

/// <summary>
/// Repositorio para gestion de proyectos.
/// </summary>
public interface IProjectRepository
{
    Task<IEnumerable<Project>> GetAllProjectsAsync();
    Task<Project?> GetProjectAsync(string path);
    Task AddProjectAsync(string path);
    Task RemoveProjectAsync(string path);
}

