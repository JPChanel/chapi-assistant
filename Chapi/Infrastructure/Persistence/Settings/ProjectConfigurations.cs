using System.IO;
using System.Text.Json;

namespace Chapi.Infrastructure.Persistence.Settings;

public class ProjectConfig
{
    public string ProjectPath { get; set; } = string.Empty;
    public DeploymentConfig Deployment { get; set; } = new();
}

public class DeploymentConfig
{
    public bool IsEnabled { get; set; }
    public string AppName { get; set; } = string.Empty; // Nombre del ejecutable/ID (ej: FirmaDigital)
    public string Author { get; set; } = string.Empty;  // Autor/Empresa (ej: ANC)
    public string Type { get; set; } = "Local"; // Local, FTP
    public string LocalPath { get; set; } = string.Empty;
    public string FtpUrl { get; set; } = string.Empty;
    public string FtpUser { get; set; } = string.Empty;
    public string FtpPassword { get; set; } = string.Empty; // TODO: Encrypt in future
    public string IconPath { get; set; } = string.Empty;
    public string SplashPath { get; set; } = string.Empty;
}

public static class ProjectConfigurations
{
    private static readonly string AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chapi");
    private static readonly string ConfigFilePath = Path.Combine(AppDataPath, "project_configs.json");

    static ProjectConfigurations()
    {
        Directory.CreateDirectory(AppDataPath);
    }

    public static Dictionary<string, ProjectConfig> LoadConfigs()
    {
        if (!File.Exists(ConfigFilePath))
        {
            return new Dictionary<string, ProjectConfig>();
        }

        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, ProjectConfig>>(json) ?? new Dictionary<string, ProjectConfig>();
        }
        catch
        {
            return new Dictionary<string, ProjectConfig>();
        }
    }

    public static void SaveConfig(string projectPath, ProjectConfig config)
    {
        var configs = LoadConfigs();
        configs[projectPath] = config;
        
        var json = JsonSerializer.Serialize(configs, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigFilePath, json);
    }

    public static ProjectConfig GetConfig(string projectPath)
    {
        var configs = LoadConfigs();
        if (configs.TryGetValue(projectPath, out var config))
        {
            return config;
        }
        return new ProjectConfig { ProjectPath = projectPath };
    }
}
