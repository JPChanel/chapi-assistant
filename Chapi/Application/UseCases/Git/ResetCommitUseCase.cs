using Chapi.Domain.Common;
using Chapi.Domain.Enums;
using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para deshacer el ultimo commit.
/// </summary>
public class ResetCommitUseCase
{
    private readonly IGitRepository _gitRepo;
    private readonly INotificationService _notificationService;

    public ResetCommitUseCase(IGitRepository gitRepo, INotificationService notificationService)
    {
        _gitRepo = gitRepo;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Deshace el ultimo commit.
    /// </summary>
    /// <param name="projectPath">Ruta del proyecto</param>
    /// <param name="mode">Modo de reset (Soft por defecto)</param>
    public async Task<Result> ExecuteAsync(string projectPath, ResetMode mode = ResetMode.Soft)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return Result.Fail("La ruta del proyecto no puede estar vacia");

        try
        {
            _notificationService.ShowInfo($"Deshaciendo ultimo commit ({mode})...");

            var result = await _gitRepo.ResetAsync(projectPath, "HEAD~1", mode);
            if (!result.IsSuccess) return result;

            string message = mode switch
            {
                ResetMode.Soft => "✅ Commit deshecho. Cambios en staging.",
                ResetMode.Mixed => "✅ Commit deshecho. Cambios en working directory.",
                ResetMode.Hard => "✅ Commit deshecho. Cambios descartados.",
                _ => "✅ Commit deshecho."
            };

            _notificationService.ShowSuccess(message);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"❌ Error al deshacer commit: {ex.Message}");
            return Result.Fail(ex.Message);
        }
    }
}

