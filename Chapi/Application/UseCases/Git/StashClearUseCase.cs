using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para limpiar todos los stashes.
/// </summary>
public class StashClearUseCase
{
    private readonly IGitRepository _gitRepo;
    private readonly INotificationService _notificationService;

    public StashClearUseCase(IGitRepository gitRepo, INotificationService notificationService)
    {
        _gitRepo = gitRepo;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Elimina todos los stashes.
    /// </summary>
    public async Task<Result> ExecuteAsync(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return Result.Fail("La ruta del proyecto no puede estar vacía");

        try
        {
            _notificationService.ShowInfo("Limpiando todos los stashes...");

            var result = await _gitRepo.ExecuteGitCommandAsync(projectPath, "stash clear");

            _notificationService.ShowSuccess("✅ Todos los stashes han sido eliminados");
            return Result.Success();
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"❌ Error al limpiar stashes: {ex.Message}");
            return Result.Fail(ex.Message);
        }
    }
}
