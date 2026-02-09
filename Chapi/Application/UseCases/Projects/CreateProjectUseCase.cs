using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;
using System.IO;

namespace Chapi.Application.UseCases.Projects;

public record CreateProjectRequest(
    string ProjectName,
    string ParentDirectory,
    string TemplateUrl,
    string RemoteUrl = null
);

public class CreateProjectUseCase
{
    private readonly IGitRepository _gitRepository;
    private readonly ITemplateService _templateService;
    private readonly IProjectRepository _projectRepository;

    public CreateProjectUseCase(
        IGitRepository gitRepository,
        ITemplateService templateService,
        IProjectRepository projectRepository)
    {
        _gitRepository = gitRepository;
        _templateService = templateService;
        _projectRepository = projectRepository;
    }

    public async Task<Result<string>> ExecuteAsync(CreateProjectRequest request, Action<string> onProgress = null)
    {
        try
        {
            string targetPath = Path.Combine(request.ParentDirectory, request.ProjectName);
            
            if (Directory.Exists(targetPath))
                return Result<string>.Fail($"El directorio ya existe: {targetPath}");

            // 1. Clonar repositorio base
            onProgress?.Invoke("Clonando repositorio base...");
            var cloneResult = await _gitRepository.CloneAsync(request.TemplateUrl, targetPath);
            if (!cloneResult.IsSuccess) return Result<string>.Fail(cloneResult.Error);

            // 2. Eliminar carpeta .git original
            onProgress?.Invoke("Limpiando metadatos de Git...");
            string gitPath = Path.Combine(targetPath, ".git");
            if (Directory.Exists(gitPath))
            {
                DeleteDirectory(gitPath);
            }

            // 3. Renombrar estructura
            onProgress?.Invoke("Personalizando estructura del proyecto...");
            string oldName = Path.GetFileNameWithoutExtension(request.TemplateUrl);
            var renameResult = await _templateService.RenameTemplateAsync(targetPath, oldName, request.ProjectName, onProgress);
            if (!renameResult.IsSuccess) return Result<string>.Fail(renameResult.Error);

            // 4. Inicializar nuevo repo Git
            onProgress?.Invoke("Inicializando nuevo repositorio Git...");
            var initResult = await _gitRepository.InitAsync(targetPath);
            if (!initResult.IsSuccess) return Result<string>.Fail(initResult.Error);

            // 5. Asociar remoto si se proporcionó
            if (!string.IsNullOrWhiteSpace(request.RemoteUrl))
            {
                onProgress?.Invoke("Asociando repositorio remoto...");
                await _gitRepository.AddRemoteAsync(targetPath, "origin", request.RemoteUrl);
            }

            // 6. Registrar proyecto
            onProgress?.Invoke("Registrando proyecto en Chapi...");
            await _projectRepository.AddProjectAsync(targetPath);

            return Result<string>.Success(targetPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Error fatal al crear proyecto: {ex.Message}");
        }
    }

    private void DeleteDirectory(string target_dir)
    {
        string[] files = Directory.GetFiles(target_dir);
        string[] dirs = Directory.GetDirectories(target_dir);

        foreach (string file in files)
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }

        foreach (string dir in dirs)
        {
            DeleteDirectory(dir);
        }

        Directory.Delete(target_dir, false);
    }
}
