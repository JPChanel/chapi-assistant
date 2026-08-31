using Chapi.Domain.Common;
using Chapi.Presentation.Shared.Tasks;
using Chapi.Presentation.Features.Git.Models;
using Chapi.Presentation.Shared.Dialogs.Views;
using Microsoft.Extensions.DependencyInjection;
using UseCases = Chapi.Application.UseCases.Git;

namespace Chapi.Presentation.Features.Git.Workflows;

public sealed class GitSyncWorkflow
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConflictResolutionWorkflow _conflictResolutionWorkflow;

    public GitSyncWorkflow(IServiceProvider serviceProvider, ConflictResolutionWorkflow conflictResolutionWorkflow)
    {
        _serviceProvider = serviceProvider;
        _conflictResolutionWorkflow = conflictResolutionWorkflow;
    }

    public async Task ExecuteAsync(GitWorkflowContext context, GitActionState action)
    {
        await context.RunWithLoadingAsync(async () =>
        {
            var currentBranch = context.GetCurrentBranch();
            var result = Result.Success();

            switch (action)
            {
                case GitActionState.Fetch:
                    var fetchUseCase = _serviceProvider.GetRequiredService<UseCases.FetchChangesUseCase>();
                    result = await fetchUseCase.ExecuteAsync(context.ProjectPath, isSilent: false);
                    break;

                case GitActionState.Pull:
                    var pullUseCase = _serviceProvider.GetRequiredService<UseCases.PullChangesUseCase>();
                    result = await pullUseCase.ExecuteAsync(context.ProjectPath, currentBranch, stashChanges: false);

                    if (!result.IsSuccess && UseCases.PullChangesUseCase.IsLocalChangesOverwriteError(result.Error))
                    {
                        var conflictingFiles = ExtractFilesFromPullOverwriteError(result.Error);
                        var details = BuildPullOverwriteDetailsAscii(conflictingFiles);
                        var proceedWithStash = await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
                            "No se puede hacer Pull",
                            details,
                            DialogVariant.Warning,
                            DialogType.Confirm,
                            confirmButtonText: "Guardar cambios y continuar",
                            cancelButtonText: "Cancelar");

                        if (!proceedWithStash)
                        {
                            return;
                        }

                        result = await pullUseCase.ExecuteAsync(
                            context.ProjectPath,
                            currentBranch,
                            stashChanges: true,
                            restoreAfterPull: true);
                    }
                    break;

                case GitActionState.Push:
                    var gitRepository = _serviceProvider.GetRequiredService<Chapi.Domain.Interfaces.IGitRepository>();
                    var remoteUrl = await gitRepository.GetRemoteUrlAsync(context.ProjectPath);
                    if (string.IsNullOrWhiteSpace(remoteUrl))
                    {
                        var (ok, newUrl) = await Chapi.Presentation.Shared.Dialogs.DialogService.ShowInputDialog(
                            "Asociar Repositorio Remoto",
                            "Este repositorio no tiene un origen remoto configurado.\n\nIngresa la URL remota (HTTPS o SSH) para subir tus cambios:",
                            "");

                        if (!ok || string.IsNullOrWhiteSpace(newUrl))
                        {
                            return;
                        }

                        var associateUseCase = _serviceProvider.GetRequiredService<UseCases.AssociateGitUseCase>();
                        var associateResult = await associateUseCase.ExecuteAsync(context.ProjectPath, newUrl.Trim());
                        if (!associateResult.IsSuccess)
                        {
                            await Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
                                "Error al asociar remoto",
                                $"No se pudo asociar la URL remota: {associateResult.Error}",
                                DialogVariant.Error,
                                DialogType.Info);
                            return;
                        }
                    }

                    var pushUseCase = _serviceProvider.GetRequiredService<UseCases.PushChangesUseCase>();
                    result = await pushUseCase.ExecuteAsync(context.ProjectPath, currentBranch);
                    break;
            }

            await context.LoadHistoryAsync();
            await context.UpdateProjectStatusesAsync();

            if (action != GitActionState.Fetch)
            {
                context.SyncProjectAsync().Forget("sincronizando cambios despues de accion git");
            }

            await context.ForceRefreshChangesAsync();

            if (!result.IsSuccess && UseCases.PullChangesUseCase.IsConflictError(result.Error))
            {
                await _conflictResolutionWorkflow.HandleAsync(context);
            }
        });
    }

    private static List<string> ExtractFilesFromPullOverwriteError(string error)
    {
        var files = new List<string>();
        if (string.IsNullOrWhiteSpace(error))
        {
            return files;
        }

        var lines = error.Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var readingFiles = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!readingFiles)
            {
                if (line.Contains("following files would be overwritten", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("archivos", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains("sobrescrit", StringComparison.OrdinalIgnoreCase))
                {
                    readingFiles = true;
                }

                continue;
            }

            if (line.StartsWith("Please ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Aborta", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("error:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("hint:", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var candidate = line.TrimStart('-', '*', ' ', '\t');
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                files.Add(candidate);
            }
        }

        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string BuildPullOverwriteDetailsAscii(List<string> files)
    {
        const string header = "No se puede hacer Pull porque estos archivos locales serian sobrescritos.";
        const string guidance = "Puedes guardar tus cambios en un Stash y continuar, o cancelar para revisarlos.";

        if (files == null || files.Count == 0)
        {
            return $"{header}\n\n{guidance}";
        }

        var max = Math.Min(files.Count, 12);
        var listed = string.Join("\n", files.Take(max).Select(file => $"- {file}"));
        var more = files.Count > max ? $"\n- ... y {files.Count - max} archivo(s) mas" : string.Empty;

        return $"{header}\n\n{listed}{more}\n\n{guidance}";
    }
}
