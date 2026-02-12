using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using Chapi.Application.Interfaces.Workspace;
using Chapi.Domain.Common;
using Chapi.Domain.Entities.Workspace;
using System.Collections.Generic;

namespace Chapi.Infrastructure.Services;

public class WorkspaceService : IWorkspaceService
{
    private readonly string _appDataPath;

    public WorkspaceService()
    {
        _appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChapiAssistant", "Workspaces");
        if (!Directory.Exists(_appDataPath))
        {
            Directory.CreateDirectory(_appDataPath);
        }
    }

    private string GetProjectStoragePath(string projectPath)
    {
        // Use MD5 of the project path to create a unique folder name
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = BitConverter.ToString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(projectPath))).Replace("-", "");
        
        // Get project folder name for readability and append it
        var projectName = new DirectoryInfo(projectPath).Name;
        foreach (var c in Path.GetInvalidFileNameChars())
            projectName = projectName.Replace(c, '_');

        var folderName = $"{hash}_{projectName}";

        // Structure: AppData/ChapiAssistant/Workspaces/[HASH]_[ProjectName]/
        var projectStoragePath = Path.Combine(_appDataPath, folderName);
        
        if (!Directory.Exists(projectStoragePath))
        {
            Directory.CreateDirectory(projectStoragePath);
        }

        return projectStoragePath;
    }

    public async Task<Result<WorkspaceData>> LoadWorkspaceAsync(string projectPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return Result<WorkspaceData>.Fail("Ruta de proyecto inválida");

            var storagePath = GetProjectStoragePath(projectPath);
            var metadataPath = Path.Combine(storagePath, "metadata.json");
            var tasksPath = Path.Combine(storagePath, "tasks");

            var data = new WorkspaceData { ProjectPath = projectPath };

            // 1. Load Metadata (Notes, Queue)
            if (File.Exists(metadataPath))
            {
                var json = await File.ReadAllTextAsync(metadataPath);
                var loadedData = JsonSerializer.Deserialize<WorkspaceData>(json);
                if (loadedData != null)
                {
                    data.SessionNotes = loadedData.SessionNotes;
                    data.DeploymentQueue = loadedData.DeploymentQueue ?? new List<DeploymentAsset>();
                    data.LastUpdated = loadedData.LastUpdated;
                }
            }

            // 2. Load Individual Tasks
            if (Directory.Exists(tasksPath))
            {
                var taskFiles = Directory.GetFiles(tasksPath, "*.json");
                foreach (var file in taskFiles)
                {
                    try
                    {
                        var taskJson = await File.ReadAllTextAsync(file);
                        var task = JsonSerializer.Deserialize<WorkspaceTask>(taskJson);
                        if (task != null)
                        {
                            data.Tasks.Add(task);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[WorkspaceService] Corrupt task file found: {file}. Error: {ex.Message}");
                        // Optionally rename .bad to avoid reloading loop?
                        // File.Move(file, file + ".corrupt");
                    }
                }
            }
            
            // Legacy Migration (Optional: Load old single file if new structure doesn't exist?)
            // For now, let's assume we are starting fresh or migrating. 
            // If the old file exists in the old path, we could load it.
            // Old path was: Path.Combine(_appDataPath, $"{hash}_{FileName}");
            // Let's check it.
            /*
            var oldPath = Path.Combine(_appDataPath, $"{Path.GetFileName(storagePath)}_{FileName}");
            if (!Directory.Exists(tasksPath) && File.Exists(oldPath)) {
                 // Load old logic...
            }
            */
            
            return Result<WorkspaceData>.Success(data);
        }
        catch (Exception ex)
        {
            return Result<WorkspaceData>.Fail($"Error al cargar workspace: {ex.Message}");
        }
    }

    public async Task<Result> SaveWorkspaceAsync(WorkspaceData data)
    {
        try
        {
            if (data == null || string.IsNullOrWhiteSpace(data.ProjectPath))
                return Result.Fail("Datos de workspace inválidos");

            var storagePath = GetProjectStoragePath(data.ProjectPath);
            var metadataPath = Path.Combine(storagePath, "metadata.json");
            var tasksPath = Path.Combine(storagePath, "tasks");

            if (!Directory.Exists(tasksPath))
                Directory.CreateDirectory(tasksPath);

            // 1. Save Metadata (Exclude tasks to keep it small)
            // We need a temporary object or ignore Tasks property. 
            // WorkspaceData has Tasks. We can just create a new instance with same props but empty tasks.
            var metadata = new WorkspaceData 
            {
                ProjectPath = data.ProjectPath,
                SessionNotes = data.SessionNotes,
                DeploymentQueue = data.DeploymentQueue,
                LastUpdated = DateTime.Now
            };
            // Note: Tasks are empty in 'metadata' object, so serialized JSON won't have them (or empty).

            var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(metadataPath, metadataJson);

            // 2. Save Tasks (Individual Files)
            var currentTaskIds = new HashSet<Guid>();
            foreach (var task in data.Tasks)
            {
                if (task.Id == Guid.Empty) task.Id = Guid.NewGuid(); // Ensure ID
                currentTaskIds.Add(task.Id);

                var taskFilePath = Path.Combine(tasksPath, $"{task.Id}.json");
                var taskJson = JsonSerializer.Serialize(task, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(taskFilePath, taskJson);
            }

            // 3. Clean up deleted tasks
            var existingFiles = Directory.GetFiles(tasksPath, "*.json");
            foreach (var file in existingFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (Guid.TryParse(fileName, out var fileId))
                {
                    if (!currentTaskIds.Contains(fileId))
                    {
                        try 
                        {
                            File.Delete(file); 
                        }
                        catch (Exception ex) 
                        {
                            Debug.WriteLine($"[WorkspaceService] Failed to delete orphaned task: {ex.Message}");
                        }
                    }
                }
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error al guardar workspace: {ex.Message}");
        }
    }

    public async Task<Result<string>> GetRandomQuoteAsync()
    {
        // Simulating async for future API call
        await Task.Yield();
        
        var quotes = new List<string>
        {
            "La vida tiene su propio backend: son esas conexiones ocultas que hacen que todo funcione sin que lo notes.",
            "El código limpio es como un buen chiste: si tienes que explicarlo, es malo.",
            "Hay dos formas de escribir programas sin errores; solo la tercera funciona.",
            "Primero resuelve el problema. Luego, escribe el código.",
            "La simplicidad es el alma de la eficiencia.",
            "No documentes el problema, soluciónalo.",
            "Si no está probado, está roto.",
            "Refactorizar es como limpiar tu habitación: nadie quiere hacerlo, pero se siente genial cuando terminas.",
            "Los bugs son solo features no documentadas... o eso dicen.",
            "Un buen programador es alguien que mira a ambos lados antes de cruzar una calle de sentido único."
        };

        var random = new Random();
        return Result<string>.Success(quotes[random.Next(quotes.Count)]);
    }

    public Result OpenFileInExplorer(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return Result.Fail("Ruta de archivo vacía");

            // Check if file exists, if not check directory
            string argument = "/select, \"" + filePath + "\"";
            
            Process.Start("explorer.exe", argument);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail($"No se pudo abrir el explorador: {ex.Message}");
        }
    }
}
