using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;
using System.IO;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para hacer fetch del remoto.
/// </summary>
public class FetchChangesUseCase
{
    private readonly IGitRepository _gitRepo;
    private readonly INotificationService _notifications;

    public FetchChangesUseCase(IGitRepository gitRepo, INotificationService notifications)
    {
        _gitRepo = gitRepo;
        _notifications = notifications;
    }

    public async Task<Result> ExecuteAsync(string projectPath, bool isSilent = false)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            if (!isSilent)
                _notifications.ShowWarning("Ruta de proyecto inválida");
            return Result.Fail("Ruta de proyecto inválida");
        }

        var result = await _gitRepo.FetchAsync(projectPath);

        if (result.IsSuccess && !isSilent)
        {
            _notifications.ShowSuccess("✅ Fetch completado");
        }
        else if (!result.IsSuccess && !isSilent)
        {
            _notifications.ShowError($"❌ Error al hacer fetch: {result.Error}");
        }

        return result;
    }
}
