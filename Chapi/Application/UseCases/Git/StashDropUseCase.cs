using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para eliminar un stash específico sin aplicarlo.
/// </summary>
public class StashDropUseCase
{
    private readonly IGitRepository _gitRepo;
    private readonly INotificationService _notificationService;

    public StashDropUseCase(IGitRepository gitRepo, INotificationService notificationService)
    {
        _gitRepo = gitRepo;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Elimina un stash específico.
    /// </summary>
    /// <param name="projectPath">Ruta del proyecto</param>
    /// <param name="stashIndex">Índice del stash (obligaorio)</param>
    public async Task<Result> ExecuteAsync(string projectPath, int stashIndex)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return Result.Fail("La ruta del proyecto no puede estar vacía");

        try
        {
            _notificationService.ShowInfo($"Eliminando stash@{stashIndex}...");

            var result = await _gitRepo.ExecuteGitCommandAsync(projectPath, $"stash drop stash@{{{stashIndex}}}");

            if (result.Contains("Dropped"))
            {
                _notificationService.ShowSuccess($"✅ Stash@{stashIndex} eliminado correctamente");
                return Result.Success();
            }

            return Result.Fail($"Error al eliminar stash: {result}");
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"❌ Error al eliminar stash: {ex.Message}");
            return Result.Fail(ex.Message);
        }
    }
}
