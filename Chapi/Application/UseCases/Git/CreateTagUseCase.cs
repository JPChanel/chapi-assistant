using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Git;

/// <summary>
/// Use Case para crear una nueva etiqueta (Tag).
/// </summary>
public class CreateTagUseCase
{
    private readonly IGitRepository _gitRepo;
    private readonly INotificationService _notificationService;

    public CreateTagUseCase(IGitRepository gitRepo, INotificationService notificationService)
    {
        _gitRepo = gitRepo;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Crea una nueva etiqueta.
    /// </summary>
    /// <param name="projectPath">Ruta del proyecto</param>
    /// <param name="tagName">Nombre de la etiqueta</param>
    /// <param name="message">Mensaje de la etiqueta (anotada)</param>
    /// <param name="commitHash">Hash del commit (opcional, null = HEAD)</param>
    public async Task<Result> ExecuteAsync(string projectPath, string tagName, string message, string? commitHash = null)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return Result.Fail("La ruta del proyecto no puede estar vacia");

        if (string.IsNullOrWhiteSpace(tagName))
            return Result.Fail("El nombre de la etiqueta no puede estar vacio");

        try
        {
            _notificationService.ShowInfo($"Creando etiqueta '{tagName}'...");

            // Limpiar mensaje de comillas
            string safeMessage = message.Replace("\"", "'");
            
            string command = string.IsNullOrWhiteSpace(commitHash)
                ? $"tag -a \"{tagName}\" -m \"{safeMessage}\""
                : $"tag -a \"{tagName}\" -m \"{safeMessage}\" {commitHash}";

            var result = await _gitRepo.ExecuteGitCommandAsync(projectPath, command);

            if (result.Contains("already exists"))
            {
                _notificationService.ShowWarning($"âš ï¸ La etiqueta '{tagName}' ya existe");
                return Result.Fail($"La etiqueta '{tagName}' ya existe");
            }

            _notificationService.ShowSuccess($"âœ… Etiqueta '{tagName}' creada correctamente");
            return Result.Success();
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"âŒ Error al crear etiqueta: {ex.Message}");
            return Result.Fail(ex.Message);
        }
    }
}

