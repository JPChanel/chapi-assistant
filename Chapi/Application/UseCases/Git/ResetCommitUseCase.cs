using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Modo de reset para el commit.
/// </summary>
public enum ResetMode
{
    /// <summary>
    /// Mantiene los cambios en el area de staging (--soft)
    /// </summary>
    Soft,
    
    /// <summary>
    /// Mantiene los cambios en el working directory (--mixed)
    /// </summary>
    Mixed,
    
    /// <summary>
    /// Descarta todos los cambios (--hard)
    /// </summary>
    Hard
}

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
            string modeStr = mode switch
            {
                ResetMode.Soft => "--soft",
                ResetMode.Mixed => "--mixed",
                ResetMode.Hard => "--hard",
                _ => "--soft"
            };

            _notificationService.ShowInfo($"Deshaciendo ultimo commit ({mode})...");

            var result = await _gitRepo.ExecuteGitCommandAsync(projectPath, $"reset {modeStr} HEAD~1");

            string message = mode switch
            {
                ResetMode.Soft => "âœ… Commit deshecho. Cambios en staging.",
                ResetMode.Mixed => "âœ… Commit deshecho. Cambios en working directory.",
                ResetMode.Hard => "âœ… Commit deshecho. Cambios descartados.",
                _ => "âœ… Commit deshecho."
            };

            _notificationService.ShowSuccess(message);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"âŒ Error al deshacer commit: {ex.Message}");
            return Result.Fail(ex.Message);
        }
    }
}

