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

    public async Task<Result> ExecuteAsync(string projectPath, string branch, bool stashChanges = false, bool restoreAfterPull = true)
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

        string? temporaryStashName = null;

        // Si se solicita, hacer stash antes del pull
        if (stashChanges)
        {
            _notifications.ShowInfo("Guardando cambios locales en un stash temporal...");

            var stashNamesBefore = (await _gitRepo.ListStashesAsync(projectPath))
                .Select(s => s.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var stashResult = await _gitRepo.StashChangesAsync(projectPath, $"Auto-stash antes de pull en {branch}");
            if (!stashResult.IsSuccess)
            {
                _notifications.ShowWarning("No se pudo crear el stash: " + stashResult.Error);
                return stashResult;
            }

            var stashesAfter = (await _gitRepo.ListStashesAsync(projectPath)).ToList();
            temporaryStashName = stashesAfter
                .Select(s => s.Name)
                .FirstOrDefault(name => !stashNamesBefore.Contains(name));

            if (string.IsNullOrWhiteSpace(temporaryStashName))
            {
                temporaryStashName = stashesAfter.FirstOrDefault()?.Name;
            }

            if (string.IsNullOrWhiteSpace(temporaryStashName))
            {
                _notifications.ShowWarning("No se pudo confirmar la creacion del stash temporal.");
                return Result.Fail("No se pudo confirmar la creacion del stash temporal.");
            }

            _notifications.ShowInfo($"Cambios guardados en {temporaryStashName}.");
        }

        var result = await _gitRepo.PullAsync(projectPath, branch);

        if (result.IsSuccess)
        {
            _notifications.ShowSuccess($"✅ Pull exitoso desde {branch}");

            // Si hicimos stash, opcionalmente intentamos recuperarlo
            if (stashChanges)
            {
                if (restoreAfterPull)
                {
                    _notifications.ShowInfo("Restaurando cambios guardados...");
                    var popResult = await _gitRepo.StashPopAsync(projectPath, 0);
                    if (!popResult.IsSuccess)
                    {
                        _notifications.ShowWarning("Pull completado, pero hubo conflictos al restaurar tus cambios locales. Por favor revisa los stashes.");
                    }
                }
                else if (!string.IsNullOrWhiteSpace(temporaryStashName))
                {
                    _notifications.ShowInfo($"Tus cambios quedaron en {temporaryStashName}. Puedes recuperarlos desde la seccion Stash.");
                }
            }
        }
        else
        {
            if (stashChanges && restoreAfterPull)
            {
                _notifications.ShowWarning("El pull fallo. Intentando restaurar los cambios guardados...");
                var popResult = await _gitRepo.StashPopAsync(projectPath, 0);
                if (!popResult.IsSuccess)
                {
                    _notifications.ShowWarning("No se pudieron restaurar automaticamente tus cambios. Revisa la lista de stashes.");
                }
            }

            if (result.Error == "CONFLICTO_DETECTADO")
            {
                _notifications.ShowWarning("Conflictos detectados. Por favor, resuélvelos en la ventana correspondiente.");
            }
            else if (IsLocalChangesOverwriteError(result.Error))
            {
                // Este caso se maneja en UI con un modal específico y listado de archivos.
                _notifications.ShowWarning("No se pudo hacer pull porque algunos cambios locales serían sobrescritos.");
            }
            else
            {
                _notifications.ShowError($"❌ Error al hacer pull: {result.Error}");
            }
        }

        return result;
    }

    public static bool IsLocalChangesOverwriteError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return false;

        return error.Contains("would be overwritten by merge", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Your local changes to the following files would be overwritten", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Please commit your changes or stash them before you merge", StringComparison.OrdinalIgnoreCase)
            || error.Contains("tus cambios locales", StringComparison.OrdinalIgnoreCase)
            || error.Contains("serian sobrescritos", StringComparison.OrdinalIgnoreCase)
            || error.Contains("serán sobrescritos", StringComparison.OrdinalIgnoreCase);
    }
}
