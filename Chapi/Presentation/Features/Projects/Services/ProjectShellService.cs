using Chapi.Domain.Interfaces;
using Chapi.Domain.Models;
using Chapi.Infrastructure.Persistence.Settings;
using Chapi.Presentation.Features.Projects.Models;
using Chapi.Presentation.Features.Assistant.ViewModels;
using Chapi.Presentation.Features.Changes.ViewModels;
using Chapi.Presentation.Features.Documentation.ViewModels;
using Chapi.Presentation.Features.History.ViewModels;
using Chapi.Presentation.Features.Releases.ViewModels;
using Chapi.Presentation.Features.Workspace.ViewModels;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using UseCases = Chapi.Application.UseCases.Git;

namespace Chapi.Presentation.Features.Projects.Services;

public sealed class ProjectShellService
{
    private readonly IGitRepository _gitRepository;
    private readonly IServiceProvider _serviceProvider;

    public ProjectShellService(IGitRepository gitRepository, IServiceProvider serviceProvider)
    {
        _gitRepository = gitRepository;
        _serviceProvider = serviceProvider;
    }

    public IReadOnlyList<ProjectViewModel> LoadProjects()
    {
        var data = ProjectSettings.LoadData();
        var groupsById = data.Groups.ToDictionary(g => g.Id, g => g);
        var result = new List<ProjectViewModel>();

        foreach (var path in data.Projects)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var dirName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(dirName)) dirName = path;

            string? groupId = null;
            string groupName = "Sin Agrupar";
            int groupOrder = int.MaxValue;

            if (data.Mappings.TryGetValue(path, out var gid) && !string.IsNullOrEmpty(gid) && groupsById.TryGetValue(gid, out var group))
            {
                groupId = group.Id;
                groupName = group.Name;
                groupOrder = group.Order;
            }

            result.Add(new ProjectViewModel
            {
                FullPath = path,
                Name = dirName,
                Icon = PackIconKind.FolderOutline,
                GroupId = groupId,
                GroupName = groupName,
                GroupOrder = groupOrder
            });
        }

        return result;
    }

    public Task<ProjectSelectionSnapshot> LoadProjectContextAsync(
        ProjectSelectionRequest request,
        CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentBranch = string.Empty;
            var branches = new List<string>();
            var ahead = 0;
            var needsPublish = false;

            var getBranchesUseCase = _serviceProvider.GetService<UseCases.GetBranchesUseCase>();
            var metadataTask = _gitRepository.GetMetadataAsync(request.ProjectPath);
            var branchesTask = getBranchesUseCase != null
                ? getBranchesUseCase.ExecuteAsync(request.ProjectPath)
                : Task.FromResult(Enumerable.Empty<string>());

            await Task.WhenAll(metadataTask, branchesTask);
            cancellationToken.ThrowIfCancellationRequested();

            var metadataResult = await metadataTask;
            var branchesResult = (await branchesTask).ToList();

            if (metadataResult.IsSuccess)
            {
                currentBranch = metadataResult.Data.CurrentBranch ?? string.Empty;
                ahead = metadataResult.Data.Ahead;
                needsPublish = !metadataResult.Data.HasUpstream;
            }

            branches = branchesResult;
            if (!string.IsNullOrWhiteSpace(currentBranch) && !branches.Contains(currentBranch))
            {
                branches.Insert(0, currentBranch);
            }

            return new ProjectSelectionSnapshot
            {
                CurrentBranch = currentBranch,
                Branches = branches,
                Ahead = ahead,
                NeedsPublish = needsPublish
            };
        }, cancellationToken);
    }

    public async Task WarmProjectContextAsync(ProjectSelectionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await LoadWorkspaceAsync(request.ProjectPath, request.WorkspaceViewModel);

        cancellationToken.ThrowIfCancellationRequested();
        await UpdateAssistantContextAsync(
            request.ProjectPath,
            request.AssistantViewModel,
            request.DocumentationViewModel);

        cancellationToken.ThrowIfCancellationRequested();
        await LoadReleasesAsync(request.ProjectPath, request.ReleasesViewModel);
    }

    public async Task<bool> HasPendingChangesAsync(string projectPath, ChangesViewModel? changesViewModel)
    {
        if (changesViewModel != null &&
            string.Equals(changesViewModel.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase))
        {
            return changesViewModel.HasPendingChanges;
        }

        var changes = await _gitRepository.GetChangesAsync(projectPath);
        return changes.Any();
    }

    public async Task LoadChangesAsync(string projectPath, ChangesViewModel? changesViewModel)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || changesViewModel == null) return;
        await changesViewModel.LoadChangesAsync();
    }

    public async Task LoadReleasesAsync(string projectPath, ReleasesViewModel? releasesViewModel)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || releasesViewModel == null) return;

        releasesViewModel.ProjectPath = projectPath;
        await releasesViewModel.LoadReleasesAsync();
    }

    public async Task LoadHistoryAsync(string projectPath, HistoryViewModel? historyViewModel)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || historyViewModel == null) return;

        historyViewModel.ProjectPath = projectPath;
        await historyViewModel.ReloadHistoryAsync();
    }

    public async Task LoadWorkspaceAsync(string projectPath, WorkspaceViewModel? workspaceViewModel)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || workspaceViewModel == null) return;
        await workspaceViewModel.InitializeAsync(projectPath);
    }

    public async Task UpdateAssistantContextAsync(
        string projectPath,
        AssistantViewModel? assistantViewModel,
        DocumentationViewModel? documentationViewModel)
    {
        if (string.IsNullOrWhiteSpace(projectPath)) return;

        if (assistantViewModel != null)
        {
            await assistantViewModel.UpdateProjectContextAsync(projectPath);
        }

        if (documentationViewModel != null)
        {
            await documentationViewModel.SetProjectContextAsync(
                new DirectoryInfo(projectPath).Name,
                projectPath);
        }
    }

    public async Task<bool> CheckNeedsPublishAsync(string projectPath, string? currentBranch)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(currentBranch))
        {
            return false;
        }

        return !await _gitRepository.HasUpstreamAsync(projectPath, currentBranch);
    }

    public async Task<ProjectBranchSnapshot> RefreshBranchesAsync(string projectPath)
    {
        var branches = (await _gitRepository.GetBranchesAsync(projectPath)).ToList();
        var activeBranch = await _gitRepository.GetCurrentBranchAsync(projectPath);

        if (!string.IsNullOrWhiteSpace(activeBranch) && !branches.Contains(activeBranch))
        {
            branches.Add(activeBranch);
        }

        return new ProjectBranchSnapshot
        {
            CurrentBranch = activeBranch ?? string.Empty,
            Branches = branches
        };
    }
}
