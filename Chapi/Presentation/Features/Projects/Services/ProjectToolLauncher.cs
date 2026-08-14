using Chapi.Infrastructure.Git;
using Chapi.Infrastructure.Services;
using System.Diagnostics;
using System.IO;

namespace Chapi.Presentation.Features.Projects.Services;

public sealed class ProjectToolLauncher
{
    public void OpenVSCode(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "code",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Msg.Assistant($"Error al abrir VS Code: {ex.Message}");
        }
    }

    public void OpenVisualStudio(string path)
    {
        try
        {
            var solution = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                .FirstOrDefault(file =>
                    file.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase));

            if (solution == null)
            {
                Msg.Assistant("No se encontro ningun archivo .sln o .slnx");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = solution,
                UseShellExecute = true
            });
        }
        catch (UnauthorizedAccessException)
        {
            Msg.Assistant("No tienes permisos para acceder a algunas carpetas.");
        }
        catch (Exception ex)
        {
            Msg.Assistant($"Error al abrir Visual Studio: {ex.Message}");
        }
    }

    public void OpenExplorer(string path)
    {
        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch
        {
            Process.Start("explorer.exe", path);
        }
    }

    public void OpenAntigravity(string path)
    {
        try
        {
            var localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

            var antigravityCli = Path.Combine(
                localAppData,
                "Programs",
                "Antigravity IDE",
                "bin",
                "antigravity-ide.cmd");

            if (!File.Exists(antigravityCli))
            {
                Msg.Assistant("Antigravity IDE no detectado.");
                return;
            }

            if (IsWslPath(path))
            {
                var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length <= 2)
                {
                    Msg.Assistant("Ruta WSL inválida.");
                    return;
                }

                var distro = parts[1];

                var linuxPath = "/" + string.Join("/", parts.Skip(2));

                var remoteUri = $"vscode-remote://wsl+{distro}{linuxPath}";

                Process.Start(new ProcessStartInfo
                {
                    FileName = antigravityCli,
                    Arguments = $"--folder-uri \"{remoteUri}\"",
                    UseShellExecute = true
                });
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = antigravityCli,
                Arguments = $"--new-window \"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Msg.Assistant($"Error al abrir Antigravity: {ex.Message}");
        }
    }

    public void OpenCmd(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            WorkingDirectory = path
        });
    }

    public void OpenGitTerminal(string path)
    {
        try
        {
            var launchInfo = GitBinaryLocator.GetGitBashLaunchInfo();
            if (launchInfo == null)
            {
                Msg.Assistant("No se encontró el ejecutable de la consola de Git.");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = launchInfo.Value.ExePath,
                Arguments = launchInfo.Value.Arguments,
                WorkingDirectory = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Msg.Assistant($"Error al abrir la consola de Git: {ex.Message}");
        }
    }

    private static bool IsWslPath(string path)
    {
        return path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase);
    }
}
