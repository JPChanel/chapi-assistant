using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Chapi.Presentation.Features.History.ViewModels;

namespace Chapi.Presentation.Features.History.Views;

public partial class HistoryView : UserControl
{
    private HistoryViewModel _viewModel => DataContext as HistoryViewModel;

    public HistoryView()
    {
        InitializeComponent();
    }

    private void History_ContextMenu_Opening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CommitItemViewModel commit)
        {
            if (fe.ContextMenu != null)
            {
                var undoItem = fe.ContextMenu.Items[0] as MenuItem;
                if (undoItem != null && undoItem.Name == "ResetSoftMenuItem")
                {
                    undoItem.IsEnabled = !commit.IsSynced;
                    undoItem.ToolTip = undoItem.IsEnabled ? "Deshacer este commit manteniendo cambios" : "No se puede deshacer un commit ya subido al servidor";
                }
            }
        }
    }

    private void History_ResetSoft_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.CommandParameter is CommitItemViewModel commit)
        {
            if (commit.IsSynced)
            {
                MessageBox.Show("No se puede deshacer un commit que ya ha sido subido al servidor.", "Acción no permitida", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _viewModel?.ResetSoftCommand.Execute(commit);
            MainWindow.Instance?.Dispatcher.InvokeAsync(async () => await MainWindow.Instance.UpdateProjectStatusesAsync());
        }
    }

    private void History_CreateBranch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.CommandParameter is string hash)
        {
            _viewModel?.CreateBranchCommand.Execute(hash);
        }
    }

    private void History_CreateTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.CommandParameter is string hash)
        {
            _viewModel?.CreateTagCommand.Execute(hash);
        }
    }

    private void ProjectMenuItem_OpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        string path = GetPathFromMenuItem(sender);
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            path = path.Replace('/', '\\');

            if (File.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                string dir = Path.GetDirectoryName(path);
                if (Directory.Exists(dir))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true
                    });
                }
            }
        }
        catch { }
    }

    private void ProjectMenuItem_OpenAntigravity_Click(object sender, RoutedEventArgs e)
    {
        string path = GetPathFromMenuItem(sender);
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] agyCandidates = {
                Path.Combine(localAppData, "Programs", "Antigravity IDE", "bin", "antigravity-ide.cmd"),
                Path.Combine(localAppData, "Programs", "Antigravity", "bin", "antigravity.cmd"),
                Path.Combine(localAppData, "Programs", "Antigravity", "antigravity.cmd"),
                Path.Combine(localAppData, "Programs", "Antigravity IDE", "antigravity-ide.cmd")
            };
            string? agyCli = agyCandidates.FirstOrDefault(File.Exists);

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
        catch { }
    }

    private void ProjectMenuItem_OpenVisualStudio_Click(object sender, RoutedEventArgs e)
    {
        string path = GetPathFromMenuItem(sender);
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void HistoryFiles_CopyPath_Click(object sender, RoutedEventArgs e)
    {
        string path = GetPathFromMenuItem(sender);
        if (!string.IsNullOrEmpty(path)) Clipboard.SetText(path);
    }

    private void HistoryFiles_CopyRelativePath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is string rel) Clipboard.SetText(rel);
    }

    private async void ProjectMenuItem_OpenGitHub_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null || _viewModel.SelectedCommit == null) return;
        if (sender is MenuItem mi && mi.CommandParameter is string relativePath)
        {
            try
            {
                var gitRepo = App.ServiceProvider.GetRequiredService<Chapi.Domain.Interfaces.IGitRepository>();
                var remoteUrl = await gitRepo.GetRemoteUrlAsync(_viewModel.ProjectPath, "origin");

                if (string.IsNullOrEmpty(remoteUrl)) return;

                remoteUrl = remoteUrl.Replace(".git", "");
                if (remoteUrl.StartsWith("git@"))
                {
                    remoteUrl = remoteUrl.Replace(":", "/");
                    remoteUrl = remoteUrl.Replace("git@", "https://");
                }

                string url;
                string hash = _viewModel.SelectedCommit.Hash;

                if (remoteUrl.Contains("github.com"))
                {
                    url = $"{remoteUrl}/blob/{hash}/{relativePath.Replace("\\", "/")}";
                }
                else if (remoteUrl.Contains("gitlab.com"))
                {
                    url = $"{remoteUrl}/-/blob/{hash}/{relativePath.Replace("\\", "/")}";
                }
                else
                {
                    url = $"{remoteUrl}/commit/{hash}";
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    private string GetPathFromMenuItem(object sender)
    {
        if (sender is MenuItem mi)
        {
            if (mi.CommandParameter is string path)
            {
                if (System.IO.Path.IsPathRooted(path)) return path;
                if (!string.IsNullOrEmpty(_viewModel?.ProjectPath))
                    return System.IO.Path.Combine(_viewModel.ProjectPath, path);
            }
        }
        return null;
    }
}
