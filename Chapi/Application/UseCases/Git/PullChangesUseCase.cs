using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;
using System.IO;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para hacer pull de cambios del remoto.
/// </summary>
public class PullChangesUseCase
{
    private readonly IGitRepository _gitRepo;
    private readonly INotificationService _notifications;

    public PullChangesUseCase(IGitRepository gitRepo, INotificationService notifications)
    {
        _gitRepo = gitRepo;
        _notifications = notifications;
    }

    public async Task<Result> ExecuteAsync(string projectPath, string branch, bool stashChanges = false)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            _notifications.ShowWarning("Ruta de proyecto invalida");
            return Result.Fail("Ruta de proyecto invalida");
        }

        if (string.IsNullOrWhiteSpace(branch))
        {
            _notifications.ShowWarning("Nombre de rama invalido");
            return Result.Fail("Nombre de rama invalido");
        }

        // Si se solicita, hacer stash antes del pull
        if (stashChanges)
        {
            var stashResult = await _gitRepo.StashChangesAsync(projectPath, $"Auto-stash antes de pull en {branch}");
            if (!stashResult.IsSuccess)
            {
                _notifications.ShowWarning("No se pudo crear el stash: " + stashResult.Error);
                return stashResult;
            }
        }

        var result = await _gitRepo.PullAsync(projectPath, branch);

        if (result.IsSuccess)
        {
            _notifications.ShowSuccess($"✅ Pull exitoso desde {branch}");

            // Si hicimos stash, intentamos recuperarlo
            if (stashChanges)
            {
                _notifications.ShowInfo("Restaurando cambios guardados...");
                var popResult = await _gitRepo.StashPopAsync(projectPath, 0);
                if (!popResult.IsSuccess)
                {
                    _notifications.ShowWarning("Pull completado, pero hubo conflictos al restaurar tus cambios locales. Por favor revisa los stashes.");
                }
            }
        }
        else
        {
            if (result.Error == "CONFLICTO_DETECTADO")
            {
                _notifications.ShowWarning("Conflictos detectados. Por favor, resuélvelos en la ventana correspondiente.");
            }
            else
            {
                _notifications.ShowError($"❌ Error al hacer pull: {result.Error}");
            }
        }

        return result;
    }
}
