using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace app_desktop_base.Models;

public sealed class EstDataCellPresentation
{
    public string? Text { get; init; }

    public PackIconKind? IconKind { get; init; }

    public Brush? Foreground { get; init; }

    public Brush? Background { get; init; }

    public Brush? BorderBrush { get; init; }

    public Thickness? BorderThickness { get; init; }

    public Thickness? Padding { get; init; }

    public CornerRadius? CornerRadius { get; init; }

    public FontWeight? FontWeight { get; init; }

    public bool? UseContainer { get; init; }

    public bool? IsLink { get; init; }

    public bool? Underline { get; init; }

    public ICommand? Command { get; init; }

    public object? CommandParameter { get; init; }

    public string? ToolTip { get; init; }

    public ContextMenu? ContextMenu { get; init; }
}
