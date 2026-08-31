using Chapi.Domain.Entities;
using Chapi.Infrastructure.Services;
using Chapi.Presentation.Shared.Dialogs.Views;
using DiffPlex.DiffBuilder.Model;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Chapi.Presentation.Features.Changes.ViewModels;

namespace Chapi.Presentation.Features.Changes.Views;

public partial class ChangesView : UserControl
{
    private ChangesViewModel _viewModel => DataContext as ChangesViewModel;

    public ChangesView()
    {
        InitializeComponent();
    }

    private void btnInstallGit_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://git-scm.com/downloads",
            UseShellExecute = true
        });
    }

    private void btnRefreshGitCheck_Click(object sender, RoutedEventArgs e)
    {
        _ = _viewModel?.ForceRefreshAsync();
    }

    private async void btnGitCommitIA_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        await MainWindow.Instance.RunWithLoading(async () =>
        {
            await _viewModel.GenerateCommitMessageCommand.ExecuteAsync(null);
        });
    }

    private void StashSelectedMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.StashSelectedCommand.Execute(null);
    }

    private async void DiscardAllChangesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var result = await DialogService.ShowConfirmDialog(
            "Confirmar Descarte",
            "¿Estás seguro de que deseas descartar TODOS los cambios? Esta acción no se puede deshacer.",
            DialogVariant.Warning);

        if (result && _viewModel != null)
        {
            await _viewModel.DiscardAllCommand.ExecuteAsync(null);
        }
    }

    private void DiscardAllStashesButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.ClearStashesCommand.Execute(null);
    }

    private void StashListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StashListView.SelectedItem is GitStash stash && _viewModel != null)
        {
            _viewModel.SelectedStash = stash;
            _viewModel.IsStashViewVisible = true;
            StashListView.SelectedItem = null; // Reset para permitir volver a seleccionar
        }
    }

    private void RestoreStashItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is GitStash stash)
        {
            _viewModel?.PopStashCommand.Execute(stash);
        }
    }

    private void DiscardStashItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is GitStash stash)
        {
            _viewModel?.DropStashCommand.Execute(stash);
        }
    }

    private void ChangesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.SelectedChange = ChangesListView.SelectedItem as ChangeItemViewModel;
        }
    }

    private async void DiscardChangesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is ChangeItemViewModel item)
        {
            var result = await DialogService.ShowConfirmDialog(
                "Confirmar Descarte",
                $"¿Estás seguro de que deseas descartar los cambios en '{item.FileName}'?\n\nEsta acción no se puede deshacer.",
                DialogVariant.Warning);

            if (result)
            {
                await _viewModel?.DiscardCommand.ExecuteAsync(item);
            }
        }
    }

    private void ProjectMenuItem_OpenAntigravity_Click(object sender, RoutedEventArgs e)
    {
        string path = GetPathFromMenuItem(sender);
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var agyCli = FindAntigravityCli();
            if (agyCli != null)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = agyCli,
                    Arguments = $"--reuse-window \"{path}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true
                });
            }
            else
            {
                MessageBox.Show("No se encontró la instalación de Antigravity IDE.");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al abrir en Antigravity: {ex.Message}");
        }
    }

    private void ProjectMenuItem_OpenVSCode_Click(object sender, RoutedEventArgs e)
    {
        string path = GetPathFromMenuItem(sender);
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "code",
                Arguments = $"--reuse-window \"{path}\"",
                UseShellExecute = true,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo iniciar VS Code: {ex.Message}");
        }
    }

    private void ProjectMenuItem_OpenVisualStudio_Click(object sender, RoutedEventArgs e)
    {
        string path = GetPathFromMenuItem(sender);
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            string devenvPath = GetVisualStudioPath(null);
            if (File.Exists(devenvPath) && File.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = devenvPath,
                    Arguments = $"/Edit \"{path}\"",
                    UseShellExecute = true
                });
                return;
            }

            // Si es archivo o solución, abrir con el sistema
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al abrir Visual Studio: {ex.Message}");
        }
    }

    private void ProjectMenuItem_OpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        string path = GetPathFromMenuItem(sender);
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            if (File.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
            else if (Directory.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    private void StashView_RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedStash is GitStash stash)
        {
            _viewModel.PopStashCommand.Execute(stash);
            _viewModel.IsStashViewVisible = false;
        }
    }

    private void RestoreLatestStashButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.PopAllStashesCommand.Execute(null);
    }

    private async void StashView_DiscardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedStash is GitStash stash)
        {
            var result = await DialogService.ShowConfirmDialog(
                "Confirmar Eliminación",
                $"¿Estás seguro de que deseas eliminar el stash '{stash.Message}'?",
                DialogVariant.Warning);

            if (result)
            {
                await _viewModel.DropStashCommand.ExecuteAsync(stash);
                _viewModel.IsStashViewVisible = false;
            }
        }
    }

    private void StashView_BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.IsStashViewVisible = false;
            _viewModel.SelectedStash = null;
            _viewModel.SelectedStashedFile = null;
            _ = _viewModel.LoadDiffAsync();
        }
    }

    private void StashFilesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StashFilesListView.SelectedItem is ChangeItemViewModel item && _viewModel != null)
        {
            _viewModel.SelectedStashedFile = item;
        }
    }

    private void DiffLine_ContextMenuOpening(object sender, ContextMenuEventArgs e) { }

    private void DiffLineMenu_OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is DiffPiece line && _viewModel?.SelectedChange != null)
        {
            try
            {
                string projectPath = _viewModel.ProjectPath;
                string filePath = _viewModel.SelectedChange.FilePath;
                int? lineNum = line.Position;

                if (!lineNum.HasValue) return;

                SmartOpenFileInEditor(projectPath, filePath, lineNum.Value);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo abrir el editor: {ex.Message}");
            }
        }
    }

    private void SmartOpenFileInEditor(string projectPath, string filePath, int lineNum)
    {
        string projectName = new DirectoryInfo(projectPath).Name;
        string fullPath = GetAbsoluteChangePath(projectPath, filePath);
        string normalizedGitPath = ToGitStylePath(filePath);

        string? slnName = null;
        try
        {
            var slnFile = Directory.EnumerateFiles(projectPath, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (slnFile != null)
            {
                slnName = Path.GetFileNameWithoutExtension(slnFile);
            }
        }
        catch { }

        bool isWsl = projectPath.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase) ||
                     projectPath.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase);

        // Detectar procesos de editores en ejecución
        var processes = System.Diagnostics.Process.GetProcesses();
        var matchingEditors = new List<(System.Diagnostics.Process Process, string EditorType, int Priority)>();

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
                bool isRider = procName.StartsWith("rider", StringComparison.OrdinalIgnoreCase);

                if (!isAntigravity && !isVsCode && !isCursor && !isWindsurf && !isVs && !isRider)
                    continue;

                int matchScore = 10;
                if (!string.IsNullOrEmpty(title))
                {
                    if (title.Contains(projectName, StringComparison.OrdinalIgnoreCase) ||
                        (slnName != null && title.Contains(slnName, StringComparison.OrdinalIgnoreCase)))
                    {
                        matchScore = 100;
                    }
                    else if (title.Contains(Path.GetFileName(filePath), StringComparison.OrdinalIgnoreCase))
                    {
                        matchScore = 80;
                    }
                }

                string editorType = isAntigravity ? "antigravity" :
                                   isCursor ? "cursor" :
                                   isWindsurf ? "windsurf" :
                                   isVs ? "devenv" :
                                   isRider ? "rider" : "vscode";

                matchingEditors.Add((p, editorType, matchScore));
            }
            catch { }
        }

        var bestEditor = matchingEditors
            .OrderByDescending(e => e.Priority)
            .FirstOrDefault();

        // Si encontramos una instancia coincidente, aseguramos traer su ventana al frente
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

        // 1. Si Visual Studio está abierto con el proyecto/solución
        if (bestEditor.EditorType == "devenv" && bestEditor.Priority >= 80)
        {
            string devenvPath = GetVisualStudioPath(bestEditor.Process);
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = devenvPath,
                    Arguments = $"/Edit \"{fullPath}\" /Command \"Edit.GoTo {lineNum}\"",
                    UseShellExecute = true
                });
                return;
            }
            catch { }
        }

        // 2. Si Antigravity está en ejecución
        if (bestEditor.EditorType == "antigravity")
        {
            var agyCli = FindAntigravityCli();
            if (agyCli != null && LaunchVsCodeStyleCli(agyCli, projectPath, normalizedGitPath, fullPath, lineNum, isWsl))
            {
                return;
            }
        }

        // 3. Si Cursor está en ejecución
        if (bestEditor.EditorType == "cursor")
        {
            if (LaunchVsCodeStyleCli("cursor", projectPath, normalizedGitPath, fullPath, lineNum, isWsl))
            {
                return;
            }
        }

        // 4. Si Windsurf está en ejecución
        if (bestEditor.EditorType == "windsurf")
        {
            if (LaunchVsCodeStyleCli("windsurf", projectPath, normalizedGitPath, fullPath, lineNum, isWsl))
            {
                return;
            }
        }

        // 5. Si Rider está en ejecución
        if (bestEditor.EditorType == "rider")
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "rider64",
                    Arguments = $"--line {lineNum} \"{fullPath}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true
                });
                return;
            }
            catch { }
        }

        // 6. Si VS Code está en ejecución o como fallback general (--reuse-window para no abrir nueva instancia)
        if (bestEditor.EditorType == "vscode" || bestEditor.Process != null)
        {
            if (LaunchVsCodeStyleCli("code", projectPath, normalizedGitPath, fullPath, lineNum, isWsl))
            {
                return;
            }
        }

        // 7. Fallback: intentar Antigravity CLI o VS Code CLI
        var fallbackAgy = FindAntigravityCli();
        if (fallbackAgy != null && LaunchVsCodeStyleCli(fallbackAgy, projectPath, normalizedGitPath, fullPath, lineNum, isWsl))
        {
            return;
        }

        LaunchVsCodeStyleCli("code", projectPath, normalizedGitPath, fullPath, lineNum, isWsl);
    }

    private static string? FindAntigravityCli()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates = {
            Path.Combine(localAppData, "Programs", "Antigravity IDE", "bin", "antigravity-ide.cmd"),
            Path.Combine(localAppData, "Programs", "Antigravity", "bin", "antigravity.cmd"),
            Path.Combine(localAppData, "Programs", "Antigravity", "antigravity.cmd"),
            Path.Combine(localAppData, "Programs", "Antigravity IDE", "antigravity-ide.cmd")
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }

    private static bool LaunchVsCodeStyleCli(string cliExe, string projectPath, string normalizedGitPath, string fullPath, int lineNum, bool isWsl)
    {
        try
        {
            string arguments;
            if (isWsl)
            {
                var parts = projectPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string distro = parts[1];
                    string linuxPath = "/" + string.Join("/", parts.Skip(2)).Replace("\\", "/");
                    string fileLinuxPath = (linuxPath.TrimEnd('/') + "/" + normalizedGitPath.TrimStart('/')).Replace("//", "/");
                    string remoteUri = $"vscode-remote://wsl+{distro}{EscapeVsCodeRemotePath(fileLinuxPath)}";
                    arguments = $"--reuse-window --goto \"{remoteUri}:{lineNum}\"";
                }
                else
                {
                    arguments = $"--reuse-window --goto \"{fullPath}:{lineNum}\"";
                }
            }
            else
            {
                arguments = $"--reuse-window --goto \"{fullPath}:{lineNum}\"";
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = cliExe,
                Arguments = arguments,
                UseShellExecute = true,
                CreateNoWindow = true
            };

            System.Diagnostics.Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsDotNetProject(string projectPath, string filePath)
    {
        if (!string.IsNullOrEmpty(filePath))
        {
            string ext = Path.GetExtension(filePath).ToLower();
            string[] dotNetExtensions = { ".cs", ".csproj", ".sln", ".vb", ".vbproj", ".xaml", ".axaml", ".razor", ".resx", ".config" };
            if (dotNetExtensions.Contains(ext)) return true;
        }
        if (string.IsNullOrEmpty(projectPath) || !Directory.Exists(projectPath)) return false;
        if (Directory.EnumerateFiles(projectPath, "*.sln", SearchOption.TopDirectoryOnly).Any() ||
            Directory.EnumerateFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly).Any()) return true;
        try
        {
            return Directory.EnumerateFiles(projectPath, "*.csproj", SearchOption.AllDirectories).Any() ||
                   Directory.EnumerateFiles(projectPath, "*.sln", SearchOption.AllDirectories).Any();
        }
        catch { return false; }
    }

    private string GetVisualStudioPath(System.Diagnostics.Process? activeEditor)
    {
        if (activeEditor != null && activeEditor.ProcessName.Equals("devenv", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string? exePath = activeEditor.MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath)) return exePath;
            }
            catch { }
        }

        string[] commonPaths = {
            @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe",
            @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\Common7\IDE\devenv.exe",
            @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\Common7\IDE\devenv.exe",
            @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\Common7\IDE\devenv.exe"
        };
        foreach (var path in commonPaths) { if (File.Exists(path)) return path; }
        return "devenv.exe";
    }

    private void ToggleStashView_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.IsStashViewVisible = !_viewModel.IsStashViewVisible;
        }
    }

    private string GetPathFromMenuItem(object sender)
    {
        if (sender is MenuItem menuItem)
        {
            if (menuItem.CommandParameter is string path) return path;
            if (menuItem.CommandParameter is ChangeItemViewModel changeItem)
            {
                if (!string.IsNullOrEmpty(_viewModel?.ProjectPath))
                {
                    return GetAbsoluteChangePath(_viewModel.ProjectPath, changeItem.FilePath);
                }
                return changeItem.FilePath;
            }
        }
        return null;
    }

    private static string GetAbsoluteChangePath(string projectPath, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return projectPath;

        if (Path.IsPathRooted(filePath))
            return Path.GetFullPath(filePath);

        var normalizedRelativePath = filePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.GetFullPath(Path.Combine(projectPath, normalizedRelativePath));
    }

    private static string ToGitStylePath(string filePath)
    {
        return string.IsNullOrWhiteSpace(filePath)
            ? string.Empty
            : filePath.Replace('\\', '/');
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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
