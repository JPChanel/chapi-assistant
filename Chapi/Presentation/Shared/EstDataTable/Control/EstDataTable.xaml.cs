using app_desktop_base.Models.EstDataTable;
using app_desktop_base.Utilities;
using MaterialDesignThemes.Wpf;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DataGridTextColumn = MaterialDesignThemes.Wpf.DataGridTextColumn;

namespace app_desktop_base.Controls;

public partial class EstDataTable : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(IEnumerable), typeof(EstDataTable), new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty ItemsSourceProperty = DataProperty;

    public static readonly DependencyProperty ColumnsProperty =
        DependencyProperty.Register(nameof(Columns), typeof(IEnumerable<EstDataColumnDefinition>), typeof(EstDataTable), new PropertyMetadata(null, OnColumnsChanged));

    public static readonly DependencyProperty ColumnsSourceProperty = ColumnsProperty;

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(EstDataTable), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

    public static readonly DependencyProperty SelectedItemsProperty =
        DependencyProperty.Register(nameof(SelectedItems), typeof(IList), typeof(EstDataTable), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemsChanged));

    public static readonly DependencyProperty TopToolbarCustomActionsProperty =
        DependencyProperty.Register(nameof(TopToolbarCustomActions), typeof(IEnumerable<EstDataTableToolbarAction>), typeof(EstDataTable), new PropertyMetadata(null, OnTopToolbarCustomActionsChanged));

    public static readonly DependencyProperty RowActionsProperty =
        DependencyProperty.Register(nameof(RowActions), typeof(IEnumerable<EstDataTableRowAction>), typeof(EstDataTable), new PropertyMetadata(null, OnRowActionsChanged));

    public static readonly DependencyProperty ColumnProperty =
        DependencyProperty.Register(nameof(Column), typeof(EstDataTableDefinition), typeof(EstDataTable), new PropertyMetadata(null, OnColumnDefinitionChanged));

    public static readonly DependencyProperty DefinitionProperty = ColumnProperty;

    public static readonly DependencyProperty AddItemCommandProperty =
        DependencyProperty.Register(nameof(AddItemCommand), typeof(ICommand), typeof(EstDataTable), new PropertyMetadata(null));

    public static readonly DependencyProperty DeleteItemCommandProperty =
        DependencyProperty.Register(nameof(DeleteItemCommand), typeof(ICommand), typeof(EstDataTable), new PropertyMetadata(null));

    public static readonly DependencyProperty AllowCreateProperty =
        DependencyProperty.Register(nameof(AllowCreate), typeof(bool), typeof(EstDataTable), new PropertyMetadata(true));

    public static readonly DependencyProperty AllowDeleteProperty =
        DependencyProperty.Register(nameof(AllowDelete), typeof(bool), typeof(EstDataTable), new PropertyMetadata(true));

    public static readonly DependencyProperty AllowInlineEditProperty =
        DependencyProperty.Register(nameof(AllowInlineEdit), typeof(bool), typeof(EstDataTable), new PropertyMetadata(true, OnAllowInlineEditChanged));

    public static readonly DependencyProperty AllowResetStateProperty =
        DependencyProperty.Register(nameof(AllowResetState), typeof(bool), typeof(EstDataTable), new PropertyMetadata(true));

    public static readonly DependencyProperty AllowColumnVisibilityToggleProperty =
        DependencyProperty.Register(nameof(AllowColumnVisibilityToggle), typeof(bool), typeof(EstDataTable), new PropertyMetadata(true));

    public static readonly DependencyProperty EnableMultiSelectionProperty =
        DependencyProperty.Register(nameof(EnableMultiSelection), typeof(bool), typeof(EstDataTable), new PropertyMetadata(false, OnEnableMultiSelectionChanged));

    public static readonly DependencyProperty PageSizeProperty =
        DependencyProperty.Register(nameof(PageSize), typeof(int), typeof(EstDataTable), new PropertyMetadata(10, OnPageSizeChanged));

    public static readonly DependencyProperty ShowRowNumbersProperty =
        DependencyProperty.Register(nameof(ShowRowNumbers), typeof(bool), typeof(EstDataTable), new PropertyMetadata(true, OnTablePresentationPropertyChanged));

    public static readonly DependencyProperty ShowRowActionsProperty =
        DependencyProperty.Register(nameof(ShowRowActions), typeof(bool), typeof(EstDataTable), new PropertyMetadata(true, OnTablePresentationPropertyChanged));

    private readonly ObservableCollection<object> _pagedItems = [];
    private readonly ObservableCollection<DataTableColumnState> _columnStates = [];
    private readonly List<DataTableColumnState> _displayedColumnStates = [];
    private readonly List<DataTableColumnState> _responsiveHiddenColumnStates = [];
    private readonly List<SortDefinition> _sortDefinitions = [];
    private INotifyCollectionChanged? _itemsNotifier;
    private INotifyCollectionChanged? _columnsNotifier;
    private INotifyCollectionChanged? _topToolbarActionsNotifier;
    private INotifyCollectionChanged? _rowActionsNotifier;
    private string _globalSearchText = string.Empty;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private int _totalItems;
    private bool _hasNoResults;
    private bool _synchronizingSelection;
    private bool _areFiltersVisible;
    private double _lastResponsiveWidth;

    public EstDataTable()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += EstDataTable_SizeChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IEnumerable? Data
    {
        get => (IEnumerable?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IEnumerable? ItemsSource
    {
        get => Data;
        set => Data = value;
    }

    public IEnumerable<EstDataColumnDefinition>? Columns
    {
        get => ReadLocalValue(ColumnsProperty) != DependencyProperty.UnsetValue
            ? (IEnumerable<EstDataColumnDefinition>?)GetValue(ColumnsProperty)
            : Column?.Columns;
        set => SetValue(ColumnsProperty, value);
    }

    public IEnumerable<EstDataColumnDefinition>? ColumnsSource
    {
        get => Columns;
        set => Columns = value;
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public IList? SelectedItems
    {
        get => (IList?)GetValue(SelectedItemsProperty);
        set => SetValue(SelectedItemsProperty, value);
    }

    public IEnumerable<EstDataTableToolbarAction>? TopToolbarCustomActions
    {
        get => ReadLocalValue(TopToolbarCustomActionsProperty) != DependencyProperty.UnsetValue
            ? (IEnumerable<EstDataTableToolbarAction>?)GetValue(TopToolbarCustomActionsProperty)
            : Column?.TopToolbarCustomActions;
        set => SetValue(TopToolbarCustomActionsProperty, value);
    }

    public IEnumerable<EstDataTableToolbarAction>? HeaderActions
    {
        get => TopToolbarCustomActions;
        set => TopToolbarCustomActions = value;
    }

    public IEnumerable<EstDataTableRowAction>? RowActions
    {
        get => ReadLocalValue(RowActionsProperty) != DependencyProperty.UnsetValue
            ? (IEnumerable<EstDataTableRowAction>?)GetValue(RowActionsProperty)
            : Column?.RowActions;
        set => SetValue(RowActionsProperty, value);
    }

    public EstDataTableDefinition? Column
    {
        get => (EstDataTableDefinition?)GetValue(ColumnProperty);
        set => SetValue(ColumnProperty, value);
    }

    public EstDataTableDefinition? Definition
    {
        get => Column;
        set => Column = value;
    }

    public ICommand? AddItemCommand
    {
        get => (ICommand?)GetValue(AddItemCommandProperty);
        set => SetValue(AddItemCommandProperty, value);
    }

    public ICommand? DeleteItemCommand
    {
        get => (ICommand?)GetValue(DeleteItemCommandProperty);
        set => SetValue(DeleteItemCommandProperty, value);
    }

    public bool AllowCreate
    {
        get => (bool)GetValue(AllowCreateProperty);
        set => SetValue(AllowCreateProperty, value);
    }

    public bool AllowDelete
    {
        get => (bool)GetValue(AllowDeleteProperty);
        set => SetValue(AllowDeleteProperty, value);
    }

    public bool AllowInlineEdit
    {
        get => (bool)GetValue(AllowInlineEditProperty);
        set => SetValue(AllowInlineEditProperty, value);
    }

    public bool AllowResetState
    {
        get => (bool)GetValue(AllowResetStateProperty);
        set => SetValue(AllowResetStateProperty, value);
    }

    public bool AllowColumnVisibilityToggle
    {
        get => (bool)GetValue(AllowColumnVisibilityToggleProperty);
        set => SetValue(AllowColumnVisibilityToggleProperty, value);
    }

    public bool EnableMultiSelection
    {
        get => (bool)GetValue(EnableMultiSelectionProperty);
        set => SetValue(EnableMultiSelectionProperty, value);
    }

    public int PageSize
    {
        get => (int)GetValue(PageSizeProperty);
        set => SetValue(PageSizeProperty, value);
    }

    public bool ShowRowNumbers
    {
        get => (bool)GetValue(ShowRowNumbersProperty);
        set => SetValue(ShowRowNumbersProperty, value);
    }

    public bool ShowRowActions
    {
        get => (bool)GetValue(ShowRowActionsProperty);
        set => SetValue(ShowRowActionsProperty, value);
    }

    public ObservableCollection<object> PagedItems => _pagedItems;

    public ObservableCollection<DataTableColumnState> ColumnStates => _columnStates;

    public IEnumerable<DataTableColumnState> VisibleFilterStates => _displayedColumnStates.Where(static state => state.IsFilterable);

    public IReadOnlyList<int> PageSizeOptions { get; } = [10, 25, 50, 100];

    public string GlobalSearchText
    {
        get => _globalSearchText;
        set
        {
            if (_globalSearchText == value)
            {
                return;
            }

            _globalSearchText = value;
            _currentPage = 1;
            OnPropertyChanged(nameof(GlobalSearchText));
            RefreshView();
        }
    }

    public string PaginationSummary => $"Pagina {_currentPage} de {_totalPages} - {_totalItems} registros";

    public int CurrentPageOffset => (_currentPage - 1) * Math.Max(1, PageSize);

    public string StatusSummary
    {
        get
        {
            var selectedCount = GetSelectedItemsSnapshot().Count;
            return selectedCount > 0
                ? $"{PaginationSummary} - {selectedCount} seleccionadas"
                : PaginationSummary;
        }
    }

    public bool HasNoResults
    {
        get => _hasNoResults;
        private set
        {
            if (_hasNoResults == value)
            {
                return;
            }

            _hasNoResults = value;
            OnPropertyChanged(nameof(HasNoResults));
        }
    }

    public bool HasSelection => GetSelectedItemsSnapshot().Count > 0;

    public IEnumerable<EstDataTableToolbarAction> ResolvedTopToolbarCustomActions =>
        EffectiveTopToolbarCustomActions ?? Enumerable.Empty<EstDataTableToolbarAction>();

    public IEnumerable<EstDataTableRowAction> ResolvedRowActions =>
        EffectiveRowActions ?? Enumerable.Empty<EstDataTableRowAction>();

    public bool HasTopToolbarCustomActions => EffectiveTopToolbarCustomActions?.Any(static action => action.IsVisible) == true;

    public bool HasHeaderActions => HasTopToolbarCustomActions;

    public bool HasRowActions => EffectiveRowActions?.Any(static action => action.IsVisible) == true;

    public bool HasFilterableColumns => _displayedColumnStates.Any(static state => state.IsFilterable);

    public bool HasActiveTableState =>
        _currentPage > 1
        || PageSize != 10
        || !string.IsNullOrWhiteSpace(GlobalSearchText)
        || _sortDefinitions.Count > 0
        || _columnStates.Any(static state => state.IsVisible != state.DefaultVisible)
        || _columnStates.Any(static state => !string.IsNullOrWhiteSpace(state.FilterText));

    public bool CanGoPrevious => _currentPage > 1;

    public bool CanGoNext => _currentPage < _totalPages;

    public int ResponsiveHiddenCount => _responsiveHiddenColumnStates.Count;

    public bool HasResponsiveDetails => ResponsiveHiddenCount > 0;

    public bool AreFiltersVisible
    {
        get => _areFiltersVisible;
        private set
        {
            if (_areFiltersVisible == value)
            {
                return;
            }

            _areFiltersVisible = value;
            OnPropertyChanged(nameof(AreFiltersVisible));
            OnPropertyChanged(nameof(FiltersButtonLabel));
        }
    }

    public int ActiveFilterCount => _columnStates.Count(state => !string.IsNullOrWhiteSpace(state.FilterText));

    public string FiltersButtonLabel => ActiveFilterCount > 0 ? $"Filtros ({ActiveFilterCount})" : "Filtros";

    public string ToolbarSummary =>
        HasResponsiveDetails
            ? $"{ResponsiveHiddenCount} columna(s) en detalle responsive"
            : ActiveFilterCount > 0
            ? $"{ActiveFilterCount} filtro(s) activos"
            : "Shift+click en cabeceras para ordenar por varias columnas";

    public DataGridSelectionMode GridSelectionMode => EnableMultiSelection ? DataGridSelectionMode.Extended : DataGridSelectionMode.Single;

    private IEnumerable<EstDataColumnDefinition>? EffectiveColumns =>
        ReadLocalValue(ColumnsProperty) != DependencyProperty.UnsetValue
            ? Columns
            : Column?.Columns;

    private IEnumerable<EstDataTableToolbarAction>? EffectiveTopToolbarCustomActions =>
        ReadLocalValue(TopToolbarCustomActionsProperty) != DependencyProperty.UnsetValue
            ? TopToolbarCustomActions
            : Column?.TopToolbarCustomActions;

    private IEnumerable<EstDataTableRowAction>? EffectiveRowActions =>
        ReadLocalValue(RowActionsProperty) != DependencyProperty.UnsetValue
            ? RowActions
            : Column?.RowActions;

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EstDataTable)d;
        control.DetachItemsNotifier();
        control.AttachItemsNotifier(e.NewValue as IEnumerable);
        control.ResolveColumns();
        control.RefreshView();
    }

    private static void OnColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EstDataTable)d;
        control.RefreshColumnsSubscription();
        control.ResolveColumns();
        control.RefreshView();
    }

    private static void OnTopToolbarCustomActionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EstDataTable)d;
        control.RefreshTopToolbarActionsSubscription();
        control.RaiseActionStateProperties();
    }

    private static void OnRowActionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EstDataTable)d;
        control.RefreshRowActionsSubscription();
        control.RaiseActionStateProperties();
        control.BuildGridColumns();
    }

    private static void OnColumnDefinitionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EstDataTable)d;
        control.RefreshColumnsSubscription();
        control.RefreshTopToolbarActionsSubscription();
        control.RefreshRowActionsSubscription();
        control.ResolveColumns();
        control.RefreshView();
        control.RaiseActionStateProperties();
        control.OnPropertyChanged(nameof(Columns));
        control.OnPropertyChanged(nameof(TopToolbarCustomActions));
        control.OnPropertyChanged(nameof(RowActions));
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EstDataTable)d;
        control.SyncGridSelectionFromDependencyProperties();
        control.NotifySelectionChanged();
    }

    private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EstDataTable)d;
        control.SyncGridSelectionFromDependencyProperties();
        control.NotifySelectionChanged();
    }

    private static void OnPageSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EstDataTable)d;
        control._currentPage = 1;
        control.RefreshView();
    }

    private static void OnAllowInlineEditChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EstDataTable)d;
        control.BuildGridColumns();
    }

    private static void OnEnableMultiSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EstDataTable)d;
        control.OnPropertyChanged(nameof(GridSelectionMode));
        control.SyncGridSelectionFromDependencyProperties();
        control.NotifySelectionChanged();
    }

    private static void OnTablePresentationPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EstDataTable)d;
        control.BuildGridColumns();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _lastResponsiveWidth = ActualWidth;
        ResolveColumns();
        RefreshView();
        SyncGridSelectionFromDependencyProperties();
    }

    private void EstDataTable_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded || Math.Abs(e.NewSize.Width - _lastResponsiveWidth) < 24)
        {
            return;
        }

        _lastResponsiveWidth = e.NewSize.Width;
        BuildGridColumns();
        RefreshView();
    }

    private void AttachItemsNotifier(IEnumerable? items)
    {
        if (items is INotifyCollectionChanged notifier)
        {
            _itemsNotifier = notifier;
            _itemsNotifier.CollectionChanged += ItemsNotifier_CollectionChanged;
        }
    }

    private void DetachItemsNotifier()
    {
        if (_itemsNotifier is null)
        {
            return;
        }

        _itemsNotifier.CollectionChanged -= ItemsNotifier_CollectionChanged;
        _itemsNotifier = null;
    }

    private void AttachColumnsNotifier(IEnumerable<EstDataColumnDefinition>? columns)
    {
        if (columns is INotifyCollectionChanged notifier)
        {
            _columnsNotifier = notifier;
            _columnsNotifier.CollectionChanged += ColumnsNotifier_CollectionChanged;
        }
    }

    private void RefreshColumnsSubscription()
    {
        DetachColumnsNotifier();
        AttachColumnsNotifier(EffectiveColumns);
    }

    private void AttachTopToolbarActionsNotifier(IEnumerable<EstDataTableToolbarAction>? actions)
    {
        if (actions is INotifyCollectionChanged notifier)
        {
            _topToolbarActionsNotifier = notifier;
            _topToolbarActionsNotifier.CollectionChanged += TopToolbarActionsNotifier_CollectionChanged;
        }
    }

    private void RefreshTopToolbarActionsSubscription()
    {
        DetachTopToolbarActionsNotifier();
        AttachTopToolbarActionsNotifier(EffectiveTopToolbarCustomActions);
    }

    private void AttachRowActionsNotifier(IEnumerable<EstDataTableRowAction>? actions)
    {
        if (actions is INotifyCollectionChanged notifier)
        {
            _rowActionsNotifier = notifier;
            _rowActionsNotifier.CollectionChanged += RowActionsNotifier_CollectionChanged;
        }
    }

    private void RefreshRowActionsSubscription()
    {
        DetachRowActionsNotifier();
        AttachRowActionsNotifier(EffectiveRowActions);
    }

    private void DetachColumnsNotifier()
    {
        if (_columnsNotifier is null)
        {
            return;
        }

        _columnsNotifier.CollectionChanged -= ColumnsNotifier_CollectionChanged;
        _columnsNotifier = null;
    }

    private void DetachTopToolbarActionsNotifier()
    {
        if (_topToolbarActionsNotifier is null)
        {
            return;
        }

        _topToolbarActionsNotifier.CollectionChanged -= TopToolbarActionsNotifier_CollectionChanged;
        _topToolbarActionsNotifier = null;
    }

    private void DetachRowActionsNotifier()
    {
        if (_rowActionsNotifier is null)
        {
            return;
        }

        _rowActionsNotifier.CollectionChanged -= RowActionsNotifier_CollectionChanged;
        _rowActionsNotifier = null;
    }

    private void ItemsNotifier_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshView();
    }

    private void ColumnsNotifier_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ResolveColumns();
        RefreshView();
    }

    private void TopToolbarActionsNotifier_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaiseActionStateProperties();
    }

    private void RowActionsNotifier_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaiseActionStateProperties();
        BuildGridColumns();
    }

    private void ResolveColumns()
    {
        foreach (var state in _columnStates)
        {
            state.PropertyChanged -= ColumnState_PropertyChanged;
        }

        _columnStates.Clear();

        var explicitColumns = EffectiveColumns?.Where(static column => column.CanResolveValue).ToList();
        if (explicitColumns is { Count: > 0 })
        {
            foreach (var column in explicitColumns)
            {
                AddColumnState(column);
            }
        }
        else
        {
            var itemType = ResolveItemType();
            if (itemType is not null)
            {
                foreach (var property in PropertyPathHelper.GetBrowsableProperties(itemType))
                {
                    AddColumnState(new EstDataColumnDefinition
                    {
                        Header = property.Name,
                        Path = property.Name
                    });
                }
            }
        }

        BuildGridColumns();
        RaiseTableStateProperties();
    }

    private void AddColumnState(EstDataColumnDefinition definition)
    {
        var state = new DataTableColumnState(definition);
        state.PropertyChanged += ColumnState_PropertyChanged;
        _columnStates.Add(state);
    }

    private void RefreshResponsiveColumns()
    {
        _displayedColumnStates.Clear();
        _responsiveHiddenColumnStates.Clear();

        var userVisibleColumns = _columnStates.Where(static state => state.IsVisible).ToList();
        if (userVisibleColumns.Count == 0)
        {
            return;
        }

        _displayedColumnStates.AddRange(userVisibleColumns);

        var availableWidth = ResolveAvailableColumnsWidth();
        var reservedWidth = GetFixedColumnsWidth();
        var totalWidth = GetColumnsPreferredWidth(_displayedColumnStates) + reservedWidth;
        var expansionReserved = false;

        while (_displayedColumnStates.Count > 1 && totalWidth > availableWidth)
        {
            var candidate = _displayedColumnStates
                .OrderByDescending(static state => state.Definition.Priority)
                .ThenByDescending(ResolveColumnPreferredWidth)
                .ThenByDescending(state => _columnStates.IndexOf(state))
                .FirstOrDefault();

            if (candidate is null)
            {
                break;
            }

            _displayedColumnStates.Remove(candidate);
            _responsiveHiddenColumnStates.Add(candidate);

            if (!expansionReserved)
            {
                totalWidth += 54;
                expansionReserved = true;
            }

            totalWidth -= ResolveColumnPreferredWidth(candidate);
        }

        _responsiveHiddenColumnStates.Sort((left, right) => _columnStates.IndexOf(left).CompareTo(_columnStates.IndexOf(right)));
    }

    private double ResolveAvailableColumnsWidth()
    {
        var baseWidth = PART_DataGrid?.ActualWidth > 0 ? PART_DataGrid.ActualWidth : ActualWidth;
        return Math.Max(360, baseWidth);
    }

    private double GetFixedColumnsWidth()
    {
        var width = 24d;

        if (ShowRowNumbers)
        {
            width += 56;
        }

        if (ShowRowActions && HasRowActions)
        {
            width += 96;
        }

        return width;
    }

    private static double GetColumnsPreferredWidth(IEnumerable<DataTableColumnState> columns)
    {
        return columns.Sum(ResolveColumnPreferredWidth);
    }

    private static double ResolveColumnPreferredWidth(DataTableColumnState columnState)
    {
        var definition = columnState.Definition;
        return definition.ResolvedWidth > 0 ? definition.ResolvedWidth : Math.Max(definition.MinWidth, 140);
    }

    private void BuildGridColumns()
    {
        if (PART_DataGrid is null)
        {
            return;
        }

        RefreshResponsiveColumns();
        PART_DataGrid.Columns.Clear();

        if (ShowRowNumbers)
        {
            PART_DataGrid.Columns.Add(CreateRowNumberColumn());
        }

        if (HasResponsiveDetails)
        {
            PART_DataGrid.Columns.Add(CreateExpandColumn());
        }

        foreach (var state in _displayedColumnStates)
        {
            var definition = state.Definition;
            PART_DataGrid.Columns.Add(definition.HasCustomCellPresentation
                ? CreateCustomDataColumn(state)
                : CreateTextColumn(state));
        }

        if (ShowRowActions && HasRowActions)
        {
            PART_DataGrid.Columns.Add(CreateActionsColumn());
        }

        UpdateSortGlyphs();

        if (!HasResponsiveDetails)
        {
            CollapseAllRowDetails();
        }
    }

    private DataGridColumn CreateRowNumberColumn()
    {
        var templateColumn = new DataGridTemplateColumn
        {
            Header = "#",
            Width = new DataGridLength(56),
            IsReadOnly = true,
            CanUserSort = false,
            CanUserResize = false
        };

        var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
        textBlockFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        textBlockFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        textBlockFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        textBlockFactory.SetValue(TextBlock.FontSizeProperty, 13.0);
        textBlockFactory.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")));
        textBlockFactory.SetBinding(TextBlock.TextProperty, new Binding("Header")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridRow), 1),
            FallbackValue = string.Empty,
            TargetNullValue = string.Empty
        });

        templateColumn.CellTemplate = new DataTemplate
        {
            VisualTree = textBlockFactory
        };

        return templateColumn;
    }

    private DataGridColumn CreateTextColumn(DataTableColumnState state)
    {
        var definition = state.Definition;
        var textColumn = new DataGridTextColumn
        {
            Header = state,
            HeaderTemplate = (DataTemplate)FindResource("TableColumnHeaderTemplate"),
            SortMemberPath = definition.IsSortable ? definition.ResolvedColumnKey : string.Empty,
            Width = definition.ResolvedWidth > 0 ? new DataGridLength(definition.ResolvedWidth) : new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = definition.MinWidth,
            MaxWidth = definition.MaxWidth,
            CanUserSort = definition.IsSortable,
            CanUserResize = definition.CanUserResize,
            IsReadOnly = !AllowInlineEdit || !definition.CanEdit,
            Binding = new Binding(definition.ResolvedAccessorKey)
            {
                Mode = definition.CanEdit && AllowInlineEdit ? BindingMode.TwoWay : BindingMode.OneWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                StringFormat = definition.StringFormat
            }
        };

        var elementStyle = new Style(typeof(TextBlock));
        elementStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, definition.TextAlignment));
        elementStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        textColumn.ElementStyle = elementStyle;

        var editingStyle = new Style(typeof(TextBox));
        editingStyle.Setters.Add(new Setter(TextBox.TextAlignmentProperty, definition.TextAlignment));
        textColumn.EditingElementStyle = editingStyle;

        return textColumn;
    }

    private DataGridColumn CreateCustomDataColumn(DataTableColumnState state)
    {
        var definition = state.Definition;
        var templateColumn = new DataGridTemplateColumn
        {
            Header = state,
            HeaderTemplate = (DataTemplate)FindResource("TableColumnHeaderTemplate"),
            SortMemberPath = definition.IsSortable ? definition.ResolvedColumnKey : string.Empty,
            Width = definition.ResolvedWidth > 0 ? new DataGridLength(definition.ResolvedWidth) : new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = definition.MinWidth,
            MaxWidth = definition.MaxWidth,
            CanUserSort = definition.IsSortable,
            CanUserResize = definition.CanUserResize,
            IsReadOnly = true
        };

        var presenterFactory = new FrameworkElementFactory(typeof(EstDataCellPresenter));
        presenterFactory.SetBinding(EstDataCellPresenter.ItemProperty, new Binding());
        presenterFactory.SetValue(EstDataCellPresenter.ColumnDefinitionProperty, definition);
        presenterFactory.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        presenterFactory.SetValue(MarginProperty, new Thickness(0));

        templateColumn.CellTemplate = new DataTemplate
        {
            VisualTree = presenterFactory
        };

        return templateColumn;
    }

    private DataGridColumn CreateActionsColumn()
    {
        var visibleRowActionCount = Math.Max(1, EffectiveRowActions?.Count(static action => action.IsVisible) ?? 1);
        var templateColumn = new DataGridTemplateColumn
        {
            Header = string.Empty,
            Width = new DataGridLength(24 + (visibleRowActionCount * 40)),
            IsReadOnly = true,
            CanUserSort = false,
            CanUserResize = false
        };

        templateColumn.CellTemplate = (DataTemplate)FindResource("TableRowActionsCellTemplate");

        return templateColumn;
    }

    private DataGridColumn CreateExpandColumn()
    {
        var templateColumn = new DataGridTemplateColumn
        {
            Header = string.Empty,
            Width = new DataGridLength(52),
            IsReadOnly = true,
            CanUserSort = false,
            CanUserResize = false
        };

        var buttonFactory = new FrameworkElementFactory(typeof(Button));
        buttonFactory.SetValue(Button.StyleProperty, FindResource("TableCellIconButtonStyle"));
        buttonFactory.SetValue(Button.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        buttonFactory.SetValue(Button.VerticalAlignmentProperty, VerticalAlignment.Center);
        buttonFactory.SetValue(Button.ToolTipProperty, "Ver campos ocultos");
        buttonFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler(ToggleRowDetailsButton_Click));

        var iconFactory = new FrameworkElementFactory(typeof(PackIcon));
        iconFactory.SetValue(PackIcon.WidthProperty, 16.0);
        iconFactory.SetValue(PackIcon.HeightProperty, 16.0);
        iconFactory.SetValue(PackIcon.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        iconFactory.SetValue(PackIcon.VerticalAlignmentProperty, VerticalAlignment.Center);
        iconFactory.SetValue(PackIcon.ForegroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")));
        iconFactory.SetValue(PackIcon.KindProperty, PackIconKind.ChevronRight);

        var iconStyle = new Style(typeof(PackIcon));
        iconStyle.Setters.Add(new Setter(PackIcon.KindProperty, PackIconKind.ChevronRight));
        iconStyle.Triggers.Add(new DataTrigger
        {
            Binding = new Binding("DetailsVisibility")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridRow), 1)
            },
            Value = Visibility.Visible,
            Setters =
            {
                new Setter(PackIcon.KindProperty, PackIconKind.ChevronDown)
            }
        });
        iconFactory.SetValue(PackIcon.StyleProperty, iconStyle);

        buttonFactory.AppendChild(iconFactory);

        templateColumn.CellTemplate = new DataTemplate
        {
            VisualTree = buttonFactory
        };

        return templateColumn;
    }

    private void ColumnState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DataTableColumnState state)
        {
            return;
        }

        if (e.PropertyName == nameof(DataTableColumnState.FilterText))
        {
            _currentPage = 1;
            RefreshView();
            RaiseFilterProperties();
            return;
        }

        if (e.PropertyName != nameof(DataTableColumnState.IsVisible))
        {
            return;
        }

        EnsureAtLeastOneVisibleColumn(state);

        if (!state.IsVisible)
        {
            state.FilterText = string.Empty;
            _sortDefinitions.RemoveAll(sort => sort.ColumnKey == state.Path);
        }

        BuildGridColumns();
        RefreshView();
        RaiseTableStateProperties();
    }

    private void EnsureAtLeastOneVisibleColumn(DataTableColumnState changedState)
    {
        if (_columnStates.Any(static state => state.IsVisible))
        {
            return;
        }

        changedState.PropertyChanged -= ColumnState_PropertyChanged;
        changedState.IsVisible = true;
        changedState.PropertyChanged += ColumnState_PropertyChanged;
    }

    private void RefreshView()
    {
        if (!IsLoaded)
        {
            return;
        }

        var sourceItems = GetSnapshot();
        IEnumerable<object> query = sourceItems;

        var visibleColumns = _displayedColumnStates.ToList();

        if (!string.IsNullOrWhiteSpace(GlobalSearchText))
        {
            query = query.Where(item => visibleColumns.Any(column => ContainsValue(item, column.Definition, GlobalSearchText)));
        }

        foreach (var filter in visibleColumns.Where(static filter => filter.IsFilterable && !string.IsNullOrWhiteSpace(filter.FilterText)))
        {
            query = query.Where(item => ContainsValue(item, filter.Definition, filter.FilterText));
        }

        query = ApplySort(query);

        var filteredItems = query.ToList();
        _totalItems = filteredItems.Count;
        _totalPages = Math.Max(1, (int)Math.Ceiling(_totalItems / (double)Math.Max(1, PageSize)));
        _currentPage = Math.Min(_currentPage, _totalPages);
        _currentPage = Math.Max(_currentPage, 1);

        var pageItems = filteredItems
            .Skip((_currentPage - 1) * Math.Max(1, PageSize))
            .Take(Math.Max(1, PageSize))
            .ToList();

        _pagedItems.Clear();
        foreach (var item in pageItems)
        {
            _pagedItems.Add(item);
        }

        HasNoResults = _totalItems == 0;
        RaiseTableStateProperties();
        QueueRowHeaderUpdate();
    }

    private IEnumerable<object> ApplySort(IEnumerable<object> query)
    {
        if (_sortDefinitions.Count == 0)
        {
            return query;
        }

        IOrderedEnumerable<object>? ordered = null;

        foreach (var sort in _sortDefinitions)
        {
            ordered = ordered is null
                ? ApplyInitialSort(query, sort)
                : ApplyThenSort(ordered, sort);
        }

        return ordered ?? query;
    }

    private static IOrderedEnumerable<object> ApplyInitialSort(IEnumerable<object> source, SortDefinition sort)
    {
        return sort.Direction == ListSortDirection.Ascending
            ? source.OrderBy(item => GetComparableValue(item, sort.Definition))
            : source.OrderByDescending(item => GetComparableValue(item, sort.Definition));
    }

    private static IOrderedEnumerable<object> ApplyThenSort(IOrderedEnumerable<object> source, SortDefinition sort)
    {
        return sort.Direction == ListSortDirection.Ascending
            ? source.ThenBy(item => GetComparableValue(item, sort.Definition))
            : source.ThenByDescending(item => GetComparableValue(item, sort.Definition));
    }

    private List<object> GetSnapshot()
    {
        if (ItemsSource is null)
        {
            return [];
        }

        return ItemsSource.Cast<object>().ToList();
    }

    private Type? ResolveItemType()
    {
        if (ItemsSource is null)
        {
            return null;
        }

        var collectionType = ItemsSource.GetType();
        if (collectionType.IsGenericType)
        {
            return collectionType.GetGenericArguments().FirstOrDefault();
        }

        return ItemsSource.Cast<object?>().FirstOrDefault()?.GetType();
    }

    private static bool ContainsValue(object item, EstDataColumnDefinition definition, string searchTerm)
    {
        var text = ResolveFilterText(definition, item);
        return text.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
    }

    private static object? GetComparableValue(object item, EstDataColumnDefinition definition)
    {
        var value = ResolveRawValue(definition, item);
        return value switch
        {
            null => null,
            string text => text,
            IComparable comparable => comparable,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    public IReadOnlyList<ResponsiveDetailItem> GetResponsiveDetailItems(object item)
    {
        if (!HasResponsiveDetails)
        {
            return Array.Empty<ResponsiveDetailItem>();
        }

        return _responsiveHiddenColumnStates
            .Select(column => new ResponsiveDetailItem
            {
                Label = column.Header,
                Value = ResolveFilterText(column.Definition, item)
            })
            .ToList();
    }


    private static string FormatCellValue(EstDataColumnDefinition definition, object? value)
    {
        if (value is null)
        {
            return "-";
        }

        if (!string.IsNullOrWhiteSpace(definition.StringFormat) && value is IFormattable formattable)
        {
            return formattable.ToString(definition.StringFormat, CultureInfo.CurrentCulture);
        }

        return Convert.ToString(value, CultureInfo.CurrentCulture) ?? "-";
    }

    private static object? ResolveRawValue(EstDataColumnDefinition definition, object item)
    {
        if (definition.ValueSelector is not null)
        {
            return definition.ValueSelector(item);
        }

        if (definition.Value is not null)
        {
            return definition.Value;
        }

        if (!string.IsNullOrWhiteSpace(definition.ResolvedAccessorKey))
        {
            return PropertyPathHelper.GetValue(item, definition.ResolvedAccessorKey);
        }

        return item;
    }

    private static string ResolveFilterText(EstDataColumnDefinition definition, object item)
    {
        if (definition.TextSelector is not null)
        {
            return definition.TextSelector(item) ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(definition.Text))
        {
            return definition.Text;
        }

        var custom = definition.CellPresentationSelector?.Invoke(item);
        if (!string.IsNullOrWhiteSpace(custom?.Text))
        {
            return custom.Text!;
        }

        return FormatCellValue(definition, ResolveRawValue(definition, item));
    }

    private void UpdateSortGlyphs()
    {
        if (PART_DataGrid is null)
        {
            return;
        }

        foreach (var column in PART_DataGrid.Columns)
        {
            column.SortDirection = _sortDefinitions.FirstOrDefault(sort => sort.ColumnKey == column.SortMemberPath)?.Direction;
        }
    }

    private void PART_DataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;

        var requestedPath = e.Column.SortMemberPath;
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return;
        }

        var appendSort = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (!appendSort)
        {
            _sortDefinitions.RemoveAll(sort => sort.ColumnKey != requestedPath);
        }

        var state = _columnStates.FirstOrDefault(columnState => columnState.Path == requestedPath);
        if (state is null)
        {
            return;
        }

        var existingIndex = _sortDefinitions.FindIndex(sort => sort.ColumnKey == requestedPath);
        if (existingIndex < 0)
        {
            _sortDefinitions.Add(new SortDefinition(requestedPath, state.Definition, ListSortDirection.Ascending));
        }
        else
        {
            var existing = _sortDefinitions[existingIndex];
            if (existing.Direction == ListSortDirection.Ascending)
            {
                _sortDefinitions[existingIndex] = existing with { Direction = ListSortDirection.Descending };
            }
            else
            {
                _sortDefinitions.RemoveAt(existingIndex);
            }
        }

        _currentPage = 1;
        UpdateSortGlyphs();
        RefreshView();
    }

    private void PART_DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelection)
        {
            return;
        }

        _synchronizingSelection = true;
        SelectedItem = PART_DataGrid.SelectedItem;
        SelectedItems = PART_DataGrid.SelectedItems.Cast<object>().ToList();
        _synchronizingSelection = false;

        NotifySelectionChanged();
    }

    private void PART_DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.Header = CurrentPageOffset + e.Row.GetIndex() + 1;
        UpdateRowDetailsVisualState(e.Row);
    }

    private void SyncGridSelectionFromDependencyProperties()
    {
        if (PART_DataGrid is null || _synchronizingSelection)
        {
            return;
        }

        _synchronizingSelection = true;
        PART_DataGrid.SelectionMode = GridSelectionMode;

        if (EnableMultiSelection)
        {
            PART_DataGrid.SelectedItems.Clear();

            foreach (var item in GetSelectedItemsSnapshot())
            {
                PART_DataGrid.SelectedItems.Add(item);
            }

            if (SelectedItem is not null && !PART_DataGrid.SelectedItems.Contains(SelectedItem))
            {
                PART_DataGrid.SelectedItems.Add(SelectedItem);
            }

            PART_DataGrid.SelectedItem = SelectedItem;
        }
        else
        {
            PART_DataGrid.SelectedItem = SelectedItem;
        }

        _synchronizingSelection = false;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowCreate)
        {
            return;
        }

        if (AddItemCommand?.CanExecute(null) == true)
        {
            AddItemCommand.Execute(null);
            return;
        }

        if (ItemsSource is not IList list)
        {
            return;
        }

        var itemType = ResolveItemType();
        if (itemType is null || itemType.GetConstructor(Type.EmptyTypes) is null)
        {
            return;
        }

        var newItem = Activator.CreateInstance(itemType);
        if (newItem is null)
        {
            return;
        }

        list.Add(newItem);
        SelectedItem = newItem;
        SelectedItems = new List<object> { newItem };
        RefreshView();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowDelete)
        {
            return;
        }

        var selectedItems = GetSelectedItemsSnapshot();
        if (selectedItems.Count == 0)
        {
            return;
        }

        if (DeleteItemCommand is not null)
        {
            foreach (var item in selectedItems.Where(item => DeleteItemCommand.CanExecute(item)))
            {
                DeleteItemCommand.Execute(item);
            }

            return;
        }

        if (ItemsSource is not IList list)
        {
            return;
        }

        foreach (var item in selectedItems)
        {
            list.Remove(item);
        }

        SelectedItem = null;
        SelectedItems = null;
        RefreshView();
    }

    private void ResetStateButton_Click(object sender, RoutedEventArgs e)
    {
        GlobalSearchText = string.Empty;
        PageSize = 10;
        _currentPage = 1;
        _sortDefinitions.Clear();

        foreach (var columnState in _columnStates)
        {
            columnState.PropertyChanged -= ColumnState_PropertyChanged;
            columnState.FilterText = string.Empty;
            columnState.IsVisible = columnState.DefaultVisible;
            columnState.PropertyChanged += ColumnState_PropertyChanged;
        }

        BuildGridColumns();
        AreFiltersVisible = false;
        RefreshView();
    }

    private void ToggleFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_columnStates.Any(static state => state.IsVisible && state.IsFilterable))
        {
            return;
        }

        AreFiltersVisible = !AreFiltersVisible;
        BuildGridColumns();
    }

    private void HeaderFilterButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleFiltersButton_Click(sender, e);
    }

    private void ColumnVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null)
        {
            return;
        }

        BuildColumnVisibilityMenu(button.ContextMenu);
        button.ContextMenu.DataContext = this;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }

    private void ColumnVisibilityItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not DataTableColumnState state)
        {
            return;
        }

        state.IsVisible = !state.IsVisible;
    }

    private void ColumnVisibilityMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not DataTableColumnState state)
        {
            return;
        }

        state.IsVisible = menuItem.IsChecked;
    }

    private void ShowAllColumnsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        foreach (var state in _columnStates)
        {
            state.PropertyChanged -= ColumnState_PropertyChanged;
            state.IsVisible = true;
            state.PropertyChanged += ColumnState_PropertyChanged;
        }

        BuildGridColumns();
        RefreshView();
        RaiseTableStateProperties();
    }

    private void ResetColumnVisibilityMenuItem_Click(object sender, RoutedEventArgs e)
    {
        foreach (var state in _columnStates)
        {
            state.PropertyChanged -= ColumnState_PropertyChanged;
            state.IsVisible = state.DefaultVisible;
            state.PropertyChanged += ColumnState_PropertyChanged;
        }

        BuildGridColumns();
        RefreshView();
        RaiseTableStateProperties();
    }

    private void BuildColumnVisibilityMenu(ContextMenu contextMenu)
    {
        contextMenu.Items.Clear();

        var showAllItem = new MenuItem
        {
            Header = "Mostrar todas",
            StaysOpenOnClick = false,
            Icon = new PackIcon { Kind = PackIconKind.EyeOutline }
        };
        showAllItem.Click += ShowAllColumnsMenuItem_Click;
        contextMenu.Items.Add(showAllItem);

        var resetItem = new MenuItem
        {
            Header = "Restablecer columnas",
            StaysOpenOnClick = false,
            Icon = new PackIcon { Kind = PackIconKind.Refresh }
        };
        resetItem.Click += ResetColumnVisibilityMenuItem_Click;
        contextMenu.Items.Add(resetItem);
        contextMenu.Items.Add(new Separator());

        foreach (var state in _columnStates)
        {
            var columnItem = new MenuItem
            {
                Header = state.Header,
                IsCheckable = true,
                IsChecked = state.IsVisible,
                StaysOpenOnClick = true,
                Tag = state
            };
            columnItem.Click += ColumnVisibilityMenuItem_Click;
            contextMenu.Items.Add(columnItem);
        }
    }

    private void ToolbarCustomActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not EstDataTableToolbarAction action)
        {
            return;
        }

        if (action.ContextMenu is ContextMenu contextMenu)
        {
            contextMenu.PlacementTarget = button;
            contextMenu.IsOpen = true;
            return;
        }

        if (action.Command?.CanExecute(action.CommandParameter) == true)
        {
            action.Command.Execute(action.CommandParameter);
        }
    }

    private void DeleteRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowDelete || sender is not Button button || button.Tag is null)
        {
            return;
        }

        if (DeleteItemCommand?.CanExecute(button.Tag) == true)
        {
            DeleteItemCommand.Execute(button.Tag);
            return;
        }

        if (ItemsSource is IList list)
        {
            list.Remove(button.Tag);
            RefreshView();
        }
    }

    private void ToggleRowDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || PART_DataGrid is null)
        {
            return;
        }

        if (ItemsControl.ContainerFromElement(PART_DataGrid, button) is not DataGridRow row)
        {
            return;
        }

        row.DetailsVisibility = row.DetailsVisibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpdateRowDetailsVisualState(row, button);
    }

    private void PreviousPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage <= 1)
        {
            return;
        }

        _currentPage--;
        RefreshView();
    }

    private void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage >= _totalPages)
        {
            return;
        }

        _currentPage++;
        RefreshView();
    }

    private List<object> GetSelectedItemsSnapshot()
    {
        if (EnableMultiSelection)
        {
            return SelectedItems?.Cast<object>().Distinct().ToList() ?? [];
        }

        return SelectedItem is null ? [] : [SelectedItem];
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(StatusSummary));
    }

    private void RaiseTableStateProperties()
    {
        OnPropertyChanged(nameof(VisibleFilterStates));
        OnPropertyChanged(nameof(PaginationSummary));
        OnPropertyChanged(nameof(CurrentPageOffset));
        OnPropertyChanged(nameof(StatusSummary));
        OnPropertyChanged(nameof(HasActiveTableState));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(ResponsiveHiddenCount));
        OnPropertyChanged(nameof(HasResponsiveDetails));
        OnPropertyChanged(nameof(HasTopToolbarCustomActions));
        OnPropertyChanged(nameof(HasHeaderActions));
        OnPropertyChanged(nameof(HasRowActions));
        OnPropertyChanged(nameof(HasFilterableColumns));
        RaiseFilterProperties();
    }

    private void RaiseActionStateProperties()
    {
        OnPropertyChanged(nameof(ResolvedTopToolbarCustomActions));
        OnPropertyChanged(nameof(ResolvedRowActions));
        OnPropertyChanged(nameof(HasTopToolbarCustomActions));
        OnPropertyChanged(nameof(HasHeaderActions));
        OnPropertyChanged(nameof(HasRowActions));
    }

    private void RaiseFilterProperties()
    {
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(FiltersButtonLabel));
        OnPropertyChanged(nameof(ToolbarSummary));
    }

    private void QueueRowHeaderUpdate()
    {
        if (PART_DataGrid is null || !ShowRowNumbers)
        {
            return;
        }

        Dispatcher.BeginInvoke(UpdateVisibleRowHeaders, DispatcherPriority.Loaded);
    }

    private void UpdateVisibleRowHeaders()
    {
        if (PART_DataGrid is null || !ShowRowNumbers)
        {
            return;
        }

        for (var index = 0; index < PART_DataGrid.Items.Count; index++)
        {
            if (PART_DataGrid.ItemContainerGenerator.ContainerFromIndex(index) is DataGridRow row)
            {
                row.Header = CurrentPageOffset + index + 1;
            }
        }
    }

    private void CollapseAllRowDetails()
    {
        if (PART_DataGrid is null)
        {
            return;
        }

        for (var index = 0; index < PART_DataGrid.Items.Count; index++)
        {
            if (PART_DataGrid.ItemContainerGenerator.ContainerFromIndex(index) is DataGridRow row)
            {
                row.DetailsVisibility = Visibility.Collapsed;
                UpdateRowDetailsVisualState(row);
            }
        }
    }

    private void UpdateRowDetailsVisualState(DataGridRow row, Button? toggleButton = null)
    {
        var button = toggleButton ?? FindVisualChild<Button>(row);
        if (button is null)
        {
            return;
        }

        var icon = FindVisualChild<PackIcon>(button);
        if (icon is null)
        {
            return;
        }

        icon.Kind = row.DetailsVisibility == Visibility.Visible
            ? PackIconKind.ChevronDown
            : PackIconKind.ChevronRight;
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null)
        {
            return null;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var nestedChild = FindVisualChild<T>(child);
            if (nestedChild is not null)
            {
                return nestedChild;
            }
        }

        return null;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed record SortDefinition(string ColumnKey, EstDataColumnDefinition Definition, ListSortDirection Direction);
}
