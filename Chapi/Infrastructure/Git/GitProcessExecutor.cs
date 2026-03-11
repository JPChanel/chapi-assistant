using Chapi.Domain.Common;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Chapi.Infrastructure.Git;

/// <summary>
/// Ejecutor de procesos Git de bajo nivel.
/// Mismo modelo que GitHub Desktop (dugite): lanza git.exe con ArgumentList para máxima seguridad.
/// </summary>
public static class GitProcessExecutor
{
    /// <summary>
    /// Ejecuta git con argumentos seguros (sin interpolación de shell).
    /// Equivalente a dugite.exec() en GitHub Desktop.
    /// </summary>
    public static async Task<Result<string>> RunAsync(string workingDirectory, params string[] args)
    {
        return await RunAsync(workingDirectory, 60_000, args);
    }

    public static async Task<Result<string>> RunAsync(string workingDirectory, int timeoutMs, params string[] args)
    {
        return await RunAsync(workingDirectory, timeoutMs, null, args);
    }

    public static async Task<Result<string>> RunAsync(string workingDirectory, int timeoutMs, Dictionary<string, string>? environment, params string[] args)
    {
        return await Task.Run(() =>
        {
            try
            {
                var gitPath = GitBinaryLocator.GetGitPath();

                var startInfo = new ProcessStartInfo
                {
                    FileName = gitPath,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                // Configuraciones globales críticas para soporte de WSL/UNC y archivos largos
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add("core.longpaths=true");
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add("safe.directory=*");
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add("core.fscache=false"); // Evita el "could not write index" en UNC/WSL

                // Usar ArgumentList (igual que dugite): evita problemas con espacios/comillas
                foreach (var arg in args)
                    startInfo.ArgumentList.Add(arg);

                // Inyectar variables de entorno extra si existen
                if (environment != null)
                {
                    foreach (var kvp in environment)
                        startInfo.Environment[kvp.Key] = kvp.Value;
                }
                // Desactivar explícitamente cualquier credential helper del sistema (como GCM) para evitar popups
                // Chapi maneja sus propias credenciales en memoria para esta sesión.
                // startInfo.ArgumentList.Insert(0, "-c");
                // startInfo.ArgumentList.Insert(1, "credential.helper=");

                bool disableGitPrompt = startInfo.Environment.TryGetValue("CHAPI_DISABLE_GIT_PROMPT", out var disablePromptValue) &&
                    string.Equals(disablePromptValue, "1", StringComparison.Ordinal);

                if (startInfo.Environment.ContainsKey("CHAPI_GIT_TOKEN"))
                {
                    // Crear un script que identifique si Git pide Usuario o Contraseña
                    // Para la mayoría de proveedores OAuth2 (GitHub/GitLab), cualquier usuario funciona si la clave es el token.
                    string tempAskPass = Path.Combine(Path.GetTempPath(), $"chapi_askpass_{Guid.NewGuid():N}.bat");
                    
                    var scriptContent = new StringBuilder();
                    scriptContent.AppendLine("@echo off");
                    scriptContent.AppendLine("set \"prompt=%~1\"");
                    // Si el prompt contiene "Username", devolvemos "oauth2" (estándar para tokens)
                    // Si contiene "Password", devolvemos el token real.
                    scriptContent.AppendLine("echo %prompt% | findstr /i \"Username\" >nul");
                    scriptContent.AppendLine("if %errorlevel% equ 0 (");
                    scriptContent.AppendLine("    echo oauth2");
                    scriptContent.AppendLine(") else (");
                    scriptContent.AppendLine("    echo %CHAPI_GIT_TOKEN%");
                    scriptContent.AppendLine(")");
                    
                    File.WriteAllText(tempAskPass, scriptContent.ToString());
                    
                    startInfo.Environment["GIT_ASKPASS"] = tempAskPass;
                    startInfo.Environment["GIT_TERMINAL_PROMPT"] = disableGitPrompt ? "0" : "1";
                }
                else
                {
                    // Permitir prompt para que el usuario pueda autenticarse vía GCM/OAuth2
                    startInfo.Environment["GIT_TERMINAL_PROMPT"] = disableGitPrompt ? "0" : "1";
                }

                using var process = new Process { StartInfo = startInfo };
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (_, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

                if (!process.Start())
                    return Result<string>.Fail("No se pudo iniciar git.exe.");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(timeoutMs))
                {
                    process.Kill();
                    return Result<string>.Fail($"Git superó el timeout ({timeoutMs / 1000}s).");
                }

                var output = outputBuilder.ToString().TrimEnd();
                var error = errorBuilder.ToString().TrimEnd();

                // Git exit 0 con error en stderr (por ejemplo: warnings) = éxito
                // Git exit !=0 = fallo real
                if (process.ExitCode != 0)
                {
                    // stash no tiene nada que guardar retorna exit 1 con mensaje específico
                    if (error.Contains("No local changes to save") || output.Contains("No local changes to save"))
                        return Result<string>.Success("No local changes to save");

                    var finalError = !string.IsNullOrWhiteSpace(error) ? error : output;
                    return Result<string>.Fail(string.IsNullOrWhiteSpace(finalError) ? "Error desconocido de git." : finalError);
                }

                return Result<string>.Success(output);
            }
            catch (Exception ex)
            {
                return Result<string>.Fail($"Excepción al ejecutar git: {ex.Message}");
            }
        });
    }
}
