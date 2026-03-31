using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;
using Chapi.Presentation.Features.Git.Models;
using Chapi.Presentation.Features.Git.ViewModels;
using Chapi.Presentation.Shared.Dialogs.Views;
using Msg = Chapi.Infrastructure.Services.Msg;

namespace Chapi.Presentation.Features.Git.Workflows;

public sealed class MergeWorkflow
{
    private readonly IGitRepository _gitRepository;
    private readonly ConflictResolutionWorkflow _conflictResolutionWorkflow;

    public MergeWorkflow(IGitRepository gitRepository, ConflictResolutionWorkflow conflictResolutionWorkflow)
    {
        _gitRepository = gitRepository;
        _conflictResolutionWorkflow = conflictResolutionWorkflow;
    }

    public async Task ShowDialogAsync(GitWorkflowContext context, string mergeType)
    {
        var viewModel = new MergeBranchViewModel(_gitRepository, context.ProjectPath, mergeType);
        var branches = await _gitRepository.GetBranchesAsync(context.ProjectPath);
        viewModel.LoadBranches(branches, context.GetCurrentBranch());

        var dialog = new MergeBranchDialog
        {
            DataContext = viewModel
        };

        var dialogResult = await Chapi.Presentation.Shared.Dialogs.DialogService.ShowDialog(dialog);
        if (dialogResult is BranchItemViewModel selectedBranch)
        {
            await ExecuteAsync(
                context,
                mergeType,
                selectedBranch.Name,
                viewModel.IsDeleteSourceBranchChecked);
        }
    }

    public async Task ExecuteAsync(
        GitWorkflowContext context,
        string mergeType,
        string targetBranch,
        bool autoDeleteBranch = false)
    {
        var sourceBranch = context.GetCurrentBranch();

        if (targetBranch.Equals(sourceBranch, StringComparison.OrdinalIgnoreCase))
        {
                await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
                "Error",
                $"No puedes hacer {mergeType.ToLower()} de una rama consigo misma.",
                DialogVariant.Error,
                DialogType.Info);
            return;
        }

        if (!mergeType.Equals("Rebase", StringComparison.OrdinalIgnoreCase))
        {
            var (hasConflicts, _) = await _gitRepository.CheckMergeConflictsAsync(context.ProjectPath, targetBranch);
            if (hasConflicts)
            {
                await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
                    "Conflictos Detectados",
                    $"No se puede enviar '{sourceBranch}' a '{targetBranch}' porque hay conflictos pendientes.\n\nSOLUCION: Primero debes fusionar '{targetBranch}' en tu rama actual y resolver los conflictos.",
                    DialogVariant.Error,
                    DialogType.Info);
                return;
            }
        }

        var status = await _gitRepository.GetChangesAsync(context.ProjectPath);
        if (status.Any())
        {
            await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
                "Cambios Pendientes",
                "Para hacer merge hacia otra rama, tu directorio de trabajo debe estar limpio.\n\nPor favor haz commit o stash de tus cambios actuales antes de continuar.",
                DialogVariant.Warning,
                DialogType.Info);
            return;
        }

        var prompt = string.Empty;
        var variant = DialogVariant.Info;
        var squashCommitMessage = string.Empty;
        var shouldDeleteBranch = autoDeleteBranch;

        if (mergeType == "Squash")
        {
            var squashDialog = new SquashCommitDialog(
                _gitRepository,
                context.ProjectPath,
                sourceBranch,
                targetBranch,
                autoDeleteBranch);

            var dialogResult = await Chapi.Presentation.Shared.Dialogs.DialogService.ShowDialog(squashDialog);
            if (dialogResult is bool confirmed && confirmed)
            {
                squashCommitMessage = squashDialog.CommitMessage;
            }
            else
            {
                return;
            }
        }
        else
        {
            if (mergeType == "Rebase")
            {
                prompt =
                    "EL REBASE REQUERIRA FORCE PUSH\n\n" +
                    $"Estas seguro de que deseas hacer rebase a '{sourceBranch}' de '{targetBranch}'?\n\n" +
                    "Al finalizar el rebase, tu historia local cambiara y divergiras del remoto.\n" +
                    "Para actualizar el servidor, necesitaras hacer un FORCE PUSH posteriormente.\n" +
                    "Esto alterara la historia en el remoto y podria causar problemas a otros colaboradores en esta rama.\n\n" +
                    "Deseas continuar?";
                variant = DialogVariant.Warning;
            }
            else
            {
                prompt =
                    mergeType == "Merge"
                        ? $"Estas seguro de fusionar '{sourceBranch}' en '{targetBranch}'?\n\nEl sistema cambiara a '{targetBranch}', realizara la operacion y volvera."
                        : $"Estas seguro de hacer SQUASH MERGE de '{sourceBranch}' en '{targetBranch}'?\n\nEl sistema cambiara a '{targetBranch}', realizara la operacion y volvera.";
            }

            var confirm = await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
                $"{mergeType} operation",
                prompt,
                variant,
                DialogType.Confirm);
            if (!confirm)
            {
                return;
            }
        }

        await context.RunWithLoadingAsync(async () =>
        {
            var result = Result.Fail("Iniciando...");

            try
            {
                if (mergeType == "Rebase")
                {
                    result = await _gitRepository.RebaseBranchAsync(context.ProjectPath, targetBranch);
                }
                else
                {
                    var checkoutTarget = await _gitRepository.SwitchBranchAsync(context.ProjectPath, targetBranch);
                    if (!checkoutTarget.IsSuccess)
                    {
                        throw new Exception($"No se pudo cambiar a '{targetBranch}': {checkoutTarget.Error}");
                    }

                    result = mergeType == "Merge"
                        ? await _gitRepository.MergeBranchAsync(context.ProjectPath, sourceBranch, fastForward: true)
                        : await _gitRepository.SquashMergeBranchAsync(context.ProjectPath, sourceBranch, squashCommitMessage);
                }

                if (!result.IsSuccess)
                {
                    if (result.Error == "CONFLICTO_DETECTADO")
                    {
                        await _conflictResolutionWorkflow.HandleAsync(context);
                        return;
                    }

                    throw new Exception(result.Error);
                }

                Msg.Assistant($"Operacion '{mergeType}' exitosa: '{sourceBranch}' -> '{targetBranch}'");

                if (mergeType == "Rebase")
                {
                    var forcePushConfirm = await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
                        "Rebase Exitoso - Force Push Requerido",
                        "La rama actual se ha rebasado correctamente.\n\nTu historia local ha divergido del remoto.\nDeseas realizar un FORCE PUSH ahora para actualizar el servidor?\n(Solo hazlo si estas seguro de que nadie mas trabaja sobre esta rama)",
                        DialogVariant.Warning,
                        DialogType.Confirm);

                    if (forcePushConfirm)
                    {
                        var pushResult = await _gitRepository.PushAsync(context.ProjectPath, sourceBranch, force: true);
                        if (pushResult.IsSuccess)
                        {
                            Msg.Assistant($"Force Push exitoso: '{sourceBranch}' actualizado en remoto.");
                        }
                        else
                        {
                            await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
                                "Error Force Push",
                                pushResult.Error,
                                DialogVariant.Error,
                                DialogType.Info);
                        }
                    }

                    shouldDeleteBranch = false;
                }
                else
                {
                    context.SetCurrentBranch(targetBranch);
                    context.SelectBranch(targetBranch);

                    var pushConfirm = await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
                        "Push al Servidor",
                        $"El merge local en '{targetBranch}' fue exitoso.\n\nQuieres subir (Push) los cambios de '{targetBranch}' a origin ahora mismo para que se reflejen en GitHub/GitLab?",
                        DialogVariant.Info,
                        DialogType.Confirm);

                    if (pushConfirm)
                    {
                        var pushResult = await _gitRepository.PushAsync(context.ProjectPath, targetBranch);
                        if (pushResult.IsSuccess)
                        {
                            Msg.Assistant($"Push exitoso: '{targetBranch}' actualizado en remoto.");
                        }
                        else
                        {
                            await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
                                "Error al hacer Push",
                                pushResult.Error,
                                DialogVariant.Error,
                                DialogType.Info);
                        }
                    }
                }

                if (shouldDeleteBranch && mergeType != "Rebase")
                {
                    var deleteResult = await _gitRepository.DeleteBranchAsync(
                        context.ProjectPath,
                        sourceBranch,
                        force: true,
                        deleteRemote: true);

                    if (deleteResult.IsSuccess)
                    {
                        Msg.Assistant($"Rama '{sourceBranch}' eliminada (Local y Remoto).");
                    }
                    else
                    {
                        await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
                            "Aviso",
                            $"Se intento eliminar la rama '{sourceBranch}' pero hubo un problema: {deleteResult.Error}",
                            DialogVariant.Warning,
                            DialogType.Info);
                    }
                }

                await context.LoadChangesAsync();
                await context.LoadHistoryAsync();
                await context.UpdateProjectStatusesAsync();
                await context.RefreshBranchesAsync();
            }
            catch (Exception ex)
            {
                if (!string.Equals(context.GetCurrentBranch(), sourceBranch, StringComparison.OrdinalIgnoreCase))
                {
                    await _gitRepository.SwitchBranchAsync(context.ProjectPath, sourceBranch);
                }

                await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
                    $"Error en {mergeType}",
                    $"Ocurrio un error: {ex.Message}",
                    DialogVariant.Error,
                    DialogType.Info);

                await context.LoadChangesAsync();
            }
        });
    }
}
