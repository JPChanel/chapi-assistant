using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Chapi.Infrastructure.Persistence.Settings;
using MaterialDesignThemes.Wpf;

namespace Chapi.Presentation.Shared.Dialogs.Views;

public partial class ManageProjectGroupsDialog : UserControl
{
    private Point _dragStartPoint;
    private ProjectCardModel? _draggedItem;
    private ObservableCollection<GroupCardModel> _groups = new();

    public ManageProjectGroupsDialog()
    {
        InitializeComponent();
        Loaded += ManageProjectGroupsDialog_Loaded;
    }

    private void ManageProjectGroupsDialog_Loaded(object sender, RoutedEventArgs e)
    {
        ReloadData();
    }

    public void ReloadData()
    {
        var config = ProjectSettings.LoadData();
        var groups = config.Groups.OrderBy(g => g.Order).ToList();
        var mappings = config.Mappings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var projects = config.Projects;

        _groups.Clear();

        // 1. Grupos personalizados
        foreach (var g in groups)
        {
            var grpModel = new GroupCardModel
            {
                Id = g.Id,
                Name = g.Name,
                IsDefault = false,
                IconKind = PackIconKind.FolderOutline
            };
            _groups.Add(grpModel);
        }

        // 2. Grupo "Sin Agrupar" (siempre presente)
        var unassignedGroup = new GroupCardModel
        {
            Id = null,
            Name = "Sin Agrupar",
            IsDefault = true,
            IconKind = PackIconKind.FolderOpenOutline
        };
        _groups.Add(unassignedGroup);

        // 3. Poblar proyectos
        var groupsById = _groups.Where(g => g.Id != null).ToDictionary(g => g.Id!, g => g);
        foreach (var p in projects)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var dirName = Path.GetFileName(p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(dirName)) dirName = p;

            var projModel = new ProjectCardModel
            {
                FullPath = p,
                Name = dirName
            };

            if (mappings.TryGetValue(p, out var gid) && !string.IsNullOrEmpty(gid) && groupsById.TryGetValue(gid, out var targetGroup))
            {
                projModel.GroupId = gid;
                targetGroup.Projects.Add(projModel);
            }
            else
            {
                projModel.GroupId = null;
                unassignedGroup.Projects.Add(projModel);
            }
        }

        GroupsItemsControl.ItemsSource = _groups;
        ApplyFilter(TxtSearch?.Text);
    }

    private void TxtNewGroupName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CreateGroup();
        }
    }

    private void BtnCreateGroup_Click(object sender, RoutedEventArgs e)
    {
        CreateGroup();
    }

    private void CreateGroup()
    {
        var name = TxtNewGroupName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        var newGrp = ProjectSettings.AddGroup(name);
        TxtNewGroupName.Text = string.Empty;

        // Insert before "Sin Agrupar"
        var grpModel = new GroupCardModel
        {
            Id = newGrp.Id,
            Name = newGrp.Name,
            IsDefault = false,
            IconKind = PackIconKind.FolderOutline
        };

        var unassignedIndex = _groups.IndexOf(_groups.FirstOrDefault(g => g.IsDefault)!);
        if (unassignedIndex >= 0)
        {
            _groups.Insert(unassignedIndex, grpModel);
        }
        else
        {
            _groups.Add(grpModel);
        }
    }

    private async void BtnRenameGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is GroupCardModel group && !group.IsDefault)
        {
            var (confirmed, newName) = await DialogService.ShowInputDialog("Renombrar Grupo", "Ingresa el nuevo nombre para el grupo:", group.Name);
            if (confirmed && !string.IsNullOrWhiteSpace(newName))
            {
                group.Name = newName.Trim();
                ProjectSettings.UpdateGroup(group.Id!, newName.Trim());
            }
        }
    }

    private async void BtnDeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is GroupCardModel group && !group.IsDefault)
        {
            var confirm = await DialogService.ShowConfirmDialog(
                "Eliminar Grupo",
                $"¿Seguro que deseas eliminar el grupo '{group.Name}'?\n\nLos proyectos asociados no se eliminarán; pasarán a 'Sin Agrupar'.",
                DialogVariant.Warning);

            if (confirm)
            {
                var unassigned = _groups.FirstOrDefault(g => g.IsDefault);
                if (unassigned != null)
                {
                    var projectsToMove = group.Projects.ToList();
                    foreach (var p in projectsToMove)
                    {
                        p.GroupId = null;
                        unassigned.Projects.Add(p);
                    }
                }

                _groups.Remove(group);
                ProjectSettings.DeleteGroup(group.Id!);
                ApplyFilter(TxtSearch?.Text);
            }
        }
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter(TxtSearch.Text);
    }

    private void ApplyFilter(string? query)
    {
        var filter = (query ?? string.Empty).Trim();
        foreach (var group in _groups)
        {
            foreach (var proj in group.Projects)
            {
                proj.IsVisible = string.IsNullOrEmpty(filter) ||
                                 proj.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                 proj.FullPath.Contains(filter, StringComparison.OrdinalIgnoreCase);
            }
            group.NotifyCountChanged();
        }
    }

    // --- Drag and Drop ---

    private void ProjectCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        if (sender is FrameworkElement elem && elem.Tag is ProjectCardModel proj)
        {
            _draggedItem = proj;
        }
    }

    private void ProjectCard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _draggedItem != null)
        {
            var currentPos = e.GetPosition(null);
            var diff = _dragStartPoint - currentPos;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (sender is FrameworkElement elem)
                {
                    DragDrop.DoDragDrop(elem, _draggedItem, DragDropEffects.Move);
                    _draggedItem = null;
                }
            }
        }
    }

    private void GroupBorder_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is Border border && border.Tag is GroupCardModel group)
        {
            group.IsDragOver = true;
        }
    }

    private void GroupBorder_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(ProjectCardModel)))
        {
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void GroupBorder_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border && border.Tag is GroupCardModel group)
        {
            group.IsDragOver = false;
        }
    }

    private void GroupBorder_Drop(object sender, DragEventArgs e)
    {
        if (sender is Border border && border.Tag is GroupCardModel targetGroup)
        {
            targetGroup.IsDragOver = false;

            if (e.Data.GetData(typeof(ProjectCardModel)) is ProjectCardModel project)
            {
                // Find source group
                var sourceGroup = _groups.FirstOrDefault(g => g.Projects.Contains(project));
                if (sourceGroup != null && sourceGroup != targetGroup)
                {
                    sourceGroup.Projects.Remove(project);
                    project.GroupId = targetGroup.Id;
                    targetGroup.Projects.Add(project);

                    ProjectSettings.SetProjectGroup(project.FullPath, targetGroup.Id);

                    sourceGroup.NotifyCountChanged();
                    targetGroup.NotifyCountChanged();
                    ApplyFilter(TxtSearch?.Text);
                }
            }
        }
    }
}

public class GroupCardModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string? Id { get; set; }

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(nameof(Name)); }
    }

    public bool IsDefault { get; set; }
    public PackIconKind IconKind { get; set; } = PackIconKind.FolderOutline;
    public ObservableCollection<ProjectCardModel> Projects { get; set; } = new();

    private bool _isDragOver;
    public bool IsDragOver
    {
        get => _isDragOver;
        set
        {
            _isDragOver = value;
            OnPropertyChanged(nameof(IsDragOver));
            OnPropertyChanged(nameof(BorderBrush));
            OnPropertyChanged(nameof(BorderThickness));
        }
    }

    public Brush BorderBrush => IsDragOver
        ? new SolidColorBrush(Color.FromRgb(33, 150, 243))
        : (Brush)System.Windows.Application.Current.FindResource("MaterialDesignDivider");

    public Thickness BorderThickness => IsDragOver ? new Thickness(2) : new Thickness(1);

    public bool HasNoProjects => Projects.Count == 0;

    public string VisibleCount
    {
        get
        {
            var visible = Projects.Count(p => p.IsVisible);
            return $"{visible} {(visible == 1 ? "proyecto" : "proyectos")}";
        }
    }

    public void NotifyCountChanged()
    {
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(HasNoProjects));
    }
}

public class ProjectCardModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string FullPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? GroupId { get; set; }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set { _isVisible = value; OnPropertyChanged(nameof(IsVisible)); }
    }
}