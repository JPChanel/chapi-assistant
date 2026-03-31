using Microsoft.Extensions.Configuration;
using System.IO;

namespace Chapi.Startup;

public static class AppConfigurationLoader
{
    public static IConfiguration Load(string appSettingsFileName)
    {
        var appSettingsPath = EnsureAppSettingsFile(appSettingsFileName);

        return new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(appSettingsPath)!)
            .AddJsonFile(Path.GetFileName(appSettingsPath), optional: false, reloadOnChange: true)
            .Build();
    }

    private static string EnsureAppSettingsFile(string appSettingsFileName)
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Chapi");

        Directory.CreateDirectory(appDataDirectory);

        var appDataConfigPath = Path.Combine(appDataDirectory, appSettingsFileName);
        if (File.Exists(appDataConfigPath))
        {
            return appDataConfigPath;
        }

        var bundledConfigPath = Path.Combine(AppContext.BaseDirectory, appSettingsFileName);
        if (!File.Exists(bundledConfigPath))
        {
            throw new FileNotFoundException(
                $"No se encontró '{appSettingsFileName}' ni en AppData ni en la carpeta de instalación.",
                bundledConfigPath);
        }

        File.Copy(bundledConfigPath, appDataConfigPath, overwrite: false);
        return appDataConfigPath;
    }
}
