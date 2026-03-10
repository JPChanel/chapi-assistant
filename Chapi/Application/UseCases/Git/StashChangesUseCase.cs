using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para guardar cambios en el stash.
/// </summary>
public class StashChangesUseCase
{
    private readonly IGitRepository _gitRepo;
    private readonly INotificationService _notificationService;

    public StashChangesUseCase(IGitRepository gitRepo, INotificationService notificationService)
    {
        _gitRepo = gitRepo;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Guarda cambios en el stash.
    /// </summary>
    /// <param name="projectPath">Ruta del proyecto</param>
    /// <param name="message">Mensaje del stash</param>
    /// <param name="files">Archivos especificos (opcional, null = todos)</param>
    public async Task<Result> ExecuteAsync(string projectPath, string message, IEnumerable<string>? files = null)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return Result.Fail("La ruta del proyecto no puede estar vacia");

        if (string.IsNullOrWhiteSpace(message))
            message = "Stash automatico";

        try
        {
            _notificationService.ShowInfo($"Guardando cambios en stash: {message}");

            var result = await _gitRepo.StashChangesAsync(projectPath, message, files);
            if (!result.IsSuccess)
            {
                _notificationService.ShowError($"? Error al guardar en stash: {result.Error}");
                return result;
            }

            _notificationService.ShowSuccess("? Cambios guardados en stash");
            return Result.Success();
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"❌ Error al guardar en stash: {ex.Message}");
            return Result.Fail(ex.Message);
        }
    }
}

