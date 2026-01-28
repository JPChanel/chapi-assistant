using System.Diagnostics;

namespace Chapi.Infrastructure.Git;

/// <summary>
/// Resultado de la ejecución de un comando Git.
/// </summary>
public class CommandResult
{
    public bool IsSuccess { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;

    public static CommandResult Success(string output) =>
        new() { IsSuccess = true, Output = output };

    public static CommandResult Fail(string error) =>
        new() { IsSuccess = false, Error = error };
}

/// <summary>
/// Ejecutor de comandos Git.
/// Encapsula la lógica de ejecución de procesos Git.
/// </summary>
public class GitCommandExecutor
{
    public async Task<CommandResult> ExecuteAsync(string command, string workingDirectory)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = command,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            using var process = Process.Start(processInfo);
            if (process == null)
                return CommandResult.Fail("No se pudo iniciar el proceso Git");

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            // Git a veces usa stderr para mensajes informativos
            if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
                return CommandResult.Fail(error);

            return CommandResult.Success(output);
        }
        catch (Exception ex)
        {
            return CommandResult.Fail($"Error ejecutando Git: {ex.Message}");
        }
    }
}
