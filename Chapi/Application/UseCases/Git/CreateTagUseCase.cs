using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para crear una nueva etiqueta (Tag).
/// </summary>
public class CreateTagUseCase
{
    private readonly IGitRepository _gitRepo;
    private readonly INotificationService _notificationService;

    public CreateTagUseCase(IGitRepository gitRepo, INotificationService notificationService)
    {
        _gitRepo = gitRepo;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Crea una nueva etiqueta.
    /// </summary>
    /// <param name="projectPath">Ruta del proyecto</param>
    /// <param name="tagName">Nombre de la etiqueta</param>
    /// <param name="message">Mensaje de la etiqueta (anotada)</param>
    /// <param name="commitHash">Hash del commit (opcional, null = HEAD)</param>
    public async Task<Result> ExecuteAsync(string projectPath, string tagName, string message, bool pushToRemote = true, string? commitHash = null)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return Result.Fail("La ruta del proyecto no puede estar vacia");

        if (string.IsNullOrWhiteSpace(tagName))
            return Result.Fail("El nombre de la etiqueta no puede estar vacio");

        try
        {
            _notificationService.ShowInfo($"Creando etiqueta '{tagName}'...");

            var result = await _gitRepo.CreateTagAsync(projectPath, tagName, message, commitHash);

            if (!result.IsSuccess)
            {
                _notificationService.ShowError($"❌ Error al crear etiqueta: {result.Error}");
                return result;
            }

            if (pushToRemote)
            {
                // Intentar subir al remoto
                _notificationService.ShowInfo($"Subiendo etiqueta '{tagName}' al remoto...");
                var pushResult = await _gitRepo.PushTagAsync(projectPath, tagName);

                if (pushResult.IsSuccess)
                {
                    _notificationService.ShowSuccess($"✅ Etiqueta '{tagName}' creada y subida correctamente");
                }
                else
                {
                    _notificationService.ShowWarning($"⚠️ Etiqueta creada localmente pero falló al subir: {pushResult.Error}");
                }
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"❌ Error al crear etiqueta: {ex.Message}");
            return Result.Fail(ex.Message);
        }
    }
}
