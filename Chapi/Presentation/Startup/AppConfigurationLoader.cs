using Microsoft.Extensions.Configuration;
using System.Collections;
using System.IO;

namespace Chapi.Startup;

public static class AppConfigurationLoader
{
    public static IConfiguration Load(string appSettingsFileName)
    {
        var (bundledConfigPath, appDataConfigPath) = EnsureAppSettingsFiles(appSettingsFileName);

        return new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(bundledConfigPath)!)
            // El archivo empaquetado aporta defaults y nuevas secciones.
            .AddJsonFile(Path.GetFileName(bundledConfigPath), optional: false, reloadOnChange: true)
            // AppData conserva overrides del usuario sin perder claves nuevas.
            .AddJsonFile(appDataConfigPath, optional: true, reloadOnChange: true)
            // Permite sobreescribir secretos via variables de entorno (prefijo CHAPI_, ej. CHAPI_GitHub__ClientSecret).
            .AddInMemoryCollection(LoadEnvironmentOverrides("CHAPI_"))
            .Build();
    }

    private static Dictionary<string, string?> LoadEnvironmentOverrides(string prefix)
    {
        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string rawKey ||
                !rawKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalizedKey = rawKey[prefix.Length..].Replace("__", ":");
            if (!string.IsNullOrWhiteSpace(normalizedKey))
            {
                overrides[normalizedKey] = entry.Value?.ToString();
            }
        }

        return overrides;
    }

    private static (string bundledConfigPath, string appDataConfigPath) EnsureAppSettingsFiles(string appSettingsFileName)
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Chapi");

        Directory.CreateDirectory(appDataDirectory);

        var appDataConfigPath = Path.Combine(appDataDirectory, appSettingsFileName);
        var bundledConfigPath = Path.Combine(AppContext.BaseDirectory, appSettingsFileName);

        if (!File.Exists(bundledConfigPath))
        {
            throw new FileNotFoundException(
                $"No se encontro '{appSettingsFileName}' ni en AppData ni en la carpeta de instalacion.",
                bundledConfigPath);
        }

        if (!File.Exists(appDataConfigPath))
        {
            File.Copy(bundledConfigPath, appDataConfigPath, overwrite: false);
        }

        return (bundledConfigPath, appDataConfigPath);
    }
}
