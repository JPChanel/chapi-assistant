using System.IO;
using System.Reflection;

namespace Chapi.Infrastructure.Git;

/// <summary>
/// Localiza el binario de Git a usar.
/// Prioridad: 1) Git embebido en la app, 2) Git del sistema (PATH).
/// Igual que dugite: la app lleva su propio Git para no depender del entorno del usuario.
/// </summary>
public static class GitBinaryLocator
{
    private static string? _cachedPath;
    private static readonly object _lock = new();

    /// <summary>
    /// Retorna la ruta completa al ejecutable git.exe.
    /// Busca primero en la carpeta de la aplicación (binario embebido), luego en el PATH del sistema.
    /// </summary>
    public static string GetGitPath()
    {
        if (_cachedPath != null) return _cachedPath;

        lock (_lock)
        {
            if (_cachedPath != null) return _cachedPath;
            _cachedPath = ResolveGitPath();
            return _cachedPath;
        }
    }

    private static string ResolveGitPath()
    {
        // 1. Buscar Git embebido junto al ejecutable de la app (como dugite)
        var appDir = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location ?? "");
        if (!string.IsNullOrEmpty(appDir))
        {
            var embeddedPaths = new[]
            {
                Path.Combine(appDir, "git", "cmd", "git.exe"),              // Estructura PortableGit
                Path.Combine(appDir, "git", "bin", "git.exe"),
                Path.Combine(appDir, "resources", "git", "cmd", "git.exe"), // Estructura GitHub Desktop
                Path.Combine(appDir, "git.exe"),                             // Raíz del directorio
            };

            foreach (var path in embeddedPaths)
            {
                if (File.Exists(path)) return path;
            }
        }

        // 2. Fallback: GitHub Desktop instalado (útil en desarrollo antes del primer build)
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var ghdBase = Path.Combine(localAppData, "GitHubDesktop");
        if (Directory.Exists(ghdBase))
        {
            // Buscar la versión más reciente de GitHub Desktop (app-X.Y.Z)
            var ghdVersions = Directory.GetDirectories(ghdBase, "app-*")
                .OrderByDescending(d => d)  // La más reciente primero
                .ToList();

            foreach (var version in ghdVersions)
            {
                var ghdGit = Path.Combine(version, "resources", "app", "git", "cmd", "git.exe");
                if (File.Exists(ghdGit)) return ghdGit;
            }
        }

        // 3. Buscar git.exe en el PATH del sistema
        var systemGit = FindInPath("git.exe");
        if (systemGit != null) return systemGit;

        // 3. Ubicaciones comunes de Git para Windows
        var commonPaths = new[]
        {
            @"C:\Program Files\Git\cmd\git.exe",
            @"C:\Program Files (x86)\Git\cmd\git.exe",
            @"C:\Git\cmd\git.exe",
        };

        foreach (var path in commonPaths)
        {
            if (File.Exists(path)) return path;
        }

        throw new InvalidOperationException(
            "No se encontró git.exe. Instala Git para Windows o incluye el binario embebido en la carpeta 'git' de la aplicación."
        );
    }

    private static string? FindInPath(string fileName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(';'))
        {
            var fullPath = Path.Combine(dir.Trim(), fileName);
            if (File.Exists(fullPath)) return fullPath;
        }
        return null;
    }

    /// <summary>
    /// Retorna una tupla (executablePath, arguments) para lanzar la consola interactiva de Git (Git Bash / Bash).
    /// Prioriza los binarios de Git embebidos en Chapi.
    /// </summary>
    public static (string ExePath, string Arguments)? GetGitBashLaunchInfo()
    {
        var appDir = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location ?? "");
        if (string.IsNullOrEmpty(appDir)) appDir = AppDomain.CurrentDomain.BaseDirectory;

        if (!string.IsNullOrEmpty(appDir))
        {
            // 1. Prioridad: git-bash.exe o bash.exe embebido en la aplicación (autónomo)
            var embeddedBashCandidates = new (string Path, string Args)[]
            {
                (Path.Combine(appDir, "git", "git-bash.exe"), ""),
                (Path.Combine(appDir, "git-bash.exe"), ""),
                (Path.Combine(appDir, "git", "usr", "bin", "bash.exe"), "--login -i"),
                (Path.Combine(appDir, "git", "bin", "bash.exe"), "--login -i"),
                (Path.Combine(appDir, "resources", "git", "git-bash.exe"), ""),
                (Path.Combine(appDir, "resources", "git", "usr", "bin", "bash.exe"), "--login -i"),
            };

            foreach (var (path, args) in embeddedBashCandidates)
            {
                if (File.Exists(path)) return (path, args);
            }
        }

        // 2. Fallback: GitHub Desktop local (desarrollo local)
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var ghdBase = Path.Combine(localAppData, "GitHubDesktop");
        if (Directory.Exists(ghdBase))
        {
            var ghdVersions = Directory.GetDirectories(ghdBase, "app-*")
                .OrderByDescending(d => d)
                .ToList();

            foreach (var version in ghdVersions)
            {
                var ghdBash = Path.Combine(version, "resources", "app", "git", "usr", "bin", "bash.exe");
                if (File.Exists(ghdBash)) return (ghdBash, "--login -i");

                var ghdGitBash = Path.Combine(version, "resources", "app", "git", "git-bash.exe");
                if (File.Exists(ghdGitBash)) return (ghdGitBash, "");
            }
        }

        // 3. Fallback: Git instalado en el sistema
        var commonBashPaths = new (string Path, string Args)[]
        {
            (@"C:\Program Files\Git\git-bash.exe", ""),
            (@"C:\Program Files\Git\bin\bash.exe", "--login -i"),
            (@"C:\Program Files (x86)\Git\git-bash.exe", ""),
            (@"C:\Program Files (x86)\Git\bin\bash.exe", "--login -i"),
        };

        foreach (var (path, args) in commonBashPaths)
        {
            if (File.Exists(path)) return (path, args);
        }

        var systemGitBash = FindInPath("git-bash.exe");
        if (systemGitBash != null) return (systemGitBash, "");

        var systemBash = FindInPath("bash.exe");
        if (systemBash != null) return (systemBash, "--login -i");

        return null;
    }

    /// <summary>
    /// Retorna true si se encontró git en alguna ubicación (embebida o sistema).
    /// </summary>
    public static bool IsGitAvailable()
    {
        try
        {
            GetGitPath();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
