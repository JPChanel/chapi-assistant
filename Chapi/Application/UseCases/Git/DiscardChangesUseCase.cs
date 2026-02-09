using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para descartar cambios en archivos.
/// </summary>
public class DiscardChangesUseCase
{
    private readonly IGitRepository _gitRepo;
    private readonly INotificationService _notificationService;

    public DiscardChangesUseCase(IGitRepository gitRepo, INotificationService notificationService)
    {
        _gitRepo = gitRepo;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Descarta cambios en archivos especificos o todos.
    /// </summary>
    /// <param name="projectPath">Ruta del proyecto</param>
    /// <param name="files">Archivos especificos (opcional, null = todos)</param>
    public async Task<Result> ExecuteAsync(string projectPath, IEnumerable<string>? files = null)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return Result.Fail("La ruta del proyecto no puede estar vacia");

        try
        {
            bool isAll = files == null || !files.Any();
            
            _notificationService.ShowInfo(isAll 
                ? "Descartando todos los cambios..." 
                : $"Descartando cambios en {files!.Count()} archivo(s)...");

            var result = await _gitRepo.DiscardChangesAsync(projectPath, files);
            if (!result.IsSuccess)
                return result;

            _notificationService.ShowSuccess(isAll 
                ? "Todos los cambios han sido descartados" 
                : $"Cambios descartados en {files!.Count()} archivo(s)");
            
            return Result.Success();
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"Error al descartar cambios: {ex.Message}");
            return Result.Fail(ex.Message);
        }
    }
}

