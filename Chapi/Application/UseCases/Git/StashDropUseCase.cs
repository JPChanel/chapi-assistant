using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para eliminar un stash especifico sin aplicarlo.
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
    /// Elimina un stash especifico.
    /// </summary>
    /// <param name="projectPath">Ruta del proyecto</param>
    /// <param name="stashIndex"> ndice del stash (obligaorio)</param>
    public async Task<Result> ExecuteAsync(string projectPath, int stashIndex)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return Result.Fail("La ruta del proyecto no puede estar vacia");

        try
        {
            _notificationService.ShowInfo($"Eliminando stash@{stashIndex}...");

            var result = await _gitRepo.ExecuteGitCommandAsync(projectPath, $"stash drop stash@{{{stashIndex}}}");

            if (result.Contains("Dropped"))
            {
                _notificationService.ShowSuccess($"âœ… Stash@{stashIndex} eliminado correctamente");
                return Result.Success();
            }

            return Result.Fail($"Error al eliminar stash: {result}");
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"âŒ Error al eliminar stash: {ex.Message}");
            return Result.Fail(ex.Message);
        }
    }
}

