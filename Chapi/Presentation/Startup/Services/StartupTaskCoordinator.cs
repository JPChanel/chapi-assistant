using Chapi.Domain.Interfaces;
using Chapi.Infrastructure.Services;
using Chapi.Presentation.Shared.Tasks;
using Chapi.Presentation.Startup.Models;
using Chapi.Presentation.Shared.Dialogs.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using System.Windows;
using Velopack;
using Velopack.Sources;

namespace Chapi.Presentation.Startup.Services;

public sealed class StartupTaskCoordinator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IGitRepository _gitRepository;
    private int _viewModelEventsSubscribed;

    public StartupTaskCoordinator(IServiceProvider serviceProvider, IGitRepository gitRepository)
    {
        _serviceProvider = serviceProvider;
        _gitRepository = gitRepository;
    }

    public async Task CheckForUpdatesAsync(string updateUrl)
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(updateUrl, null, false));
            var info = await mgr.CheckForUpdatesAsync();
            if (info == null)
            {
                return;
            }

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                Msg.Assistant($"Nueva version v{info.TargetFullRelease.Version} disponible."));
        }
        catch
        {
        }
    }

    public async Task HandleWindowLoadedAsync(StartupTaskContext context)
    {
        await Task.Delay(300);

        context.MarkWindowInitialized();
        context.LoadProjects();
        EnsureViewModelSubscriptions(context);
        PreloadCloneRepositoryViewModel();

        Task.Run(PreloadAvatarsAsync).Forget("precargando avatares");
        CheckGitInstallationAsync(context).Forget("validando git");
    }

    private void EnsureViewModelSubscriptions(StartupTaskContext context)
    {
        if (Interlocked.Exchange(ref _viewModelEventsSubscribed, 1) == 1)
        {
            return;
        }

        if (context.ChangesViewModel != null)
        {
            context.ChangesViewModel.CommitCompleted += (_, _) =>
            {
                HandleCommitCompletedAsync(context).Forget("actualizando historial tras commit");
            };
        }

        if (context.HistoryViewModel != null)
        {
            context.HistoryViewModel.ResetCompleted += (_, _) =>
            {
                HandleResetCompletedAsync(context).Forget("actualizando cambios tras reset");
            };
        }

        if (context.ReleasesViewModel != null)
        {
            context.ReleasesViewModel.TagDeleted += (_, _) =>
            {
                context.LoadHistoryAsync().Forget("recargando historial tras eliminar tag");
            };
        }
    }

    private void PreloadCloneRepositoryViewModel()
    {
        _ = _serviceProvider.GetService<Presentation.Features.Projects.ViewModels.CloneRepositoryViewModel>();
    }

    private async Task PreloadAvatarsAsync()
    {
        var storage = _serviceProvider.GetService<ICredentialStorageService>();
        if (storage == null)
        {
            return;
        }

        await Domain.Services.AvatarCacheService.Instance.PreloadAvatarsAsync(storage);
    }

    private async Task CheckGitInstallationAsync(StartupTaskContext context)
    {
        var isGitInstalled = _gitRepository.IsGitInstalled();
        context.SetGitInstalled(isGitInstalled);

        if (isGitInstalled)
        {
            await context.UpdateProjectStatusesAsync();
        }

        await CheckSetupAsync(context.Owner);
    }

    private async Task CheckSetupAsync(Window owner)
    {
        var storage = _serviceProvider.GetService<ICredentialStorageService>();
        if (storage == null)
        {
            return;
        }

        var hasGitHub = await storage.HasCredentialAsync("GitHub");
        var hasGitLab = await storage.HasCredentialAsync("GitLab");
        if (hasGitHub || hasGitLab)
        {
            return;
        }

        await owner.Dispatcher.InvokeAsync(() =>
        {
            var viewModel = _serviceProvider.GetRequiredService<Presentation.Features.Git.ViewModels.GitProviderSelectionViewModel>();
            var dialog = new GitProviderSelectionDialog(viewModel)
            {
                Owner = owner
            };

            dialog.ShowDialog();
        });
    }

    private async Task HandleCommitCompletedAsync(StartupTaskContext context)
    {
        await context.LoadHistoryAsync();
        await context.UpdateProjectStatusesAsync();
    }

    private async Task HandleResetCompletedAsync(StartupTaskContext context)
    {
        await context.RefreshChangesAfterResetAsync();
        await context.UpdateProjectStatusesAsync();
    }
}
