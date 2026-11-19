using Chapi.Application.Interfaces.Workspace;
using Chapi.Domain.Common;
using Chapi.Domain.Entities.Workspace;
using Chapi.Domain.Interfaces;
using Chapi.Infrastructure.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Chapi.Infrastructure.Services;

public class WorkspaceService : IWorkspaceService
{
    private readonly string _appDataPath;
    private readonly string _tipsCachePath;
    private readonly IServiceProvider _serviceProvider;
    private readonly IGitRepository _gitRepository;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(2);
    private DailyTipsCache? _cachedTips;
    private readonly SemaphoreSlim _tipsLock = new(1, 1);
    private static int _fallbackIndex = -1;
    private static readonly List<string> FallbackTips = new()
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

    private class DailyTipsCache
    {
        public DateTime? Date { get; set; }
        public DateTime LastFetchedUtc { get; set; } = DateTime.MinValue;
        public List<string> Tips { get; set; } = new();
        public int CurrentIndex { get; set; } = -1;
    }

    public WorkspaceService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _gitRepository = serviceProvider.GetRequiredService<IGitRepository>();
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

    public async Task<Result<IReadOnlyList<WorkspaceActivityRecord>>> LoadActivityRecordsAsync()
    {
        try
        {
            if (!Directory.Exists(_appDataPath))
                return Result<IReadOnlyList<WorkspaceActivityRecord>>.Success(Array.Empty<WorkspaceActivityRecord>());

            var records = new List<WorkspaceActivityRecord>();
            var ownerCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var workspaceDirectories = Directory.GetDirectories(_appDataPath);

            foreach (var workspaceDirectory in workspaceDirectories)
            {
                var metadataPath = Path.Combine(workspaceDirectory, "metadata.json");
                var tasksPath = Path.Combine(workspaceDirectory, "tasks");
                if (!Directory.Exists(tasksPath))
                    continue;

                var workspaceData = await LoadWorkspaceMetadataAsync(metadataPath);
                var projectPath = workspaceData?.ProjectPath ?? string.Empty;
                var projectName = !string.IsNullOrWhiteSpace(projectPath) && Directory.Exists(projectPath)
                    ? new DirectoryInfo(projectPath).Name
                    : new DirectoryInfo(workspaceDirectory).Name;
                var owner = await ResolveOwnerAsync(projectPath, ownerCache);

                foreach (var taskFile in Directory.GetFiles(tasksPath, "*.json"))
                {
                    try
                    {
                        var taskJson = await File.ReadAllTextAsync(taskFile);
                        var task = JsonSerializer.Deserialize<WorkspaceTask>(taskJson);
                        if (task == null || task.IsDeleted || task.ShouldBePermanentlyDeleted)
                            continue;

                        var updatedAt = task.UpdatedAt == default
                            ? (task.CreatedAt == default ? DateTime.Now : task.CreatedAt)
                            : task.UpdatedAt;

                        records.Add(new WorkspaceActivityRecord
                        {
                            TaskId = task.Id,
                            ProjectPath = projectPath,
                            ProjectName = projectName,
                            Title = task.Title ?? string.Empty,
                            Owner = owner,
                            Priority = task.Priority,
                            Status = task.Status,
                            CreatedAt = task.CreatedAt == default ? updatedAt : task.CreatedAt,
                            UpdatedAt = updatedAt,
                            CompletedAt = task.CompletedAt
                        });
                    }
                    catch
                    {
                    }
                }
            }

            return Result<IReadOnlyList<WorkspaceActivityRecord>>.Success(
                records
                    .OrderByDescending(record => record.UpdatedAt)
                    .ThenBy(record => record.ProjectName)
                    .ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<WorkspaceActivityRecord>>.Fail($"Error al cargar actividades globales: {ex.Message}");
        }
    }

    public async Task<Result<string>> GetRandomQuoteAsync()
    {
        await _tipsLock.WaitAsync();
        try
        {
            if (_cachedTips == null)
            {
                _cachedTips = await LoadTipsCacheAsync();
            }

            var now = DateTime.UtcNow;
            bool shouldRefreshWithAi = _cachedTips == null
                || !_cachedTips.Tips.Any()
                || (now - _cachedTips.LastFetchedUtc) >= RefreshInterval;

            // 1. Si pasaron 2 horas (o no hay tips en caché), refrescar con la IA
            if (shouldRefreshWithAi)
            {
                var newTips = await FetchTipsFromAiAsync();
                if (newTips != null && newTips.Any())
                {
                    _cachedTips = new DailyTipsCache
                    {
                        LastFetchedUtc = now,
                        Tips = newTips,
                        CurrentIndex = 0
                    };
                    await SaveTipsCacheAsync(_cachedTips);
                    return Result<string>.Success(_cachedTips.Tips[0]);
                }
            }

            // 2. En el lapso de 2 horas (o fallback si la IA falló): alternar entre los 10 tips traídos anteriormente
            if (_cachedTips != null && _cachedTips.Tips.Any())
            {
                _cachedTips.CurrentIndex = (_cachedTips.CurrentIndex + 1) % _cachedTips.Tips.Count;
                var selectedTip = _cachedTips.Tips[_cachedTips.CurrentIndex];
                await SaveTipsCacheAsync(_cachedTips);
                return Result<string>.Success(selectedTip);
            }

            // 3. Fallback en caso de no tener caché ni conexión
            return Result<string>.Success(GetNextFallbackTip());
        }
        catch
        {
            return Result<string>.Success(GetNextFallbackTip());
        }
        finally
        {
            _tipsLock.Release();
        }
    }

    private static string GetNextFallbackTip()
    {
        _fallbackIndex = (_fallbackIndex + 1) % FallbackTips.Count;
        return FallbackTips[_fallbackIndex];
    }

    private async Task<List<string>?> FetchTipsFromAiAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var prompt = "Genera un JSON array de strings con 10 consejos cortos (máx 15 palabras), ingeniosos, modernos y útiles sobre desarrollo de software, clean code, arquitectura o vida dev. En español. Solo el JSON array puro.";

            using var scope = _serviceProvider.CreateScope();
            var chatClient = scope.ServiceProvider.GetService<IChatClient>();
            if (chatClient == null) return null;

            var messages = new[] { new ChatMessage(ChatRole.User, prompt) };
            var response = await chatClient.GetResponseAsync(messages, cancellationToken: cts.Token);
            var responseText = response.Messages.FirstOrDefault()?.Text;

            if (string.IsNullOrWhiteSpace(responseText)) return null;

            responseText = responseText.Replace("```json", "").Replace("```", "").Trim();

            var startIndex = responseText.IndexOf('[');
            var endIndex = responseText.LastIndexOf(']');
            if (startIndex >= 0 && endIndex > startIndex)
            {
                responseText = responseText.Substring(startIndex, endIndex - startIndex + 1);
            }

            var tips = JsonSerializer.Deserialize<List<string>>(responseText);
            if (tips != null && tips.Any())
            {
                return tips.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            }
        }
        catch
        {
            // Falla silenciosa si no hay internet o timeout
        }

        return null;
    }

    private async Task<DailyTipsCache?> LoadTipsCacheAsync()
    {
        try
        {
            if (File.Exists(_tipsCachePath))
            {
                var json = await File.ReadAllTextAsync(_tipsCachePath);
                var cache = JsonSerializer.Deserialize<DailyTipsCache>(json);
                if (cache != null)
                {
                    if (cache.LastFetchedUtc == DateTime.MinValue && cache.Date.HasValue)
                    {
                        cache.LastFetchedUtc = cache.Date.Value.ToUniversalTime();
                    }
                    return cache;
                }
            }
        }
        catch { }

        return null;
    }

    private async Task SaveTipsCacheAsync(DailyTipsCache cache)
    {
        try
        {
            var dir = Path.GetDirectoryName(_tipsCachePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_tipsCachePath, json);
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

    private static async Task<WorkspaceData?> LoadWorkspaceMetadataAsync(string metadataPath)
    {
        if (!File.Exists(metadataPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath);
            return JsonSerializer.Deserialize<WorkspaceData>(json);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> ResolveOwnerAsync(string projectPath, IDictionary<string, string> ownerCache)
    {
        var cacheKey = string.IsNullOrWhiteSpace(projectPath) ? "__global__" : projectPath;
        if (ownerCache.TryGetValue(cacheKey, out var cachedOwner))
            return cachedOwner;

        var owner = string.Empty;

        if (!string.IsNullOrWhiteSpace(projectPath) && Directory.Exists(projectPath))
        {
            owner = await _gitRepository.GetConfigAsync(projectPath, "user.name");
        }

        if (string.IsNullOrWhiteSpace(owner))
        {
            owner = await _gitRepository.GetConfigAsync(string.Empty, "user.name", isGlobal: true);
        }

        owner = string.IsNullOrWhiteSpace(owner) ? "(Sin usuario Git)" : owner.Trim();
        ownerCache[cacheKey] = owner;
        return owner;
    }
}
