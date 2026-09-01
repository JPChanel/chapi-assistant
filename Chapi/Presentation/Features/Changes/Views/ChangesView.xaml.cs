using Chapi.Domain.Entities;
using Chapi.Infrastructure.Services;
using Chapi.Presentation.Shared.Dialogs.Views;
using DiffPlex.DiffBuilder.Model;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Chapi.Presentation.Features.Changes.ViewModels;
using Chapi.Presentation.Features.Projects.Services;

namespace Chapi.Presentation.Features.Changes.Views;

public partial class ChangesView : UserControl
{
    private ChangesViewModel _viewModel => DataContext as ChangesViewModel;
    private ProjectToolLauncher? _lazyProjectToolLauncher;
    private ProjectToolLauncher _projectToolLauncher => _lazyProjectToolLauncher ??= (App.ServiceProvider?.GetService<ProjectToolLauncher>() ?? null!);

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
            StashListView.SelectedItem = null;
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
        if (!string.IsNullOrEmpty(path)) _projectToolLauncher.OpenAntigravity(path);
    }

    private void ProjectMenuItem_OpenVSCode_Click(object sender, RoutedEventArgs e)
    {
        string path = GetPathFromMenuItem(sender);
        if (!string.IsNullOrEmpty(path)) _projectToolLauncher.OpenVSCode(path);
    }

    private void ProjectMenuItem_OpenCursor_Click(object sender, RoutedEventArgs e)
    {
        string path = GetPathFromMenuItem(sender);
        if (!string.IsNullOrEmpty(path)) _projectToolLauncher.OpenCursor(path);
    }

    private void ProjectMenuItem_OpenWindsurf_Click(object sender, RoutedEventArgs e)
    {
        string path = GetPathFromMenuItem(sender);
        if (!string.IsNullOrEmpty(path)) _projectToolLauncher.OpenWindsurf(path);
    }

    private void ProjectMenuItem_OpenVisualStudio_Click(object sender, RoutedEventArgs e)
    {
        string path = GetPathFromMenuItem(sender);
        if (!string.IsNullOrEmpty(path)) _projectToolLauncher.OpenVisualStudio(path);
    }

    private void ProjectMenuItem_OpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        string path = GetPathFromMenuItem(sender);
        if (!string.IsNullOrEmpty(path)) _projectToolLauncher.OpenExplorer(path);
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

                _projectToolLauncher.SmartOpen(projectPath, filePath, lineNum);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo abrir el editor: {ex.Message}");
            }
        }
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
}
