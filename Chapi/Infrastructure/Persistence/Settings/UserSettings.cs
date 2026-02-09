using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Chapi.Infrastructure.Persistence.Settings;

public static class UserSettingsService
{
    private static readonly string AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chapi");
    private static readonly string SettingsFilePath = Path.Combine(AppDataPath, "user.api.settings.json");

    static UserSettingsService()
    {
        Directory.CreateDirectory(AppDataPath);
    }

    public static UserApiSettings LoadSettings()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new UserApiSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<UserApiSettings>(json) ?? new UserApiSettings();
        }
        catch (Exception)
        {
            return new UserApiSettings();
        }
    }

    public static void SaveSettings(UserApiSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFilePath, json);
    }
}

public class UserApiSettings
{
    public string GeminiApiKey { get; set; } = string.Empty;

    public bool ProxyEnabled { get; set; } = false;
    public string ProxyUrl { get; set; } = string.Empty;
    public string ProxyUser { get; set; } = string.Empty;
    public string ProxyPass { get; set; } = string.Empty;

    // GitHub Auth
    public string GitHubToken { get; set; } = string.Empty;
    public string GitHubUserLogin { get; set; } = string.Empty;
    public string GitHubUserName { get; set; } = string.Empty;
    public string GitHubUserAvatar { get; set; } = string.Empty;
}
