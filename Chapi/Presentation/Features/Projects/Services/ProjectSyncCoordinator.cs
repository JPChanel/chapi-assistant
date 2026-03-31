using Chapi.Application.UseCases.Projects;
using Chapi.Domain.Models;
using Chapi.Presentation.Features.Projects.Models;
using UseCases = Chapi.Application.UseCases.Git;

namespace Chapi.Presentation.Features.Projects.Services;

public sealed class ProjectSyncCoordinator
{
    private readonly UseCases.FetchChangesUseCase _fetchChangesUseCase;
    private readonly UpdateProjectIndicatorsUseCase _updateProjectIndicatorsUseCase;
    private readonly SemaphoreSlim _fetchRefreshSemaphore = new(1, 1);

    public ProjectSyncCoordinator(
        UseCases.FetchChangesUseCase fetchChangesUseCase,
        UpdateProjectIndicatorsUseCase updateProjectIndicatorsUseCase)
    {
        _fetchChangesUseCase = fetchChangesUseCase;
        _updateProjectIndicatorsUseCase = updateProjectIndicatorsUseCase;
    }

    public async Task FetchAndRefreshAsync(ProjectSyncContext context, bool isSilent = false)
    {
        if (string.IsNullOrWhiteSpace(context.ProjectPath))
        {
            return;
        }

        if (!await _fetchRefreshSemaphore.WaitAsync(0))
        {
            return;
        }

        try
        {
            var result = await _fetchChangesUseCase.ExecuteAsync(context.ProjectPath, isSilent);
            if (!result.IsSuccess)
            {
                return;
            }

            try
            {
                await context.RefreshBranchesAsync();
            }
            catch
            {
            }

            var changesProjectPath = context.GetChangesProjectPath();
            var sameProject = string.Equals(changesProjectPath, context.ProjectPath, StringComparison.OrdinalIgnoreCase);
            var shouldRefresh = sameProject && (!isSilent || context.IsChangesTabActive());
            if (!shouldRefresh)
            {
                return;
            }

            if (context.IsWslProject())
            {
                await context.ForceRefreshChangesAsync();
            }
            else
            {
                await context.RefreshChangesIfNecessaryAsync();
            }
        }
        finally
        {
            _fetchRefreshSemaphore.Release();
        }
    }

    public async Task UpdateProjectStatusesAsync(ProjectSyncContext context, IReadOnlyList<ProjectViewModel>? projects = null)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (projects == null)
        {
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                projects = await dispatcher.InvokeAsync(() =>
                    (IReadOnlyList<ProjectViewModel>)context.GetLoadedProjects()
                        .Where(project => project.FullPath == context.ProjectPath)
                        .ToList());
            }
            else
            {
                projects = context.GetLoadedProjects()
                    .Where(project => project.FullPath == context.ProjectPath)
                    .ToList();
            }
        }

        if (projects.Count == 0)
        {
            return;
        }

        var tasks = projects.Select(project =>
            _updateProjectIndicatorsUseCase.ExecuteAsync(project.FullPath, (ahead, behind) =>
            {
                if (dispatcher == null || dispatcher.CheckAccess())
                {
                    project.Ahead = ahead;
                    project.Behind = behind;
                    return;
                }

                dispatcher.Invoke(() =>
                {
                    project.Ahead = ahead;
                    project.Behind = behind;
                });
            }));

        await Task.WhenAll(tasks);

        if (!projects.Any(project => project.FullPath == context.ProjectPath))
        {
            return;
        }

        bool isProjectDropdownOpen;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            isProjectDropdownOpen = await dispatcher.InvokeAsync(context.IsProjectDropdownOpen);
        }
        else
        {
            isProjectDropdownOpen = context.IsProjectDropdownOpen();
        }

        if (isProjectDropdownOpen)
        {
            return;
        }

        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            Task? uiTask = null;
            dispatcher.Invoke(() =>
            {
                uiTask = RefreshSelectedProjectUiAsync(context);
            });

            if (uiTask != null)
            {
                await uiTask;
            }
        }
        else
        {
            await RefreshSelectedProjectUiAsync(context);
        }
    }

    private static async Task RefreshSelectedProjectUiAsync(ProjectSyncContext context)
    {
        await context.RefreshBranchesAsync();
        await context.CheckBranchStatusAsync();
    }
}
