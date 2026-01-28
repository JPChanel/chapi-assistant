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
    /// Aplica y elimina el último stash (o uno específico).
    /// </summary>
    /// <param name="projectPath">Ruta del proyecto</param>
    /// <param name="stashIndex">Índice del stash (opcional, null = último)</param>
    public async Task<Result> ExecuteAsync(string projectPath, int? stashIndex = null)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return Result.Fail("La ruta del proyecto no puede estar vacía");

        try
        {
            _notificationService.ShowInfo("Aplicando cambios del stash...");

            string command = stashIndex.HasValue
                ? $"stash pop stash@{{{stashIndex.Value}}}"
                : "stash pop";

            var result = await _gitRepo.ExecuteGitCommandAsync(projectPath, command);

            if (result.Contains("CONFLICT"))
            {
                _notificationService.ShowWarning("⚠️ Conflictos detectados al aplicar stash");
                return Result.Fail("Conflictos detectados. Resuelve los conflictos manualmente.");
            }

            if (result.Contains("Dropped") || result.Contains("Applied"))
            {
                _notificationService.ShowSuccess("✅ Stash aplicado correctamente");
                return Result.Success();
            }

            return Result.Fail($"Error al aplicar stash: {result}");
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"❌ Error al aplicar stash: {ex.Message}");
            return Result.Fail(ex.Message);
        }
    }
}
