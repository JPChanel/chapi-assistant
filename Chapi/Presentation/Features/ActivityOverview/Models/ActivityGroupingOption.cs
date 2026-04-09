using CommunityToolkit.Mvvm.ComponentModel;

namespace Chapi.Presentation.Features.ActivityOverview.Models;

public sealed partial class ActivityGroupingOption : ObservableObject
{
    public string Label { get; init; } = string.Empty;
    public string PropertyName { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}
