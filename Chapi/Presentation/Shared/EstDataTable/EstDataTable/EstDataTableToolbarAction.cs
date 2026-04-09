using System.Windows.Input;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;

namespace app_desktop_base.Models.EstDataTable;

public sealed class EstDataTableToolbarAction
{
    public string Label { get; init; } = string.Empty;

    public ICommand? Command { get; init; }

    public object? CommandParameter { get; init; }

    public string ToolTip { get; init; } = string.Empty;

    public PackIconKind? IconKind { get; init; }

    public ContextMenu? ContextMenu { get; init; }

    public bool IsPrimary { get; init; }

    public bool IsVisible { get; init; } = true;

    public bool HasIcon => IconKind.HasValue;

    public bool HasContextMenu => ContextMenu is not null;
}
