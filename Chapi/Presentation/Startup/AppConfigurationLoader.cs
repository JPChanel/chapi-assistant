using Microsoft.Extensions.Configuration;
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
            .Build();
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
