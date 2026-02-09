using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using System.IO;
using System.Windows.Data;
using System.Globalization;
using Chapi.Presentation.ViewModels;
using Chapi.Domain.Entities;

using Chapi.Infrastructure.Git;
using Chapi.Application.UseCases;
using Chapi.Infrastructure.Services;
using Chapi.Presentation.Views.Dialogs;
using DiffPlex.DiffBuilder.Model;

namespace Chapi.Presentation.Views.Tabs;

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
        _ = _viewModel?.LoadChangesAsync();
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

    private void ProjectMenuItem_OpenVSCode_Click(object sender, RoutedEventArgs e)
    {
        string path = GetPathFromMenuItem(sender);
        if (string.IsNullOrEmpty(path)) return;
        
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "code",
                Arguments = $"\"{path}\"",
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
            string searchDir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            var slnFile = Directory.GetFiles(searchDir, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();

            if (slnFile != null)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = slnFile,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al abrir Visual Studio: {ex.Message}");
        }
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
        var latestStash = _viewModel?.Stashes?.FirstOrDefault();
        if (latestStash != null)
        {
             _viewModel.PopStashCommand.Execute(latestStash);
        }
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
        if (_viewModel != null) _viewModel.IsStashViewVisible = false;
    }

    private void StashFilesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // El ViewModel ya maneja el cambio via binding SelectedStashedFile en XAML
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
        string fullPath = Path.Combine(projectPath, filePath);
        
        bool isDotNet = IsDotNetProject(projectPath, filePath);

        if (isDotNet)
        {
            try
            {
                var slnFile = Directory.EnumerateFiles(projectPath, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (slnFile != null)
                {
                    projectName = Path.GetFileNameWithoutExtension(slnFile);
                }
            }
            catch { /* Fallback al nombre del directorio */ }
        }

        var processes = System.Diagnostics.Process.GetProcesses();

        var activeEditor = processes
            .Where(p => 
            {
                try
                {
                    bool isMajorEditor = p.ProcessName.Contains("Antigravity", StringComparison.OrdinalIgnoreCase) || 
                                         p.ProcessName.Equals("Code", StringComparison.OrdinalIgnoreCase) || 
                                         p.ProcessName.Equals("devenv", StringComparison.OrdinalIgnoreCase);
                    
                    if (!isMajorEditor || string.IsNullOrEmpty(p.MainWindowTitle)) return false;

                    if (p.ProcessName.Equals("devenv", StringComparison.OrdinalIgnoreCase))
                    {
                        return p.MainWindowTitle.Contains(projectName, StringComparison.OrdinalIgnoreCase);
                    }

                    return p.MainWindowTitle.Contains(projectName, StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            })
            .OrderBy(p => 
            {
                if (isDotNet)
                {
                    if (p.ProcessName.Equals("devenv", StringComparison.OrdinalIgnoreCase)) return 0;
                    if (p.ProcessName.Contains("Antigravity")) return 1;
                    return 2; // Code
                }
                else
                {
                    if (p.ProcessName.Contains("Antigravity")) return 0;
                    if (p.ProcessName.Equals("Code", StringComparison.OrdinalIgnoreCase)) return 1;
                    return 2; 
                }
            })
            .FirstOrDefault();

        bool isWsl = projectPath.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase) || 
                     projectPath.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var agyExePath = Path.Combine(localAppData, "Programs", "Antigravity", "Antigravity.exe");

        if (isDotNet && (activeEditor == null || activeEditor.ProcessName.Equals("devenv", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                string? slnFile = null;
                string? currentDir = Path.GetDirectoryName(fullPath);
                
                while (!string.IsNullOrEmpty(currentDir) && currentDir.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                {
                    slnFile = Directory.EnumerateFiles(currentDir, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
                    if (slnFile != null) break;
                    currentDir = Path.GetDirectoryName(currentDir);
                }

                if (slnFile == null)
                    slnFile = Directory.EnumerateFiles(projectPath, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();

                if (slnFile == null)
                    slnFile = Directory.EnumerateFiles(projectPath, "*.sln", SearchOption.AllDirectories).FirstOrDefault();
                
                if (slnFile != null)
                {
                    string devenvPath = GetVisualStudioPath(activeEditor);

                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = devenvPath,
                        Arguments = $"/Edit \"{fullPath}\" /Command \"Edit.GoTo {lineNum}\"",
                        UseShellExecute = true
                    });
                    return;
                }
            }
            catch { }
        }

        if (activeEditor != null && activeEditor.ProcessName.Equals("devenv", StringComparison.OrdinalIgnoreCase))
        {
            string devenvPath = GetVisualStudioPath(activeEditor);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = devenvPath,
                Arguments = $"/Edit \"{fullPath}\" /Command \"Edit.GoTo {lineNum}\"",
                UseShellExecute = true
            });
        }
        else
        {
            string editorExe = "code";
            if (activeEditor?.ProcessName.Contains("Antigravity", StringComparison.OrdinalIgnoreCase) == true || 
                (activeEditor == null && File.Exists(agyExePath)))
            {
                editorExe = agyExePath;
            }

            string arguments = "";
            bool isCodeEditor = editorExe.Equals("code", StringComparison.OrdinalIgnoreCase);

            if (isWsl && isCodeEditor)
            {
                var parts = projectPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string distro = parts[1];
                    string linuxPath = "/" + string.Join("/", parts.Skip(2)).Replace("\\", "/");
                    string fileLinuxPath = (linuxPath.TrimEnd('/') + "/" + filePath.Replace("\\", "/")).Replace("//", "/");
                    string remoteUri = $"vscode-remote://wsl+{distro}{fileLinuxPath}";
                    arguments = $"--reuse-window --goto \"{remoteUri}:{lineNum}\"";
                }
            }
            else
            {
                arguments = $"--reuse-window --goto \"{fullPath}:{lineNum}\"";
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = editorExe,
                Arguments = arguments,
                UseShellExecute = true,
                CreateNoWindow = true
            });
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
                    return Path.Combine(_viewModel.ProjectPath, changeItem.FilePath);
                }
                return changeItem.FilePath;
            }
        }
        return null;
    }
}




