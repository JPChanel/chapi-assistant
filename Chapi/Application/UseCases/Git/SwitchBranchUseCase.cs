using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;
using System.IO;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para cambiar de rama.
/// </summary>
public class SwitchBranchUseCase
{
    private readonly IGitRepository _gitRepo;
    private readonly INotificationService _notifications;

    public SwitchBranchUseCase(IGitRepository gitRepo, INotificationService notifications)
    {
        _gitRepo = gitRepo;
        _notifications = notifications;
    }

    public async Task<Result> ExecuteAsync(string projectPath, string branchName, bool stashChanges = false)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            _notifications.ShowWarning("Ruta de proyecto inválida");
            return Result.Fail("Ruta de proyecto inválida");
        }

        if (string.IsNullOrWhiteSpace(branchName))
        {
            _notifications.ShowWarning("Nombre de rama inválido");
            return Result.Fail("Nombre de rama inválido");
        }

        // Si se solicita, hacer stash antes de cambiar de rama
        if (stashChanges)
        {
            var stashCommand = await _gitRepo.ExecuteGitCommandAsync(projectPath, "stash push -m \"Auto-stash al cambiar de rama\"");
            if (string.IsNullOrEmpty(stashCommand) || stashCommand.Contains("fatal:"))
            {
                _notifications.ShowWarning("No se pudo crear el stash");
                return Result.Fail("Error al crear stash");
            }
        }

        var result = await _gitRepo.SwitchBranchAsync(projectPath, branchName);

        if (result.IsSuccess)
        {
            _notifications.ShowSuccess($"✅ Cambiado a rama: {branchName}");
        }
        else
        {
            _notifications.ShowError($"❌ Error al cambiar de rama: {result.Error}");
        }

        return result;
    }
}
