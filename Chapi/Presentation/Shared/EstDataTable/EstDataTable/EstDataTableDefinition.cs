using System.Collections.Generic;

namespace app_desktop_base.Models.EstDataTable;

public class EstDataTableDefinition
{
    public IEnumerable<EstDataColumnDefinition>? Columns { get; init; }

    public IEnumerable<EstDataTableToolbarAction>? TopToolbarCustomActions { get; init; }

    public IEnumerable<EstDataTableRowAction>? RowActions { get; init; }
}

public sealed class EstDataTableDefinition<T> : EstDataTableDefinition
{
}
