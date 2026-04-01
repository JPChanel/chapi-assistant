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
    public string AppName { get; set; } = string.Empty; // Título visual de la app (ej: Firma Digital)
    public string PackageId { get; set; } = string.Empty; // ID único para Velopack/NuGet (ej: ANC.FirmaDigital)
    public string Author { get; set; } = string.Empty;  // Autor/Empresa (ej: ANC)
    public string Platform { get; set; } = string.Empty; // RuntimeIdentifier opcional (ej: win-x64)
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
        // 1. Guardar en el proyecto (Portabilidad entre máquinas via Git)
        try 
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(projectPath, "chapi.config.json"), json);
        }
        catch { }

        // 2. Guardar en el global para compatibilidad
        var configs = LoadConfigs();
        configs[projectPath] = config;
        
        var globalJson = JsonSerializer.Serialize(configs, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigFilePath, globalJson);
    }

    public static ProjectConfig GetConfig(string projectPath)
    {
        // 1. Intentar cargar desde el proyecto directo (Prioridad: Portabilidad)
        string projectLocalConfig = Path.Combine(projectPath, "chapi.config.json");
        if (File.Exists(projectLocalConfig))
        {
            try 
            {
                var json = File.ReadAllText(projectLocalConfig);
                var config = JsonSerializer.Deserialize<ProjectConfig>(json);
                if (config != null)
                {
                    config.ProjectPath = projectPath; // Asegurar ruta actual
                    return config;
                }
            }
            catch { }
        }

        // 2. Fallback al global habitual
        var configs = LoadConfigs();
        if (configs.TryGetValue(projectPath, out var configGlobal))
        {
            return configGlobal;
        }
        return new ProjectConfig { ProjectPath = projectPath };
    }
}
