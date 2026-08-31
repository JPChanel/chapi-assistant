using Chapi.Presentation.Features.Git.Models;
using Chapi.Presentation.Features.Git.ViewModels;
using Chapi.Presentation.Shared.Dialogs.Views;
using Microsoft.Extensions.DependencyInjection;
using Msg = Chapi.Infrastructure.Services.Msg;
using UseCases = Chapi.Application.UseCases.Git;

namespace Chapi.Presentation.Features.Git.Workflows;

public sealed class ConflictResolutionWorkflow
{
    private readonly IServiceProvider _serviceProvider;

    public ConflictResolutionWorkflow(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task HandleAsync(string projectPath, Func<Task>? onResolved = null)
    {
        try
        {
            var getConflictsUseCase = _serviceProvider.GetRequiredService<UseCases.GetConflictsUseCase>();
            var resolveConflictUseCase = _serviceProvider.GetRequiredService<UseCases.ResolveConflictUseCase>();

            var viewModel = new ConflictResolutionViewModel(
                projectPath,
                getConflictsUseCase,
                resolveConflictUseCase);

            await viewModel.LoadConflictsAsync();

            if (viewModel.Conflicts.Any())
            {
                var dialog = new ConflictResolutionDialog(viewModel);
                await Chapi.Presentation.Shared.Dialogs.DialogService.ShowDialog(dialog);
                if (onResolved != null)
                {
                    await onResolved();
                }
            }
            else
            {
                Msg.Assistant("No se encontraron conflictos a revisar o ya estan resueltos.");
            }
        }
        catch (Exception ex)
        {
            Msg.Assistant($"Error abriendo ventana de conflictos: {ex.Message}");
        }
    }

    public Task HandleAsync(GitWorkflowContext context)
    {
        return HandleAsync(context.ProjectPath, async () =>
        {
            await context.LoadChangesAsync();
            await context.LoadHistoryAsync();
            await context.UpdateProjectStatusesAsync();
        });
    }
}
