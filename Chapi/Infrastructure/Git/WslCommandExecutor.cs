using System.Diagnostics;
using System.Text;
using Chapi.Domain.Common;

namespace Chapi.Infrastructure.Git;

/// <summary>
/// Ejecutor de comandos nativos de Git dentro de entornos WSL.
/// Proporciona una mejora de rendimiento significativa (10x-50x) respecto a I/O a través de 9P.
/// </summary>
public static class WslCommandExecutor
{
    public static async Task<Result<string>> ExecuteAsync(string windowsPath, string gitCommand)
    {
        return await Task.Run(() =>
        {
            try
            {
                var (distro, linuxPath) = ParseWslPath(windowsPath);
                if (string.IsNullOrEmpty(distro)) 
                    return Result<string>.Fail("La ruta proporcionada no pertenece a un sistema de archivos WSL.");

                // El comando final se ejecuta como: wsl -d Ubuntu -- git -C /home/user/... stash push -m "msg"
                var startInfo = new ProcessStartInfo
                {
                    FileName = "wsl.exe",
                    Arguments = $"-d {distro} -- {gitCommand.Replace("{path}", linuxPath)}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var process = Process.Start(startInfo);
                if (process == null) return Result<string>.Fail("No se pudo iniciar el proceso WSL.");

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    return Result<string>.Fail(!string.IsNullOrWhiteSpace(error) ? error : "Error de ejecución en WSL.");
                }

                return Result<string>.Success(output);
            }
            catch (Exception ex)
            {
                return Result<string>.Fail($"Fallo crítico al ejecutar comando en WSL: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Convierte una ruta de red de Windows (tipo \\wsl$\Ubuntu\...) a componentes WSL.
    /// </summary>
    public static (string distro, string linuxPath) ParseWslPath(string path)
    {
        string? prefix = null;
        if (path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase)) prefix = @"\\wsl$\";
        else if (path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase)) prefix = @"\\wsl.localhost\";

        if (prefix == null) return (string.Empty, string.Empty);

        var remaining = path.Substring(prefix.Length);
        var parts = remaining.Split(new[] { '\\' }, 2);
        
        string distro = parts[0];
        string linuxPath = parts.Length > 1 ? "/" + parts[1].Replace('\\', '/') : "/";

        return (distro, linuxPath);
    }
}
