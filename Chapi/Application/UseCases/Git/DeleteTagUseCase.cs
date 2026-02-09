using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para eliminar una etiqueta (Tag).
/// </summary>
public class DeleteTagUseCase
{
    private readonly IGitRepository _gitRepo;
    private readonly INotificationService _notificationService;

    public DeleteTagUseCase(IGitRepository gitRepo, INotificationService notificationService)
    {
        _gitRepo = gitRepo;
        _notificationService = notificationService;
    }

    public async Task<Result> ExecuteAsync(string projectPath, string tagName)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return Result.Fail("La ruta del proyecto no puede estar vacia");

        if (string.IsNullOrWhiteSpace(tagName))
            return Result.Fail("El nombre de la etiqueta no puede estar vacio");

        try
        {
            _notificationService.ShowInfo($"Eliminando etiqueta '{tagName}'...");
            
            var result = await _gitRepo.DeleteTagLocalAsync(projectPath, tagName);

            if (result.IsSuccess)
            {
                _notificationService.ShowSuccess($"âœ… Etiqueta '{tagName}' eliminada correctamente");
                return Result.Success();
            }

            _notificationService.ShowError($"â Œ No se pudo eliminar la etiqueta: {result.Error}");
            return result;
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"â Œ Error al eliminar etiqueta: {ex.Message}");
            return Result.Fail(ex.Message);
        }
    }
}
