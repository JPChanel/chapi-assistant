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
            return Result.Fail("Directorio invalido");

        if (string.IsNullOrWhiteSpace(branchName))
            return Result.Fail("Nombre de rama invalido");

        // Si se solicita, hacer stash antes de cambiar de rama
        if (stashChanges)
        {
            // Intentar obtener rama actual para el mensaje
            string currentBranch = "unknown";
            try { currentBranch = await _gitRepo.GetCurrentBranchAsync(projectPath); } catch { }

            var stashResult = await _gitRepo.StashChangesAsync(projectPath, $"Auto-stash de {currentBranch}: Cambio a {branchName}");
            if (!stashResult.IsSuccess)
            {
                _notifications.ShowWarning("No se pudo crear el stash: " + stashResult.Error);
                return stashResult;
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
