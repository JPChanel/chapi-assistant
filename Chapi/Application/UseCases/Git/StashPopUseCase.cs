using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para aplicar cambios del stash.
/// </summary>
public class StashPopUseCase
{
    private readonly IGitRepository _gitRepo;
    private readonly INotificationService _notificationService;

    public StashPopUseCase(IGitRepository gitRepo, INotificationService notificationService)
    {
        _gitRepo = gitRepo;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Aplica y elimina el ultimo stash (o uno especifico).
    /// </summary>
    /// <param name="projectPath">Ruta del proyecto</param>
    /// <param name="stashIndex"> ndice del stash (opcional, null = ultimo)</param>
    public async Task<Result> ExecuteAsync(string projectPath, int? stashIndex = null)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return Result.Fail("La ruta del proyecto no puede estar vacia");

        try
        {
            _notificationService.ShowInfo("Aplicando cambios del stash...");

            var result = await _gitRepo.StashPopAsync(projectPath, stashIndex);

            if (!result.IsSuccess)
            {
                if (result.Error.Contains("conflict", StringComparison.OrdinalIgnoreCase))
                {
                    _notificationService.ShowWarning("⚠️ Conflictos detectados al aplicar stash");
                    return Result.Fail("Conflictos detectados. Resuelve los conflictos manualmente.");
                }
                return result;
            }

            _notificationService.ShowSuccess("✅ Stash aplicado correctamente");
            return Result.Success();
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"âŒ Error al aplicar stash: {ex.Message}");
            return Result.Fail(ex.Message);
        }
    }
}

