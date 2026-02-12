using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using Chapi.Application.Interfaces.Workspace;
using Chapi.Domain.Common;
using Chapi.Domain.Entities.Workspace;
using System.Collections.Generic;
using Chapi.Infrastructure.AI;

namespace Chapi.Infrastructure.Services;

public class WorkspaceService : IWorkspaceService
{
    private readonly string _appDataPath;
    private readonly string _tipsCachePath;

    private class DailyTipsCache
    {
        public DateTime Date { get; set; }
        public List<string> Tips { get; set; } = new();
    }

    public WorkspaceService()
    {
        _appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChapiAssistant", "Workspaces");
        _tipsCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChapiAssistant", "daily_tips.json");
        
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
                    catch { }
                }
            }
            
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
            var metadata = new WorkspaceData 
            {
                ProjectPath = data.ProjectPath,
                SessionNotes = data.SessionNotes,
                DeploymentQueue = data.DeploymentQueue,
                LastUpdated = DateTime.Now
            };
   
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
                        catch { }
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
        // 1. Try to load from cache
        try
        {
            if (File.Exists(_tipsCachePath))
            {
                var json = await File.ReadAllTextAsync(_tipsCachePath);
                var cache = JsonSerializer.Deserialize<DailyTipsCache>(json);

                if (cache != null && cache.Date.Date == DateTime.Today && cache.Tips.Any())
                {
                    var random = new Random();
                    return Result<string>.Success(cache.Tips[random.Next(cache.Tips.Count)]);
                }
            }
        }
        catch { /* Ignore */ }

        // 2. Fallback
        var fallbackTips = new List<string>
        {
            "La vida tiene su propio backend: son esas conexiones ocultas que hacen que todo funcione.",
            "El código limpio es como un buen chiste: si tienes que explicarlo, es malo.",
            "Hay dos formas de escribir programas sin errores; solo la tercera funciona.",
            "Primero resuelve el problema. Luego, escribe el código.",
            "La simplicidad es el alma de la eficiencia.",
            "No documentes el problema, soluciónalo.",
            "Refactorizar es como limpiar tu habitación: nadie quiere hacerlo, pero se siente genial.",
            "Un buen programador mira a ambos lados antes de cruzar una calle de sentido único."
        };

        // 3. Trigger background refresh
        _ = Task.Run(() => RefreshDailyTipsAsync());

        var rnd = new Random();
        return Result<string>.Success(fallbackTips[rnd.Next(fallbackTips.Count)]);
    }

    private async Task RefreshDailyTipsAsync()
    {
        try
        {
            // Simple check to avoid spamming if cache is fresh enough
            if (File.Exists(_tipsCachePath))
            {
                var lastWrite = File.GetLastWriteTime(_tipsCachePath);
                if (lastWrite.Date == DateTime.Today) return;
            }

            var prompt = "Genera un JSON array de strings con 10 consejos cortos (máx 15 palabras), ingeniosos, modernos y útiles sobre desarrollo de software, clean code, arquitectura o vida dev. En español. Solo el JSON array puro.";
            
            var response = await AIClient.SendPromptAsync(prompt);
            
            if (string.IsNullOrWhiteSpace(response)) return;

            // Clean up potentially wrapped JSON
            response = response.Replace("```json", "").Replace("```", "").Trim();
            
            var tips = JsonSerializer.Deserialize<List<string>>(response);
            
            if (tips != null && tips.Any())
            {
                var cache = new DailyTipsCache
                {
                    Date = DateTime.Today,
                    Tips = tips
                };
                
                var json = JsonSerializer.Serialize(cache);
                await File.WriteAllTextAsync(_tipsCachePath, json);
            }
        }
        catch { }
    }

    public Result OpenFileInExplorer(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return Result.Fail("Ruta de archivo vacía");

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
