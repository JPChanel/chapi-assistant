using System.IO;
using System.Text.Json;

namespace Chapi.Helper.UserSettings;

public static class ProjectSettings
{
    private static readonly string AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chapi");
    private static readonly string ProjectsFilePath = Path.Combine(AppDataPath, "projects.json");

    static ProjectSettings()
    {
        Directory.CreateDirectory(AppDataPath);
    }

    public static List<string> LoadProjects()
    {
        if (!File.Exists(ProjectsFilePath))
        {
            return new List<string>();
        }

        var json = File.ReadAllText(ProjectsFilePath);
        return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
    }

    public static void SaveProjects(List<string> projects)
    {
        var json = JsonSerializer.Serialize(projects, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ProjectsFilePath, json);
    }

    public static void AddProject(string projectPath)
    {
        var projects = LoadProjects();
        if (!projects.Contains(projectPath))
        {
            projects.Add(projectPath);
            SaveProjects(projects);
        }
    }
    public static void RemoveProject(string projectPath)
    {
        var projects = LoadProjects();
        if (projects.Contains(projectPath))
        {
            projects.Remove(projectPath);
            SaveProjects(projects); // Reutilizamos tu método de guardado
        }
    }
}
