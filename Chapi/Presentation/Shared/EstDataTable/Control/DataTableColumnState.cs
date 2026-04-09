using app_desktop_base.Models.EstDataTable;
using CommunityToolkit.Mvvm.ComponentModel;

namespace app_desktop_base.Controls;

public partial class DataTableColumnState : ObservableObject
{
    [ObservableProperty]
    private string filterText = string.Empty;

    [ObservableProperty]
    private bool isVisible;

    public DataTableColumnState(EstDataColumnDefinition definition)
    {
        Definition = definition;
        IsVisible = definition.IsVisible;
        DefaultVisible = definition.IsVisible;
        Placeholder = $"Filtrar {definition.Header}";
    }

    public EstDataColumnDefinition Definition { get; }

    public string Header => Definition.Header;

    public string Path => Definition.ResolvedColumnKey;

    public bool IsFilterable => Definition.IsFilterable;

    public bool IsSortable => Definition.IsSortable;

    public string Placeholder { get; }

    public bool DefaultVisible { get; }

    public string VisibilityActionLabel => IsVisible ? "Ocultar" : "Mostrar";

    partial void OnIsVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(VisibilityActionLabel));
    }
}
