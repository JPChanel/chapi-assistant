using System.IO;

namespace Chapi.Infrastructure.Common;

public class FindApiDirectory
{
    public static string GetDirectory(string basePath)
    {
        var subdirs = Directory.GetDirectories(basePath);
        foreach (var dir in subdirs)
        {
            var files = Directory.GetFiles(dir);
            if (files.Any(f => Path.GetFileName(f).Equals("Program.cs", StringComparison.OrdinalIgnoreCase)))
            {
                return dir;
            }
        }
        return null;
    }

    /// <summary>
    /// Obtiene la lista de directorios (módulos) dentro de la carpeta 'Domain' del proyecto.
    /// Retorna rutas relativas (ej: "Ventas", "Seguridad/Usuarios").
    /// </summary>
    public static List<string> GetModuleDirectories(string projectDirectory)
    {
        var modules = new List<string>();
        string domainPath = Path.Combine(projectDirectory, "Domain");
        
        // Lista de carpetas técnicas a ignorar
        var ignoredFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Shared",
            "Exceptions",
            "Entities",
            "Interface",
            "Interfaces",
            "bin",
            "obj",
            ".vs",
            ".git"
        };

        if (!Directory.Exists(domainPath)) return modules;

        // Función recursiva local
        void ScanDirectories(string currentPath, string relativePrefix)
        {
            try
            {
                var dirs = Directory.GetDirectories(currentPath);
                foreach (var dir in dirs)
                {
                    string dirName = Path.GetFileName(dir);

                    // Filtros
                    if (dirName.StartsWith(".") || ignoredFolders.Contains(dirName))
                    {
                        continue;
                    }

                    string relativePath = string.IsNullOrEmpty(relativePrefix)
                        ? dirName
                        : $"{relativePrefix}/{dirName}";

                    modules.Add(relativePath);

                    // Recursión para sub-módulos
                    ScanDirectories(dir, relativePath);
                }
            }
            catch { /* Ignorar errores de acceso */ }
        }

        ScanDirectories(domainPath, "");
        return modules;
    }
}
