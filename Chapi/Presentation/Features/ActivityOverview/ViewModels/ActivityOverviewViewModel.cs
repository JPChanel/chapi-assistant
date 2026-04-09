using app_desktop_base.Models.EstDataTable;
using Chapi.Application.Interfaces.Workspace;
using Chapi.Domain.Entities.Workspace;
using Chapi.Domain.Enums;
using Chapi.Presentation.Features.ActivityOverview.Models;
using Chapi.Presentation.Features.Projects.Services;
using Chapi.Presentation.Shared.Mvvm;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Chapi.Presentation.Features.ActivityOverview.ViewModels;

public sealed class ActivityOverviewViewModel : ViewModelBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly ProjectToolLauncher _projectToolLauncher;
    private readonly List<WorkspaceActivityRecord> _allRecords = [];
    private readonly ObservableCollection<ActivityOverviewItem> _activities = [];
    private readonly ObservableCollection<EstDataTableToolbarAction> _toolbarActions = [];
    private ActivityGroupingOption? _selectedGrouping;
    private ActivityOverviewItem? _selectedActivity;
    private bool _isLoading;
    private string _statusMessage = "Cargando actividades...";
    private EstDataTableDefinition<ActivityOverviewItem> _tableDefinition = new();
    private string _groupingToolTip = "Agrupar: sin agrupacion";

    public ActivityOverviewViewModel(IWorkspaceService workspaceService, ProjectToolLauncher projectToolLauncher)
    {
        _workspaceService = workspaceService;
        _projectToolLauncher = projectToolLauncher;

        GroupingOptions = new ReadOnlyCollection<ActivityGroupingOption>(new List<ActivityGroupingOption>
        {
            new() { Label = "Estado", PropertyName = nameof(ActivityOverviewItem.GroupStatus) },
            new() { Label = "Proyecto", PropertyName = nameof(ActivityOverviewItem.GroupProject) },
            new() { Label = "Responsable", PropertyName = nameof(ActivityOverviewItem.GroupOwner) },
            new() { Label = "Mes", PropertyName = nameof(ActivityOverviewItem.GroupMonth) },
            new() { Label = "Dia", PropertyName = nameof(ActivityOverviewItem.GroupDay) }
        });

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        OpenSelectedProjectCommand = new RelayCommand(OpenSelectedProject, CanOpenSelectedProject);
        OpenActivityProjectCommand = new RelayCommand<object?>(OpenActivityProject);
        OpenActivityFolderCommand = new RelayCommand<object?>(OpenActivityFolder);
        SetGroupingCommand = new RelayCommand<object?>(SetGrouping);
        ClearGroupingCommand = new RelayCommand(ClearGrouping);

        SelectedGrouping = null;
        UpdateToolbarActions();
        TableDefinition = BuildTableDefinition();
    }

    public ObservableCollection<ActivityOverviewItem> Activities => _activities;
    public ObservableCollection<EstDataTableToolbarAction> ToolbarActions => _toolbarActions;
    public ReadOnlyCollection<ActivityGroupingOption> GroupingOptions { get; }

    public ActivityGroupingOption? SelectedGrouping
    {
        get => _selectedGrouping;
        set
        {
            if (SetProperty(ref _selectedGrouping, value))
            {
                UpdateGroupingState();
                UpdateToolbarActions();
                RebuildVisibleActivities();
            }
        }
    }

    public ActivityOverviewItem? SelectedActivity
    {
        get => _selectedActivity;
        set
        {
            if (SetProperty(ref _selectedActivity, value))
            {
                (OpenSelectedProjectCommand as RelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public EstDataTableDefinition<ActivityOverviewItem> TableDefinition
    {
        get => _tableDefinition;
        private set => SetProperty(ref _tableDefinition, value);
    }

    public bool IsUngrouped => SelectedGrouping is null;

    public string GroupingToolTip
    {
        get => _groupingToolTip;
        private set => SetProperty(ref _groupingToolTip, value);
    }

    public ActivityGroupingOption? GroupByStatusOption => GroupingOptions.ElementAtOrDefault(0);
    public ActivityGroupingOption? GroupByProjectOption => GroupingOptions.ElementAtOrDefault(1);
    public ActivityGroupingOption? GroupByOwnerOption => GroupingOptions.ElementAtOrDefault(2);
    public ActivityGroupingOption? GroupByMonthOption => GroupingOptions.ElementAtOrDefault(3);
    public ActivityGroupingOption? GroupByDayOption => GroupingOptions.ElementAtOrDefault(4);

    public ICommand RefreshCommand { get; }
    public ICommand OpenSelectedProjectCommand { get; }
    public ICommand OpenActivityProjectCommand { get; }
    public ICommand OpenActivityFolderCommand { get; }
    public ICommand SetGroupingCommand { get; }
    public ICommand ClearGroupingCommand { get; }
    public Action<string>? NavigateToProject { get; set; }

    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Cargando actividades...";

            var result = await _workspaceService.LoadActivityRecordsAsync();
            if (!result.IsSuccess)
            {
                StatusMessage = string.IsNullOrWhiteSpace(result.Error)
                    ? "No se pudieron cargar las actividades."
                    : result.Error;
                return;
            }

            _allRecords.Clear();
            _allRecords.AddRange(result.Data!);
            RebuildVisibleActivities();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RebuildVisibleActivities()
    {
        _activities.Clear();

        var records = _allRecords
            .OrderByDescending(record => record.UpdatedAt)
            .ThenBy(record => record.ProjectName)
            .ThenBy(record => record.Title)
            .ToList();

        if (records.Count == 0)
        {
            StatusMessage = "No hay actividades registradas todavia.";
            return;
        }

        if (SelectedGrouping is null)
        {
            foreach (var record in records)
            {
                _activities.Add(Map(record));
            }

            StatusMessage = $"{records.Count} actividades sin agrupacion.";
            return;
        }

        foreach (var group in records.GroupBy(ResolveGroupValue))
        {
            _activities.Add(new ActivityOverviewItem
            {
                TaskId = Guid.NewGuid(),
                IsGroupHeader = true,
                GroupLabel = $"{SelectedGrouping?.Label ?? "Grupo"}: {group.Key}",
                SummaryText = $"Recuento: {group.Count()}",
                StatusLabel = group.FirstOrDefault()?.Status switch
                {
                    WorkspaceTaskStatus.NoIniciada => "No iniciada",
                    WorkspaceTaskStatus.Completada => "Completada",
                    _ => "En curso"
                }
            });

            foreach (var record in group)
            {
                _activities.Add(Map(record));
            }
        }

        StatusMessage = $"{records.Count} actividades agrupadas por {SelectedGrouping?.Label?.ToLowerInvariant() ?? "estado"}.";
    }

    private ActivityOverviewItem Map(WorkspaceActivityRecord record)
    {
        var statusLabel = record.Status switch
        {
            WorkspaceTaskStatus.NoIniciada => "No iniciada",
            WorkspaceTaskStatus.Completada => "Completada",
            _ => "En curso"
        };

        var projectName = string.IsNullOrWhiteSpace(record.ProjectName) ? "(Proyecto sin nombre)" : record.ProjectName;
        var owner = string.IsNullOrWhiteSpace(record.Owner) ? "(Sin usuario Git)" : record.Owner;
        var priorityLabel = record.Priority switch
        {
            TaskPriority.Alta => "Alta",
            TaskPriority.Baja => "Baja",
            _ => "Media"
        };

        return new ActivityOverviewItem
        {
            TaskId = record.TaskId,
            GroupLabel = ResolveGroupValue(record),
            ProjectPath = record.ProjectPath,
            ProjectName = projectName,
            Title = string.IsNullOrWhiteSpace(record.Title) ? "(Actividad sin titulo)" : record.Title,
            Owner = owner,
            Priority = record.Priority,
            PriorityLabel = priorityLabel,
            Status = record.Status,
            StatusLabel = statusLabel,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            CompletedAt = record.CompletedAt,
            GroupStatus = $"Estado: {statusLabel}",
            GroupProject = $"Proyecto: {projectName}",
            GroupOwner = $"Responsable: {owner}",
            GroupMonth = $"Mes: {record.UpdatedAt:yyyy-MM}",
            GroupDay = $"Fecha: {record.UpdatedAt:yyyy-MM-dd}"
        };
    }

    private string ResolveGroupValue(WorkspaceActivityRecord record)
    {
        var statusLabel = record.Status switch
        {
            WorkspaceTaskStatus.NoIniciada => "No iniciada",
            WorkspaceTaskStatus.Completada => "Completada",
            _ => "En curso"
        };

        var projectName = string.IsNullOrWhiteSpace(record.ProjectName) ? "(Proyecto sin nombre)" : record.ProjectName;
        var owner = string.IsNullOrWhiteSpace(record.Owner) ? "(Sin usuario Git)" : record.Owner;

        return SelectedGrouping?.PropertyName switch
        {
            nameof(ActivityOverviewItem.GroupProject) => projectName,
            nameof(ActivityOverviewItem.GroupOwner) => owner,
            nameof(ActivityOverviewItem.GroupMonth) => record.UpdatedAt.ToString("yyyy-MM"),
            nameof(ActivityOverviewItem.GroupDay) => record.UpdatedAt.ToString("yyyy-MM-dd"),
            _ => statusLabel
        };
    }

    private EstDataTableDefinition<ActivityOverviewItem> BuildTableDefinition()
    {
        return new EstDataTableDefinition<ActivityOverviewItem>
        {
            Columns = new ObservableCollection<EstDataColumnDefinition>
            {
                new()
                {
                    Header = "Actividad",
                    ColumnKey = "title",
                    CellVariant = EstDataCellVariant.Filled,
                    ValueSelector = item => item is ActivityOverviewItem activity
                        ? activity.IsGroupHeader ? activity.GroupLabel : activity.Title
                        : string.Empty,
                    TextSelector = item => item is ActivityOverviewItem activity
                        ? activity.IsGroupHeader ? activity.GroupLabel : activity.Title
                        : string.Empty,
                    MinWidth = 320,
                    Priority = 0,
                    PaddingSelector = item => item is ActivityOverviewItem activity && activity.IsGroupHeader
                        ? new Thickness(8, 6, 8, 6)
                        : new Thickness(0),
                    CornerRadiusSelector = item => item is ActivityOverviewItem activity && activity.IsGroupHeader
                        ? new CornerRadius(6)
                        : new CornerRadius(0),
                    BackgroundSelector = ResolvePrimaryCellBackground,
                    ForegroundSelector = ResolvePrimaryCellForeground,
                    FontWeightSelector = item => item is ActivityOverviewItem activity && activity.IsGroupHeader
                        ? FontWeights.Bold
                        : FontWeights.Normal
                },
                new()
                {
                    Header = "Estado",
                    ColumnKey = "status",
                    CellVariant = EstDataCellVariant.Outline,
                    ValueSelector = item => item is ActivityOverviewItem activity
                        ? activity.IsGroupHeader ? activity.SummaryText : activity.StatusLabel
                        : string.Empty,
                    TextSelector = item => item is ActivityOverviewItem activity
                        ? activity.IsGroupHeader ? activity.SummaryText : activity.StatusLabel
                        : string.Empty,
                    IconSelector = ResolveStatusIcon,
                    Size = 170,
                    MinWidth = 140,
                    Priority = 1,
                    PaddingSelector = item => item is ActivityOverviewItem activity && activity.IsGroupHeader
                        ? new Thickness(0)
                        : new Thickness(10, 3, 10, 3),
                    CornerRadiusSelector = item => item is ActivityOverviewItem activity && activity.IsGroupHeader
                        ? new CornerRadius(0)
                        : new CornerRadius(10),
                    BorderBrushSelector = ResolveStatusBackground,
                    ForegroundSelector = ResolveStatusForeground
                },
                new()
                {
                    Header = "Prioridad",
                    ColumnKey = "priority",
                    CellVariant = EstDataCellVariant.Outline,
                    ValueSelector = item => item is ActivityOverviewItem activity && !activity.IsGroupHeader ? activity.PriorityLabel : string.Empty,
                    TextSelector = item => item is ActivityOverviewItem activity && !activity.IsGroupHeader ? activity.PriorityLabel : string.Empty,
                    IconSelector = ResolvePriorityIcon,
                    Size = 140,
                    MinWidth = 120,
                    Priority = 2,
                    Padding = new Thickness(10, 3, 10, 3),
                    CornerRadius = new CornerRadius(10),
                    BorderBrushSelector = ResolvePriorityBorderBrush,
                    ForegroundSelector = ResolvePriorityForeground
                },
                new()
                {
                    Header = "Propietario",
                    ColumnKey = "owner",
                    CellVariant = EstDataCellVariant.Filled,
                    ValueSelector = item => item is ActivityOverviewItem activity && !activity.IsGroupHeader ? activity.Owner : string.Empty,
                    TextSelector = item => item is ActivityOverviewItem activity && !activity.IsGroupHeader ? activity.Owner : string.Empty,
                    Size = 180,
                    MinWidth = 160,
                    Priority = 3,
                    Padding = new Thickness(10, 3, 10, 3),
                    CornerRadius = new CornerRadius(10),
                    BackgroundSelector = item => item is ActivityOverviewItem activity && !activity.IsGroupHeader
                        ? BrushFromHex("#F3F4F6")
                        : Brushes.Transparent,
                    ForegroundSelector = item => item is ActivityOverviewItem activity && !activity.IsGroupHeader
                        ? BrushFromHex("#4B5563")
                        : BrushFromHex("#4B5563")
                },
                new()
                {
                    Header = "Proyecto",
                    ColumnKey = "project",
                    CellVariantSelector = item => item is ActivityOverviewItem activity && !activity.IsGroupHeader
                        ? EstDataCellVariant.Link
                        : EstDataCellVariant.Text,
                    ValueSelector = item => item is ActivityOverviewItem activity && !activity.IsGroupHeader ? activity.ProjectName : string.Empty,
                    TextSelector = item => item is ActivityOverviewItem activity && !activity.IsGroupHeader ? activity.ProjectName : string.Empty,
                    ContextMenuSelector = item => item is ActivityOverviewItem activity && !activity.IsGroupHeader
                        ? BuildProjectContextMenu(activity)
                        : null,
                    ToolTipSelector = item => item is ActivityOverviewItem activity && !activity.IsGroupHeader
                        ? "Opciones del proyecto"
                        : null,
                    Size = 210,
                    MinWidth = 170,
                    Priority = 4
                },
                new()
                {
                    Header = "Fecha",
                    ColumnKey = "updated",
                    ValueSelector = item => item is ActivityOverviewItem activity && !activity.IsGroupHeader ? activity.UpdatedAt : null,
                    TextSelector = item => item is ActivityOverviewItem activity && !activity.IsGroupHeader
                        ? activity.UpdatedAt.ToString("yyyy-MM-dd HH:mm")
                        : string.Empty,
                    Size = 150,
                    MinWidth = 135,
                    Priority = 5
                },
                new()
                {
                    Header = "Creada",
                    ColumnKey = "created",
                    ValueSelector = item => item is ActivityOverviewItem activity && !activity.IsGroupHeader ? activity.CreatedAt : null,
                    TextSelector = item => item is ActivityOverviewItem activity && !activity.IsGroupHeader
                        ? activity.CreatedAt.ToString("yyyy-MM-dd")
                        : string.Empty,
                    Size = 120,
                    MinWidth = 110,
                    Priority = 6
                }
            }
        };
    }

    private void SetGrouping(object? parameter)
    {
        if (parameter is ActivityGroupingOption grouping)
        {
            SelectedGrouping = ReferenceEquals(SelectedGrouping, grouping)
                ? null
                : grouping;
        }
    }

    private void ClearGrouping()
    {
        SelectedGrouping = null;
    }

    private void OpenSelectedProject()
    {
        if (!CanOpenSelectedProject())
            return;

        NavigateToProject?.Invoke(SelectedActivity!.ProjectPath);
    }

    private bool CanOpenSelectedProject()
    {
        return SelectedActivity is { IsGroupHeader: false } activity
            && !string.IsNullOrWhiteSpace(activity.ProjectPath);
    }

    private void OpenActivityProject(object? parameter)
    {
        if (parameter is not ActivityOverviewItem activity || activity.IsGroupHeader || string.IsNullOrWhiteSpace(activity.ProjectPath))
            return;

        SelectedActivity = activity;
        NavigateToProject?.Invoke(activity.ProjectPath);
    }

    private void OpenActivityFolder(object? parameter)
    {
        if (parameter is not ActivityOverviewItem activity || activity.IsGroupHeader || string.IsNullOrWhiteSpace(activity.ProjectPath))
            return;

        _projectToolLauncher.OpenExplorer(activity.ProjectPath);
    }

    private static Brush ResolvePrimaryCellBackground(object? item)
    {
        return item is ActivityOverviewItem activity && activity.IsGroupHeader
            ? BrushFromHex("#E5E7EB")
            : Brushes.Transparent;
    }

    private static Brush ResolvePrimaryCellForeground(object? item)
    {
        return item is ActivityOverviewItem activity && activity.IsGroupHeader
            ? BrushFromHex("#111827")
            : BrushFromHex("#111827");
    }

    private static Brush ResolveStatusBackground(object? item)
    {
        if (item is not ActivityOverviewItem activity)
            return BrushFromHex("#E5E7EB");

        if (activity.IsGroupHeader)
            return Brushes.Transparent;

        return activity.Status switch
        {
            WorkspaceTaskStatus.NoIniciada => BrushFromHex("#DBEAFE"),
            WorkspaceTaskStatus.Completada => BrushFromHex("#D1FAE5"),
            _ => BrushFromHex("#FEF3C7")
        };
    }

    private static Brush ResolveStatusForeground(object? item)
    {
        if (item is not ActivityOverviewItem activity)
            return BrushFromHex("#374151");

        if (activity.IsGroupHeader)
            return BrushFromHex("#374151");

        return activity.Status switch
        {
            WorkspaceTaskStatus.NoIniciada => BrushFromHex("#1D4ED8"),
            WorkspaceTaskStatus.Completada => BrushFromHex("#047857"),
            _ => BrushFromHex("#B45309")
        };
    }

    private static PackIconKind? ResolveStatusIcon(object? item)
    {
        if (item is not ActivityOverviewItem activity || activity.IsGroupHeader)
            return null;

        return activity.Status switch
        {
            WorkspaceTaskStatus.NoIniciada => PackIconKind.ClockOutline,
            WorkspaceTaskStatus.Completada => PackIconKind.CheckCircle,
            _ => PackIconKind.ProgressClock
        };
    }

    private static Brush ResolvePriorityBorderBrush(object? item)
    {
        if (item is not ActivityOverviewItem activity || activity.IsGroupHeader)
            return BrushFromHex("#D1D5DB");

        return activity.Priority switch
        {
            TaskPriority.Alta => BrushFromHex("#FCA5A5"),
            TaskPriority.Baja => BrushFromHex("#86EFAC"),
            _ => BrushFromHex("#7DD3FC")
        };
    }

    private static Brush ResolvePriorityForeground(object? item)
    {
        if (item is not ActivityOverviewItem activity || activity.IsGroupHeader)
            return BrushFromHex("#374151");

        return activity.Priority switch
        {
            TaskPriority.Alta => BrushFromHex("#B91C1C"),
            TaskPriority.Baja => BrushFromHex("#15803D"),
            _ => BrushFromHex("#0369A1")
        };
    }

    private static PackIconKind? ResolvePriorityIcon(object? item)
    {
        if (item is not ActivityOverviewItem activity || activity.IsGroupHeader)
            return null;

        return activity.Priority switch
        {
            TaskPriority.Alta => PackIconKind.ArrowUpBoldCircleOutline,
            TaskPriority.Baja => PackIconKind.ArrowDownBoldCircleOutline,
            _ => PackIconKind.MinusCircleOutline
        };
    }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    }

    private void UpdateGroupingState()
    {
        foreach (var option in GroupingOptions)
        {
            option.IsSelected = ReferenceEquals(option, SelectedGrouping);
        }

        GroupingToolTip = SelectedGrouping is null
            ? "Agrupar: sin agrupacion"
            : $"Agrupar: {SelectedGrouping.Label}";
        OnPropertyChanged(nameof(IsUngrouped));
    }

    private void UpdateToolbarActions()
    {
        _toolbarActions.Clear();
        _toolbarActions.Add(new EstDataTableToolbarAction
        {
            IconKind = PackIconKind.ViewAgendaOutline,
            ToolTip = GroupingToolTip,
            ContextMenu = BuildGroupingContextMenu()
        });
    }

    private ContextMenu BuildGroupingContextMenu()
    {
        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(new MenuItem
        {
            Header = "Sin agrupacion",
            IsCheckable = true,
            IsChecked = SelectedGrouping is null,
            Command = ClearGroupingCommand
        });
        contextMenu.Items.Add(new Separator());

        foreach (var option in GroupingOptions)
        {
            contextMenu.Items.Add(new MenuItem
            {
                Header = option.Label,
                IsCheckable = true,
                IsChecked = option.IsSelected,
                Command = SetGroupingCommand,
                CommandParameter = option
            });
        }

        return contextMenu;
    }

    private ContextMenu BuildProjectContextMenu(ActivityOverviewItem activity)
    {
        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(new MenuItem
        {
            Header = "Abrir proyecto",
            Command = OpenActivityProjectCommand,
            CommandParameter = activity,
            Icon = new PackIcon { Kind = PackIconKind.OpenInApp }
        });
        contextMenu.Items.Add(new MenuItem
        {
            Header = "Ver en carpeta",
            Command = OpenActivityFolderCommand,
            CommandParameter = activity,
            Icon = new PackIcon { Kind = PackIconKind.FolderOpen }
        });

        return contextMenu;
    }
}
