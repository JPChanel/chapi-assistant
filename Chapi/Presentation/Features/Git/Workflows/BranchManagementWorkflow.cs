using Chapi.Domain.Interfaces;
using Chapi.Presentation.Features.Git.Models;
using Chapi.Presentation.Shared.Dialogs.Views;
using Msg = Chapi.Infrastructure.Services.Msg;

namespace Chapi.Presentation.Features.Git.Workflows;

public sealed class BranchManagementWorkflow
{
    private readonly IGitRepository _gitRepository;

    public BranchManagementWorkflow(IGitRepository gitRepository)
    {
        _gitRepository = gitRepository;
    }

    public async Task PublishAsync(GitWorkflowContext context)
    {
        await context.RunWithLoadingAsync(async () =>
        {
            var currentBranch = context.GetCurrentBranch();
            var result = await _gitRepository.PushAsync(context.ProjectPath, currentBranch);
            if (result.IsSuccess)
            {
                Msg.Assistant($"Rama '{currentBranch}' publicada en origin.");
                await context.CheckBranchStatusAsync();
            }
            else
            {
                    await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
                    "Error al publicar",
                    $"No se pudo publicar la rama: {result.Error}",
                    DialogVariant.Error,
                    DialogType.Info);
            }
        });
    }

    public async Task CreateAsync(GitWorkflowContext context, string? sourceBranch)
    {
        sourceBranch ??= context.GetCurrentBranch();
        if (string.IsNullOrWhiteSpace(sourceBranch))
        {
            return;
        }

        var (ok, newBranchName) = await Chapi.Presentation.Shared.Dialogs.DialogService.ShowInputDialog(
            "Crear Rama",
            $"Ingrese el nombre de la nueva rama (basada en '{sourceBranch}'):");

        if (!ok || string.IsNullOrWhiteSpace(newBranchName))
        {
            return;
        }

        await context.RunWithLoadingAsync(async () =>
        {
            var result = await _gitRepository.CreateBranchAsync(context.ProjectPath, newBranchName, sourceBranch);
            if (result.IsSuccess)
            {
                await context.RefreshBranchesAsync();
                Msg.Assistant($"Rama '{newBranchName}' creada correctamente.");
            }
            else
            {
                await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
                    "Error al crear rama",
                    result.Error,
                    DialogVariant.Error,
                    DialogType.Info);
            }
        });
    }

    public async Task DeleteAsync(GitWorkflowContext context, string branchName)
    {
        var currentBranch = context.GetCurrentBranch();
        if (branchName.Equals(currentBranch, StringComparison.OrdinalIgnoreCase))
        {
            await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
                "Error",
                $"No puedes eliminar la rama '{branchName}' porque es la rama activa.",
                DialogVariant.Error,
                DialogType.Info);
            return;
        }

        var confirm = await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
            "Eliminar Rama",
            $"Estas seguro de eliminar la rama '{branchName}'?",
            DialogVariant.Warning,
            DialogType.Confirm);
        if (!confirm)
        {
            return;
        }

        var confirmRemote = await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
            "Eliminar Remoto",
            $"Deseas eliminar tambien la rama '{branchName}' del repositorio remoto (origin)?",
            DialogVariant.Info,
            DialogType.Confirm);

        await context.RunWithLoadingAsync(async () =>
        {
            var result = await _gitRepository.DeleteBranchAsync(
                context.ProjectPath,
                branchName,
                force: false,
                deleteRemote: confirmRemote);

            if (result.IsSuccess)
            {
                await context.RefreshBranchesAsync();
                Msg.Assistant($"Rama '{branchName}' eliminada{(confirmRemote ? " (Local y Remoto)" : " (Local)") }.");
            }
            else if (result.Error.Contains("not fully merged", StringComparison.OrdinalIgnoreCase) ||
                     result.Error.Contains("force", StringComparison.OrdinalIgnoreCase))
            {
                await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
                    "Error al eliminar rama",
                    result.Error + "\n\nPara forzar el borrado (perdiendo cambios no fusionados), usa la terminal por ahora.",
                    DialogVariant.Error,
                    DialogType.Info);
            }
            else
            {
                await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
                    "Error al eliminar rama",
                    result.Error,
                    DialogVariant.Error,
                    DialogType.Info);
            }
        });
    }
}
