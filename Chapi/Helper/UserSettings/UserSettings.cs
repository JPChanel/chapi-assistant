using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Chapi.Helper.UserSettings;

public static class UserSettingsService
{
    // Usa la misma carpeta base
    private static readonly string AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chapi");
    // Pero un archivo DIFERENTE
    private static readonly string SettingsFilePath = Path.Combine(AppDataPath, "user.api.settings.json");

    static UserSettingsService()
    {
        Directory.CreateDirectory(AppDataPath);
    }

    /// <summary>
    /// Carga las configuraciones de API del usuario.
    /// </summary>
    public static UserApiSettings LoadSettings()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new UserApiSettings(); // Devuelve uno vacío si no existe
        }

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<UserApiSettings>(json) ?? new UserApiSettings();
        }
        catch (Exception)
        {
            return new UserApiSettings(); // Devuelve uno vacío si hay error
        }
    }

    /// <summary>
    /// Guarda las configuraciones de API del usuario.
    /// </summary>
    public static void SaveSettings(UserApiSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFilePath, json);
    }
}
public class UserApiSettings
{
    public string GeminiApiKey { get; set; }

    public bool ProxyEnabled { get; set; } = false;
    public string ProxyUrl { get; set; }
    public string ProxyUser { get; set; }
    public string ProxyPass { get; set; }

}