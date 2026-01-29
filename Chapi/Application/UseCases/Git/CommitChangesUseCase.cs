using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Domain.Interfaces;
using System.IO;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Request para hacer commit de cambios.
/// </summary>
public class CommitRequest
{
    public string ProjectPath { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public IEnumerable<string> Files { get; set; } = Enumerable.Empty<string>();
}

/// <summary>
/// Use Case para hacer commit de cambios.
/// Orquesta la logica de negocio para commits.
/// </summary>
public class CommitChangesUseCase
{
    private readonly IGitRepository _gitRepo;
    private readonly INotificationService _notifications;

    public CommitChangesUseCase(IGitRepository gitRepo, INotificationService notifications)
    {
        _gitRepo = gitRepo;
        _notifications = notifications;
    }

    public async Task<Result<GitCommit>> ExecuteAsync(CommitRequest request)
    {
        // 1. Validar
        var validation = Validate(request);
        if (!validation.IsSuccess)
        {
            _notifications.ShowWarning(validation.Error);
            return Result<GitCommit>.Fail(validation.Error);
        }

        // 2. Ejecutar commit
        var result = await _gitRepo.CommitAsync(request.ProjectPath, request.Message, request.Files);

        // 3. Notificar resultado
        if (result.IsSuccess)
        {
            _notifications.ShowSuccess($"âœ… Commit realizado: {request.Message}");
        }
        else
        {
            _notifications.ShowError($"âŒ Error al hacer commit: {result.Error}");
        }

        return result;
    }

    private Result Validate(CommitRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectPath))
            return Result.Fail("Ruta de proyecto invalida");

        if (!Directory.Exists(request.ProjectPath))
            return Result.Fail("El proyecto no existe");

        if (string.IsNullOrWhiteSpace(request.Message))
            return Result.Fail("Debes escribir un mensaje de commit");

        if (!request.Files.Any())
            return Result.Fail("No hay archivos seleccionados para hacer commit");

        return Result.Success();
    }
}

