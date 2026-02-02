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
        if (sender is FrameworkElement fe && fe.DataContext is ChangeItemViewModel item)
        {
            _viewModel?.StashSelectedCommand.Execute(item);
        }
        else
        {
            _viewModel?.StashSelectedCommand.Execute(null);
        }
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

    private void DiscardChangesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is ChangeItemViewModel item)
        {
            _viewModel?.DiscardCommand.Execute(item);
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
    private void DiffLineMenu_OpenFile_Click(object sender, RoutedEventArgs e) { }

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




