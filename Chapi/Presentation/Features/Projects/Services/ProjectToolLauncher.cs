using Chapi.Infrastructure.Git;
using Chapi.Infrastructure.Services;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Chapi.Presentation.Features.Projects.Services;

public sealed class ProjectToolLauncher
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>
    /// Detecta de forma inteligente qué editor tiene abierto el proyecto/archivo y lo abre en la línea indicada
    /// reutilizando la ventana activa para no crear nuevas instancias, con soporte nativo para WSL.
    /// </summary>
    public void SmartOpen(string projectPath, string filePath, int? lineNum = null)
    {
        if (string.IsNullOrEmpty(projectPath) && string.IsNullOrEmpty(filePath)) return;

        string targetProjectPath = !string.IsNullOrEmpty(projectPath) ? projectPath : (Path.GetDirectoryName(filePath) ?? string.Empty);
        string fullPath = GetAbsoluteChangePath(targetProjectPath, filePath);
        string projectName = !string.IsNullOrEmpty(targetProjectPath) ? new DirectoryInfo(targetProjectPath).Name : string.Empty;
        string normalizedGitPath = ToGitStylePath(filePath);

        string? slnName = null;
        try
        {
            if (Directory.Exists(targetProjectPath))
            {
                var slnFile = Directory.EnumerateFiles(targetProjectPath, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (slnFile != null)
                {
                    slnName = Path.GetFileNameWithoutExtension(slnFile);
                }
            }
        }
        catch { }

        bool isWsl = IsWslPath(targetProjectPath) || IsWslPath(fullPath);

        // Escanear procesos de editores abiertos
        var processes = Process.GetProcesses();
        var matchingEditors = new List<(Process Process, string EditorType, int Priority)>();

        foreach (var p in processes)
        {
            try
            {
                string procName = p.ProcessName;
                string title = p.MainWindowTitle ?? string.Empty;

                bool isAntigravity = procName.Contains("Antigravity", StringComparison.OrdinalIgnoreCase);
                bool isVsCode = procName.Equals("Code", StringComparison.OrdinalIgnoreCase) || procName.Equals("Code - Insiders", StringComparison.OrdinalIgnoreCase);
                bool isCursor = procName.Equals("Cursor", StringComparison.OrdinalIgnoreCase);
                bool isWindsurf = procName.Equals("Windsurf", StringComparison.OrdinalIgnoreCase);
                bool isVs = procName.Equals("devenv", StringComparison.OrdinalIgnoreCase);

                if (!isAntigravity && !isVsCode && !isCursor && !isWindsurf && !isVs)
                    continue;

                int matchScore = 10;
                if (!string.IsNullOrEmpty(title))
                {
                    if ((!string.IsNullOrEmpty(projectName) && title.Contains(projectName, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(slnName) && title.Contains(slnName, StringComparison.OrdinalIgnoreCase)))
                    {
                        matchScore = 100;
                    }
                    else if (!string.IsNullOrEmpty(filePath) && title.Contains(Path.GetFileName(filePath), StringComparison.OrdinalIgnoreCase))
                    {
                        matchScore = 80;
                    }
                }

                string editorType = isAntigravity ? "antigravity" :
                                   isCursor ? "cursor" :
                                   isWindsurf ? "windsurf" :
                                   isVs ? "devenv" : "vscode";

                matchingEditors.Add((p, editorType, matchScore));
            }
            catch { }
        }

        var bestEditor = matchingEditors
            .OrderByDescending(e => e.Priority)
            .FirstOrDefault();

        // Si encontramos una ventana coincidente, traerla al frente
        if (bestEditor.Process != null)
        {
            try
            {
                var hwnd = bestEditor.Process.MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, 9); // SW_RESTORE
                    SetForegroundWindow(hwnd);
                }
            }
            catch { }
        }

        // 1. Visual Studio (solo para Windows local con .sln abierto)
        if (!isWsl && bestEditor.EditorType == "devenv" && bestEditor.Priority >= 80)
        {
            if (OpenVisualStudio(fullPath, lineNum, bestEditor.Process)) return;
        }

        // 2. Antigravity
        if (bestEditor.EditorType == "antigravity")
        {
            if (OpenAntigravity(fullPath, lineNum, targetProjectPath, normalizedGitPath, isWsl)) return;
        }

        // 3. Cursor
        if (bestEditor.EditorType == "cursor")
        {
            if (OpenCursor(fullPath, lineNum, targetProjectPath, normalizedGitPath, isWsl)) return;
        }

        // 4. Windsurf
        if (bestEditor.EditorType == "windsurf")
        {
            if (OpenWindsurf(fullPath, lineNum, targetProjectPath, normalizedGitPath, isWsl)) return;
        }

        // 5. VS Code
        if (bestEditor.EditorType == "vscode" || bestEditor.Process != null)
        {
            if (OpenVSCode(fullPath, lineNum, targetProjectPath, normalizedGitPath, isWsl)) return;
        }

        // Fallbacks ordenados
        if (OpenAntigravity(fullPath, lineNum, targetProjectPath, normalizedGitPath, isWsl)) return;
        if (OpenVSCode(fullPath, lineNum, targetProjectPath, normalizedGitPath, isWsl)) return;
        if (OpenCursor(fullPath, lineNum, targetProjectPath, normalizedGitPath, isWsl)) return;
        if (OpenWindsurf(fullPath, lineNum, targetProjectPath, normalizedGitPath, isWsl)) return;
        if (!isWsl) OpenVisualStudio(fullPath, lineNum);
    }

    public bool OpenAntigravity(string path, int? lineNum = null, string? projectPath = null, string? gitPath = null, bool isWsl = false)
    {
        try
        {
            var cli = FindAntigravityCli();
            if (cli == null) return false;
            return LaunchVsCodeStyleCli(cli, path, lineNum, projectPath, gitPath, isWsl);
        }
        catch (Exception ex)
        {
            Msg.Assistant($"Error al abrir Antigravity: {ex.Message}");
            return false;
        }
    }

    public bool OpenVSCode(string path, int? lineNum = null, string? projectPath = null, string? gitPath = null, bool isWsl = false)
    {
        try
        {
            return LaunchVsCodeStyleCli("code", path, lineNum, projectPath, gitPath, isWsl);
        }
        catch (Exception ex)
        {
            Msg.Assistant($"Error al abrir VS Code: {ex.Message}");
            return false;
        }
    }

    public bool OpenCursor(string path, int? lineNum = null, string? projectPath = null, string? gitPath = null, bool isWsl = false)
    {
        try
        {
            var cli = FindCursorCli() ?? "cursor";
            return LaunchVsCodeStyleCli(cli, path, lineNum, projectPath, gitPath, isWsl);
        }
        catch (Exception ex)
        {
            Msg.Assistant($"Error al abrir Cursor: {ex.Message}");
            return false;
        }
    }

    public bool OpenWindsurf(string path, int? lineNum = null, string? projectPath = null, string? gitPath = null, bool isWsl = false)
    {
        try
        {
            var cli = FindWindsurfCli() ?? "windsurf";
            return LaunchVsCodeStyleCli(cli, path, lineNum, projectPath, gitPath, isWsl);
        }
        catch (Exception ex)
        {
            Msg.Assistant($"Error al abrir Windsurf: {ex.Message}");
            return false;
        }
    }

    public bool OpenVisualStudio(string path, int? lineNum = null, Process? activeEditor = null)
    {
        try
        {
            if (File.Exists(path))
            {
                string devenvPath = GetVisualStudioPath(activeEditor);
                if (File.Exists(devenvPath))
                {
                    string args = lineNum.HasValue
                        ? $"/Edit \"{path}\" /Command \"Edit.GoTo {lineNum.Value}\""
                        : $"/Edit \"{path}\"";

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = devenvPath,
                        Arguments = args,
                        UseShellExecute = true
                    });
                    return true;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
                return true;
            }

            if (Directory.Exists(path))
            {
                var solution = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                    .FirstOrDefault(file =>
                        file.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                        file.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase));

                if (solution != null)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = solution,
                        UseShellExecute = true
                    });
                    return true;
                }

                Msg.Assistant("No se encontró ningún archivo .sln o .slnx en la carpeta.");
                return false;
            }

            return false;
        }
        catch (Exception ex)
        {
            Msg.Assistant($"Error al abrir Visual Studio: {ex.Message}");
            return false;
        }
    }

    public void OpenExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else if (Directory.Exists(path))
            {
                Process.Start("explorer.exe", $"\"{path}\"");
            }
        }
        catch (Exception ex)
        {
            Msg.Assistant($"Error al abrir el explorador: {ex.Message}");
        }
    }

    public void OpenCmd(string path)
    {
        try
        {
            if (IsWslPath(path))
            {
                var (isWsl, distro, linuxPath, _) = ParseWslPath(path);
                if (isWsl)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "wsl.exe",
                        Arguments = $"-d {distro} --cd \"{linuxPath}\"",
                        UseShellExecute = true
                    });
                    return;
                }
            }

            string workingDir = Directory.Exists(path) ? path : (Path.GetDirectoryName(path) ?? path);
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                WorkingDirectory = workingDir
            });
        }
        catch (Exception ex)
        {
            Msg.Assistant($"Error al abrir consola CMD: {ex.Message}");
        }
    }

    public void OpenGitTerminal(string path)
    {
        try
        {
            if (IsWslPath(path))
            {
                var (isWsl, distro, linuxPath, _) = ParseWslPath(path);
                if (isWsl)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "wsl.exe",
                        Arguments = $"-d {distro} --cd \"{linuxPath}\"",
                        UseShellExecute = true
                    });
                    return;
                }
            }

            var launchInfo = GitBinaryLocator.GetGitBashLaunchInfo();
            if (launchInfo == null)
            {
                Msg.Assistant("No se encontró el ejecutable de la consola de Git.");
                return;
            }

            string workingDir = Directory.Exists(path) ? path : (Path.GetDirectoryName(path) ?? path);
            Process.Start(new ProcessStartInfo
            {
                FileName = launchInfo.Value.ExePath,
                Arguments = launchInfo.Value.Arguments,
                WorkingDirectory = workingDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Msg.Assistant($"Error al abrir la consola de Git: {ex.Message}");
        }
    }

    #region Helpers

    private static bool LaunchVsCodeStyleCli(string cliExe, string fullPath, int? lineNum = null, string? projectPath = null, string? gitPath = null, bool isWsl = false)
    {
        try
        {
            string arguments;
            bool targetIsWsl = isWsl || IsWslPath(fullPath) || IsWslPath(projectPath ?? string.Empty);

            if (targetIsWsl)
            {
                string targetPath = !string.IsNullOrEmpty(fullPath) ? fullPath : (projectPath ?? string.Empty);
                var (isParsed, distro, linuxPath, remoteUri) = ParseWslPath(targetPath);

                if (isParsed)
                {
                    if (!string.IsNullOrEmpty(projectPath) && !string.IsNullOrEmpty(gitPath) && !fullPath.Contains(gitPath))
                    {
                        var (_, _, projLinuxPath, _) = ParseWslPath(projectPath);
                        string fileLinuxPath = (projLinuxPath.TrimEnd('/') + "/" + gitPath.TrimStart('/')).Replace("//", "/");
                        remoteUri = $"vscode-remote://wsl+{distro}{EscapeVsCodeRemotePath(fileLinuxPath)}";
                    }

                    bool isDirectory = Directory.Exists(targetPath) || (!lineNum.HasValue && !Path.HasExtension(targetPath));

                    if (isDirectory)
                    {
                        arguments = $"--folder-uri \"{remoteUri}\"";
                    }
                    else if (lineNum.HasValue)
                    {
                        arguments = $"--reuse-window --goto \"{remoteUri}:{lineNum.Value}\"" ;
                    }
                    else
                    {
                        arguments = $"--reuse-window --file-uri \"{remoteUri}\"";
                    }
                }
                else
                {
                    arguments = lineNum.HasValue ? $"--reuse-window --goto \"{fullPath}:{lineNum.Value}\"" : $"--reuse-window \"{fullPath}\"";
                }
            }
            else
            {
                arguments = lineNum.HasValue ? $"--reuse-window --goto \"{fullPath}:{lineNum.Value}\"" : $"--reuse-window \"{fullPath}\"";
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = cliExe,
                Arguments = arguments,
                UseShellExecute = true,
                CreateNoWindow = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsWslPath(string path)
    {
        return !string.IsNullOrEmpty(path) &&
               (path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase));
    }

    public static (bool IsWsl, string Distro, string LinuxPath, string RemoteUri) ParseWslPath(string path)
    {
        if (!IsWslPath(path)) return (false, string.Empty, string.Empty, string.Empty);

        var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return (false, string.Empty, string.Empty, string.Empty);

        string distro = parts[1];
        string linuxPath = "/" + string.Join("/", parts.Skip(2)).Replace("\\", "/");
        if (string.IsNullOrEmpty(linuxPath) || linuxPath == "/") linuxPath = "/";

        string escapedLinuxPath = EscapeVsCodeRemotePath(linuxPath);
        string remoteUri = $"vscode-remote://wsl+{distro}{escapedLinuxPath}";

        return (true, distro, linuxPath, remoteUri);
    }

    public static string? FindAntigravityCli()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates = {
            Path.Combine(localAppData, "Programs", "Antigravity IDE", "bin", "antigravity-ide.cmd"),
            Path.Combine(localAppData, "Programs", "Antigravity", "bin", "antigravity.cmd"),
            Path.Combine(localAppData, "Programs", "Antigravity", "antigravity.cmd"),
            Path.Combine(localAppData, "Programs", "Antigravity IDE", "antigravity-ide.cmd")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static string? FindCursorCli()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates = {
            Path.Combine(localAppData, "Programs", "cursor", "resources", "app", "bin", "cursor.cmd"),
            Path.Combine(localAppData, "Programs", "Cursor", "resources", "app", "bin", "cursor.cmd")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static string? FindWindsurfCli()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates = {
            Path.Combine(localAppData, "Programs", "Windsurf", "bin", "windsurf.cmd"),
            Path.Combine(localAppData, "Programs", "windsurf", "bin", "windsurf.cmd")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string GetVisualStudioPath(Process? activeEditor)
    {
        // 1. Si hay un proceso de Visual Studio activo, obtener su ruta exacta
        if (activeEditor != null && activeEditor.ProcessName.Equals("devenv", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string? exePath = activeEditor.MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath)) return exePath;
            }
            catch { }
        }

        try
        {
            var runningVs = Process.GetProcessesByName("devenv").FirstOrDefault();
            if (runningVs?.MainModule?.FileName is string runningPath && File.Exists(runningPath))
            {
                return runningPath;
            }
        }
        catch { }

        // 2. Consultar el Registro de Windows (App Paths\devenv.exe)
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\devenv.exe")
                         ?? Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\devenv.exe");
            if (key?.GetValue(null) is string regPath && File.Exists(regPath))
            {
                return regPath;
            }
        }
        catch { }

        // 3. Usar la herramienta oficial de Microsoft (vswhere.exe) que detecta cualquier versión (2019, 2022, 2026, 2030, etc.)
        try
        {
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var vswherePath = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");

            if (File.Exists(vswherePath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = vswherePath,
                    Arguments = "-latest -property productPath",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(2000);
                    if (!string.IsNullOrEmpty(output) && File.Exists(output))
                    {
                        return output;
                    }
                }
            }
        }
        catch { }

        // 4. Búsqueda dinámica en carpetas de Program Files sin fijar años ni ediciones
        try
        {
            string[] baseDirs = {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };

            foreach (var baseDir in baseDirs)
            {
                var vsDir = Path.Combine(baseDir, "Microsoft Visual Studio");
                if (Directory.Exists(vsDir))
                {
                    var found = Directory.EnumerateFiles(vsDir, "devenv.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (found != null) return found;
                }
            }
        }
        catch { }

        return "devenv.exe";
    }

    private static string GetAbsoluteChangePath(string projectPath, string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return projectPath;
        if (Path.IsPathRooted(filePath)) return Path.GetFullPath(filePath);

        var normalizedRelativePath = filePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.GetFullPath(Path.Combine(projectPath, normalizedRelativePath));
    }

    private static string ToGitStylePath(string filePath)
    {
        return string.IsNullOrWhiteSpace(filePath) ? string.Empty : filePath.Replace('\\', '/');
    }

    private static string EscapeVsCodeRemotePath(string linuxPath)
    {
        var parts = linuxPath.Split('/', StringSplitOptions.None);
        for (var i = 0; i < parts.Length; i++)
        {
            if (!string.IsNullOrEmpty(parts[i]))
            {
                parts[i] = Uri.EscapeDataString(parts[i]);
            }
        }
        return string.Join("/", parts);
    }

    #endregion
}
