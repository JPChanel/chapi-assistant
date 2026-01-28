using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;
using System.IO;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para hacer push de cambios al remoto.
/// </summary>
public class PushChangesUseCase
{
    private readonly IGitRepository _gitRepo;
    private readonly INotificationService _notifications;

    public PushChangesUseCase(IGitRepository gitRepo, INotificationService notifications)
    {
        _gitRepo = gitRepo;
        _notifications = notifications;
    }

    public async Task<Result> ExecuteAsync(string projectPath, string branch)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            _notifications.ShowWarning("Ruta de proyecto inválida");
            return Result.Fail("Ruta de proyecto inválida");
        }

        if (string.IsNullOrWhiteSpace(branch))
        {
            _notifications.ShowWarning("Nombre de rama inválido");
            return Result.Fail("Nombre de rama inválido");
        }

        var result = await _gitRepo.PushAsync(projectPath, branch);

        if (result.IsSuccess)
        {
            _notifications.ShowSuccess($"✅ Push exitoso a {branch}");
        }
        else
        {
            _notifications.ShowError($"❌ Error al hacer push: {result.Error}");
        }

        return result;
    }
}
