using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para crear una nueva rama.
/// </summary>
public class CreateBranchUseCase
{
    private readonly IGitRepository _gitRepo;
    private readonly INotificationService _notificationService;

    public CreateBranchUseCase(IGitRepository gitRepo, INotificationService notificationService)
    {
        _gitRepo = gitRepo;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Crea una nueva rama.
    /// </summary>
    /// <param name="projectPath">Ruta del proyecto</param>
    /// <param name="branchName">Nombre de la nueva rama</param>
    /// <param name="fromCommitOrBranch">Commit o rama desde donde crear (opcional, null = HEAD)</param>
    public async Task<Result> ExecuteAsync(string projectPath, string branchName, string? fromCommitOrBranch = null)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return Result.Fail("La ruta del proyecto no puede estar vacía");

        if (string.IsNullOrWhiteSpace(branchName))
            return Result.Fail("El nombre de la rama no puede estar vacío");

        try
        {
            _notificationService.ShowInfo($"Creando rama '{branchName}'...");

            string command = string.IsNullOrWhiteSpace(fromCommitOrBranch)
                ? $"branch {branchName}"
                : $"branch {branchName} {fromCommitOrBranch}";

            var result = await _gitRepo.ExecuteGitCommandAsync(projectPath, command);

            if (result.Contains("already exists"))
            {
                _notificationService.ShowWarning($"⚠️ La rama '{branchName}' ya existe");
                return Result.Fail($"La rama '{branchName}' ya existe");
            }

            string message = string.IsNullOrWhiteSpace(fromCommitOrBranch)
                ? $"✅ Rama '{branchName}' creada desde HEAD"
                : $"✅ Rama '{branchName}' creada desde '{fromCommitOrBranch}'";

            _notificationService.ShowSuccess(message);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"❌ Error al crear rama: {ex.Message}");
            return Result.Fail(ex.Message);
        }
    }
}
