using System.Windows.Input;
using MaterialDesignThemes.Wpf;

namespace app_desktop_base.Models.EstDataTable;

public sealed class EstDataTableRowAction
{
    public string Label { get; init; } = string.Empty;

    public ICommand? Command { get; init; }

    public string ToolTip { get; init; } = string.Empty;

    public PackIconKind? IconKind { get; init; }

    public bool IsVisible { get; init; } = true;

    public bool IsDestructive { get; init; }

    public bool HasIcon => IconKind.HasValue;
}
