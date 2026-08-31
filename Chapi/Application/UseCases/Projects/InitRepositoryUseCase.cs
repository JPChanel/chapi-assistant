using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;
using System.IO;

namespace Chapi.Application.UseCases.Projects;

public record InitRepositoryRequest(
    string ProjectPath,
    string? DefaultBranch = null,
    string? RemoteUrl = null,
    bool CreateReadme = false,
    bool CreateGitIgnore = false
);

public class InitRepositoryUseCase
{
    private readonly IGitRepository _gitRepository;
    private readonly IProjectRepository _projectRepository;

    public InitRepositoryUseCase(
        IGitRepository gitRepository,
        IProjectRepository projectRepository)
    {
        _gitRepository = gitRepository;
        _projectRepository = projectRepository;
    }

    public async Task<Result<string>> ExecuteAsync(InitRepositoryRequest request, Action<string>? onProgress = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ProjectPath))
                return Result<string>.Fail("La ruta del proyecto no puede estar vacía.");

            if (!Directory.Exists(request.ProjectPath))
            {
                Directory.CreateDirectory(request.ProjectPath);
            }

            // 1. Determinar rama por defecto (desde solicitud o desde configuración global de Git)
            var defaultBranch = !string.IsNullOrWhiteSpace(request.DefaultBranch)
                ? request.DefaultBranch.Trim()
                : await _gitRepository.GetDefaultBranchAsync();

            if (string.IsNullOrWhiteSpace(defaultBranch))
            {
                defaultBranch = "main";
            }

            // 2. Inicializar repositorio Git
            onProgress?.Invoke($"Inicializando repositorio Git en rama '{defaultBranch}'...");
            var initResult = await _gitRepository.InitAsync(request.ProjectPath, defaultBranch);
            if (!initResult.IsSuccess)
            {
                return Result<string>.Fail($"Error al inicializar Git: {initResult.Error}");
            }

            // 3. Crear archivos opcionales iniciales
            var createdFiles = new List<string>();

            if (request.CreateGitIgnore)
            {
                var gitignorePath = Path.Combine(request.ProjectPath, ".gitignore");
                if (!File.Exists(gitignorePath))
                {
                    onProgress?.Invoke("Generando archivo .gitignore...");
                    var gitignoreContent = GetStandardGitIgnoreContent();
                    await File.WriteAllTextAsync(gitignorePath, gitignoreContent);
                    createdFiles.Add(".gitignore");
                }
            }

            if (request.CreateReadme)
            {
                var readmePath = Path.Combine(request.ProjectPath, "README.md");
                if (!File.Exists(readmePath))
                {
                    onProgress?.Invoke("Generando archivo README.md...");
                    var projectName = new DirectoryInfo(request.ProjectPath).Name;
                    var readmeContent = $"# {projectName}\n\nProyecto inicializado con Chapi Assistant.\n";
                    await File.WriteAllTextAsync(readmePath, readmeContent);
                    createdFiles.Add("README.md");
                }
            }

            // 4. Si se crearon archivos, hacer commit inicial
            if (createdFiles.Count > 0)
            {
                onProgress?.Invoke("Creando commit inicial...");
                await _gitRepository.StageFilesAsync(request.ProjectPath, createdFiles);
                await _gitRepository.CommitAsync(request.ProjectPath, "Initial commit", createdFiles);
            }

            // 5. Asociar remoto si se proporcionó
            if (!string.IsNullOrWhiteSpace(request.RemoteUrl))
            {
                onProgress?.Invoke("Asociando repositorio remoto 'origin'...");
                await _gitRepository.AddRemoteAsync(request.ProjectPath, "origin", request.RemoteUrl.Trim());
            }

            // 6. Registrar proyecto en Chapi
            onProgress?.Invoke("Registrando proyecto en Chapi...");
            await _projectRepository.AddProjectAsync(request.ProjectPath);

            return Result<string>.Success(request.ProjectPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Error al crear/inicializar repositorio: {ex.Message}");
        }
    }

    private static string GetStandardGitIgnoreContent()
    {
        return @"## Visual Studio & .NET
[Bb]in/
[Oo]bj/
[Ll]og/
[Ll]ogs/
.vs/
*.user
*.suo
*.userosscache
*.sln.docstates
.vscode/

## Node / Frontend
node_modules/
dist/
build/
.env
.env.local

## OS Files
.DS_Store
Thumbs.db
";
    }
}
