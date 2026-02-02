using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Chapi.Presentation.ViewModels;

namespace Chapi.Presentation.Views.Tabs;

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
                var undoItem = fe.ContextMenu.Items[0] as MenuItem; // Asumimos que es el primero por diseño
                if (undoItem != null && undoItem.Name == "ResetSoftMenuItem")
                {
                    // Solo permitir Undo si el commit NO está sincronizado (es local)
                    // Y es el último commit (index 0) - simplificación para evitar conflictos
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
            
            // Actualizar estado global (flechitas y botón push)
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
        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    private void ProjectMenuItem_OpenVSCode_Click(object sender, RoutedEventArgs e)
    {
        string path = GetPathFromMenuItem(sender);
        if (string.IsNullOrEmpty(path)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "code",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
    }

    private void ProjectMenuItem_OpenVisualStudio_Click(object sender, RoutedEventArgs e)
    {
        string path = GetPathFromMenuItem(sender);
        if (string.IsNullOrEmpty(path)) return;
        // Logica similar a ChangesView
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

    private void ProjectMenuItem_OpenGitHub_Click(object sender, RoutedEventArgs e)
    {
        // Logica de apertura en web
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


