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
    public static async Task<Result<string>> ExecuteAsync(string windowsPath, string gitCommand, int timeoutMilliseconds = 60000)
    {
        return await Task.Run(() =>
        {
            try
            {
                var (distro, linuxPath) = ParseWslPath(windowsPath);
                if (string.IsNullOrEmpty(distro)) 
                    return Result<string>.Fail("La ruta proporcionada no pertenece a un sistema de archivos WSL.");

                // Configurar entorno de Proxy para procesos Linux si está habilitado en Chapi
                string proxyEnv = "";
                var settings = Infrastructure.Persistence.Settings.UserSettingsService.LoadSettings();
                if (settings.ProxyEnabled && !string.IsNullOrWhiteSpace(settings.ProxyUrl))
                {
                    var url = settings.ProxyUrl;
                    string scheme = "http";
                    if (url.StartsWith("http://")) url = url.Substring(7);
                    else if (url.StartsWith("https://")) { scheme = "https"; url = url.Substring(8); }
                    
                    string auth = "";
                    if (!string.IsNullOrWhiteSpace(settings.ProxyUser) && !string.IsNullOrWhiteSpace(settings.ProxyPass))
                    {
                        auth = $"{settings.ProxyUser}:{settings.ProxyPass}@";
                    }
                    string fullProxy = $"{scheme}://{auth}{url}";
                    proxyEnv = $"env http_proxy=\"{fullProxy}\" https_proxy=\"{fullProxy}\" ALL_PROXY=\"{fullProxy}\" ";
                }

                // Añadir GIT_TERMINAL_PROMPT=0 para evitar bloqueos por solicitudes de credenciales
                var startInfo = new ProcessStartInfo
                {
                    FileName = "wsl.exe",
                    Arguments = $"-d {distro} -- {proxyEnv}env GIT_TERMINAL_PROMPT=0 {gitCommand.Replace("{path}", linuxPath)}",

                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var process = new Process { StartInfo = startInfo };
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

                if (!process.Start()) return Result<string>.Fail("No se pudo iniciar el proceso WSL.");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Añadir un timeout de seguridad configurable
                if (!process.WaitForExit(timeoutMilliseconds))
                {
                    process.Kill();
                    return Result<string>.Fail($"El comando WSL superó el tiempo de espera ({(timeoutMilliseconds/1000)}s).");
                }

                var output = outputBuilder.ToString().TrimEnd();
                var error = errorBuilder.ToString().TrimEnd();

                // Git a veces escribe en stderr para advertencias, pero si incluye 'fatal:' o 'error:', FALLÓ
                bool hasFatalErrorInStderr = error.Contains("fatal:", StringComparison.OrdinalIgnoreCase) || 
                                             error.Contains("error:", StringComparison.OrdinalIgnoreCase);

                if (process.ExitCode != 0 || hasFatalErrorInStderr)
                {
                    var finalError = !string.IsNullOrWhiteSpace(error) ? error : (hasFatalErrorInStderr ? error : "Error de ejecución en WSL.");
                    
                    // Mejorar detección de problemas de autenticación
                    if (finalError.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase) || 
                        finalError.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
                        finalError.Contains("fatal: could not read Password", StringComparison.OrdinalIgnoreCase))
                    {
                        finalError = "Error de autenticación Git. Por favor, verifica tus credenciales o token en Chapi. " + finalError;
                    }

                    return Result<string>.Fail(finalError);
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
