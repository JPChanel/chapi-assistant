using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace app_desktop_base.Models.EstDataTable;

public sealed class EstDataColumnDefinition
{
    public string Header { get; init; } = string.Empty;

    public string AccessorKey { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public double Width { get; init; }

    public double Size { get; init; }

    public double MinWidth { get; init; } = 80;

    public double MaxWidth { get; init; } = double.PositiveInfinity;

    public bool IsEditable { get; init; } = true;

    public bool EnableEditing { get; init; } = true;

    public bool IsFilterable { get; init; } = true;

    public bool IsSortable { get; init; } = true;

    public bool IsVisible { get; init; } = true;

    public bool CanUserResize { get; init; } = true;

    public int Priority { get; init; }

    public string? StringFormat { get; init; }

    public TextAlignment TextAlignment { get; init; } = TextAlignment.Left;

    public string? ColumnKey { get; init; }

    public EstDataCellVariant CellVariant { get; init; } = EstDataCellVariant.Text;

    public Func<object?, EstDataCellVariant>? CellVariantSelector { get; init; }

    public object? Value { get; init; }

    public Func<object?, object?>? ValueSelector { get; init; }

    public string? Text { get; init; }

    public Func<object?, string?>? TextSelector { get; init; }

    public PackIconKind? IconKind { get; init; }

    public Func<object?, PackIconKind?>? IconSelector { get; init; }

    public Brush? Foreground { get; init; }

    public Func<object?, Brush?>? ForegroundSelector { get; init; }

    public Brush? Background { get; init; }

    public Func<object?, Brush?>? BackgroundSelector { get; init; }

    public Brush? BorderBrush { get; init; }

    public Func<object?, Brush?>? BorderBrushSelector { get; init; }

    public Thickness? BorderThickness { get; init; }

    public Func<object?, Thickness?>? BorderThicknessSelector { get; init; }

    public Thickness? Padding { get; init; }

    public Func<object?, Thickness?>? PaddingSelector { get; init; }

    public CornerRadius? CornerRadius { get; init; }

    public Func<object?, CornerRadius?>? CornerRadiusSelector { get; init; }

    public FontWeight? FontWeight { get; init; }

    public Func<object?, FontWeight?>? FontWeightSelector { get; init; }

    public bool UseContainer { get; init; }

    public Func<object?, bool>? UseContainerSelector { get; init; }

    public bool IsLink { get; init; }

    public Func<object?, bool>? IsLinkSelector { get; init; }

    public bool Underline { get; init; }

    public Func<object?, bool>? UnderlineSelector { get; init; }

    public ICommand? ClickCommand { get; init; }

    public Func<object?, ICommand?>? ClickCommandSelector { get; init; }

    public object? ClickCommandParameter { get; init; }

    public Func<object?, object?>? ClickCommandParameterSelector { get; init; }

    public string? ToolTip { get; init; }

    public Func<object?, string?>? ToolTipSelector { get; init; }

    public ContextMenu? ContextMenu { get; init; }

    public Func<object?, ContextMenu?>? ContextMenuSelector { get; init; }

    public Func<object?, EstDataCellPresentation?>? CellPresentationSelector { get; init; }

    public string ResolvedAccessorKey => !string.IsNullOrWhiteSpace(AccessorKey) ? AccessorKey : Path;

    public string ResolvedColumnKey =>
        !string.IsNullOrWhiteSpace(ColumnKey)
            ? ColumnKey
            : !string.IsNullOrWhiteSpace(ResolvedAccessorKey)
                ? ResolvedAccessorKey
                : Header;

    public double ResolvedWidth => Width > 0 ? Width : Size;

    public bool CanEdit => IsEditable && EnableEditing;

    public bool HasCustomCellPresentation =>
        ValueSelector is not null
        || TextSelector is not null
        || CellVariant != EstDataCellVariant.Text
        || CellVariantSelector is not null
        || CellPresentationSelector is not null
        || IconKind is not null
        || IconSelector is not null
        || Foreground is not null
        || ForegroundSelector is not null
        || Background is not null
        || BackgroundSelector is not null
        || BorderBrush is not null
        || BorderBrushSelector is not null
        || BorderThickness is not null
        || BorderThicknessSelector is not null
        || Padding is not null
        || PaddingSelector is not null
        || CornerRadius is not null
        || CornerRadiusSelector is not null
        || FontWeight is not null
        || FontWeightSelector is not null
        || UseContainer
        || UseContainerSelector is not null
        || IsLink
        || IsLinkSelector is not null
        || Underline
        || UnderlineSelector is not null
        || ClickCommand is not null
        || ClickCommandSelector is not null
        || ClickCommandParameter is not null
        || ClickCommandParameterSelector is not null
        || ContextMenu is not null
        || ContextMenuSelector is not null
        || !string.IsNullOrWhiteSpace(Text)
        || !string.IsNullOrWhiteSpace(ToolTip)
        || ToolTipSelector is not null;

    public bool CanResolveValue =>
        !string.IsNullOrWhiteSpace(ResolvedAccessorKey)
        || ValueSelector is not null
        || CellPresentationSelector is not null
        || TextSelector is not null
        || Value is not null
        || !string.IsNullOrWhiteSpace(Text);
}
