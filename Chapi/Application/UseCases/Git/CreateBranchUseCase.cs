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
            return Result.Fail("La ruta del proyecto no puede estar vacia");

        if (string.IsNullOrWhiteSpace(branchName))
            return Result.Fail("El nombre de la rama no puede estar vacio");

        try
        {
            _notificationService.ShowInfo($"Creando rama '{branchName}'...");

            var result = await _gitRepo.CreateBranchAsync(projectPath, branchName, fromCommitOrBranch);

            if (!result.IsSuccess)
            {
                _notificationService.ShowError($"? Error al crear rama: {result.Error}");
                return result;
            }

            string message = string.IsNullOrWhiteSpace(fromCommitOrBranch)
                ? $"? Rama '{branchName}' creada desde HEAD"
                : $"? Rama '{branchName}' creada desde '{fromCommitOrBranch}'";

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

