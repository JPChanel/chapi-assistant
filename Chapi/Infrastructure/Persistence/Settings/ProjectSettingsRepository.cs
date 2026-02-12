using Chapi.Domain.Entities;
using Chapi.Domain.Interfaces;
using System.IO;

namespace Chapi.Infrastructure.Persistence.Settings;

public class ProjectSettingsRepository : IProjectRepository
{
    public Task<IEnumerable<Project>> GetAllProjectsAsync()
    {
        var paths = ProjectSettings.LoadProjects();
        var projects = paths.Select(p => new Project
        {
            FullPath = p,
            Name = Path.GetFileName(p)
        });
        return Task.FromResult(projects);
    }

    public Task<Project?> GetProjectAsync(string path)
    {
        var paths = ProjectSettings.LoadProjects();
        var p = paths.FirstOrDefault(x => x == path);
        if (p == null) return Task.FromResult<Project?>(null);

        return Task.FromResult<Project?>(new Project
        {
            FullPath = p,
            Name = Path.GetFileName(p)
        });
    }

    public Task AddProjectAsync(string path)
    {
        ProjectSettings.AddProject(path);
        return Task.CompletedTask;
    }

    public Task RemoveProjectAsync(string path)
    {
        ProjectSettings.RemoveProject(path);
        return Task.CompletedTask;
    }
}
