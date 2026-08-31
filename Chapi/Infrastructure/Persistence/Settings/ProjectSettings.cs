using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chapi.Infrastructure.Persistence.Settings;

public class ProjectGroupConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }
}

public class ProjectsDataConfig
{
    [JsonPropertyName("projects")]
    public List<string> Projects { get; set; } = new();

    [JsonPropertyName("groups")]
    public List<ProjectGroupConfig> Groups { get; set; } = new();

    [JsonPropertyName("mappings")]
    public Dictionary<string, string> Mappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class ProjectSettings
{
    private static readonly string AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chapi");
    private static readonly string ProjectsFilePath = Path.Combine(AppDataPath, "projects.json");

    static ProjectSettings()
    {
        Directory.CreateDirectory(AppDataPath);
    }

    public static ProjectsDataConfig LoadData()
    {
        if (!File.Exists(ProjectsFilePath))
        {
            return new ProjectsDataConfig();
        }

        try
        {
            var json = File.ReadAllText(ProjectsFilePath).Trim();
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ProjectsDataConfig();
            }

            if (json.StartsWith("["))
            {
                var oldList = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                var data = new ProjectsDataConfig
                {
                    Projects = oldList
                };
                SaveData(data);
                return data;
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var config = JsonSerializer.Deserialize<ProjectsDataConfig>(json, options) ?? new ProjectsDataConfig();
            config.Projects ??= new List<string>();
            config.Groups ??= new List<ProjectGroupConfig>();
            config.Mappings = config.Mappings == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(config.Mappings, StringComparer.OrdinalIgnoreCase);

            return config;
        }
        catch
        {
            return new ProjectsDataConfig();
        }
    }

    public static void SaveData(ProjectsDataConfig data)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = JsonSerializer.Serialize(data, options);
        File.WriteAllText(ProjectsFilePath, json);
    }

    public static List<string> LoadProjects()
    {
        return LoadData().Projects;
    }

    public static void SaveProjects(List<string> projects)
    {
        var data = LoadData();
        data.Projects = projects;
        SaveData(data);
    }

    public static void AddProject(string projectPath, string? groupId = null)
    {
        var data = LoadData();
        if (!data.Projects.Contains(projectPath, StringComparer.OrdinalIgnoreCase))
        {
            data.Projects.Add(projectPath);
        }
        if (!string.IsNullOrWhiteSpace(groupId))
        {
            data.Mappings[projectPath] = groupId;
        }
        SaveData(data);
    }

    public static void RemoveProject(string projectPath)
    {
        var data = LoadData();
        data.Projects.RemoveAll(p => string.Equals(p, projectPath, StringComparison.OrdinalIgnoreCase));
        data.Mappings.Remove(projectPath);
        SaveData(data);
    }

    public static List<ProjectGroupConfig> GetGroups()
    {
        var data = LoadData();
        return data.Groups.OrderBy(g => g.Order).ToList();
    }

    public static ProjectGroupConfig AddGroup(string name)
    {
        var data = LoadData();
        var newGroup = new ProjectGroupConfig
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = name.Trim(),
            Order = data.Groups.Count
        };
        data.Groups.Add(newGroup);
        SaveData(data);
        return newGroup;
    }

    public static void UpdateGroup(string id, string newName)
    {
        var data = LoadData();
        var group = data.Groups.FirstOrDefault(g => g.Id == id);
        if (group != null)
        {
            group.Name = newName.Trim();
            SaveData(data);
        }
    }

    public static void DeleteGroup(string id)
    {
        var data = LoadData();
        data.Groups.RemoveAll(g => g.Id == id);
        // Remove mappings for this group so projects become "Sin Agrupar"
        var keysToRemove = data.Mappings.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList();
        foreach (var key in keysToRemove)
        {
            data.Mappings.Remove(key);
        }
        for (int i = 0; i < data.Groups.Count; i++)
        {
            data.Groups[i].Order = i;
        }
        SaveData(data);
    }

    public static void SetProjectGroup(string projectPath, string? groupId)
    {
        var data = LoadData();
        if (string.IsNullOrWhiteSpace(groupId))
        {
            data.Mappings.Remove(projectPath);
        }
        else
        {
            data.Mappings[projectPath] = groupId;
        }
        SaveData(data);
    }

    public static void MoveProject(string sourcePath, string targetPath, string? targetGroupId)
    {
        var data = LoadData();

        // 1. Asignar grupo
        if (string.IsNullOrWhiteSpace(targetGroupId))
        {
            data.Mappings.Remove(sourcePath);
        }
        else
        {
            data.Mappings[sourcePath] = targetGroupId;
        }

        // 2. Reordenar en la lista
        data.Projects.RemoveAll(p => string.Equals(p, sourcePath, StringComparison.OrdinalIgnoreCase));
        var targetIndex = data.Projects.FindIndex(p => string.Equals(p, targetPath, StringComparison.OrdinalIgnoreCase));
        if (targetIndex >= 0)
        {
            data.Projects.Insert(targetIndex, sourcePath);
        }
        else
        {
            data.Projects.Add(sourcePath);
        }

        SaveData(data);
    }
}
