using System.Collections.ObjectModel;
using Chapi.Domain.Documentation;

namespace Chapi.Presentation.Features.Documentation.ViewModels;

public class DocumentationIndexItem
{
    public required string Number { get; init; }
    public required string Title { get; init; }
    public required DocSection Section { get; init; }
    public ObservableCollection<DocumentationIndexItem> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;
}
