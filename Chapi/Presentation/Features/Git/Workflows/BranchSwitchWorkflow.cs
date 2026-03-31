using Chapi.Presentation.Features.Git.Models;
using Chapi.Presentation.Shared.Tasks;
using Chapi.Presentation.Shared.Dialogs.Views;
using Microsoft.Extensions.DependencyInjection;
using UseCases = Chapi.Application.UseCases.Git;

namespace Chapi.Presentation.Features.Git.Workflows;

public sealed class BranchSwitchWorkflow
{
    private readonly IServiceProvider _serviceProvider;

    public BranchSwitchWorkflow(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<bool> ExecuteAsync(GitWorkflowContext context, string newBranch)
    {
        var currentBranch = context.GetCurrentBranch();
        var switchedSuccessfully = false;

        await context.RunWithLoadingAsync(async () =>
        {
            var hasChanges = await context.HasPendingChangesAsync();
            var stashChanges = false;

            if (hasChanges)
            {
                var dialog = new SwitchBranchDialog
                {
                    TargetBranch = newBranch
                };

                var dialogResult = await Chapi.Presentation.Shared.Dialogs.DialogService.ShowDialog(dialog);
                if (dialogResult == null || dialogResult.ToString() == "cancel")
                {
                    context.SelectBranch(currentBranch);
                    return;
                }

                stashChanges = dialogResult.ToString() == "stash";
            }

            try
            {
                using var watcherSilencer = context.SuspendWatcher?.Invoke();
                var useCase = _serviceProvider.GetService<UseCases.SwitchBranchUseCase>();
                if (useCase == null)
                {
                    throw new InvalidOperationException("No se pudo resolver SwitchBranchUseCase.");
                }

                var switchResult = await useCase.ExecuteAsync(context.ProjectPath, newBranch, stashChanges);
                if (switchResult.IsSuccess)
                {
                    context.SetCurrentBranch(newBranch);
                    context.SelectBranch(newBranch);
                    switchedSuccessfully = true;
                }
                else
                {
                    context.SelectBranch(currentBranch);
                    await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
                        "No se pudo cambiar de rama",
                        switchResult.Error,
                        DialogVariant.Error,
                        DialogType.Info);
                }
            }
            catch (Exception ex)
            {
                context.SelectBranch(currentBranch);
                await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
                    "Error al cambiar de rama",
                    $"Excepcion inesperada:\n{ex.Message}",
                    DialogVariant.Error,
                    DialogType.Info);
            }
        });

        if (!switchedSuccessfully)
        {
            return false;
        }

        context.RefreshBranchesAsync().Forget("refrescando ramas");
        await context.ForceRefreshChangesAsync();
        await context.LoadHistoryAsync();
        await context.CheckBranchStatusAsync();
        await context.UpdateProjectStatusesAsync();
        return true;
    }
}
