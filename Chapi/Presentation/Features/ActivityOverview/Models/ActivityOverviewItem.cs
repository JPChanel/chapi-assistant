using Chapi.Domain.Enums;

namespace Chapi.Presentation.Features.ActivityOverview.Models;

public sealed class ActivityOverviewItem
{
    public Guid TaskId { get; init; }
    public string GroupLabel { get; init; } = string.Empty;
    public bool IsGroupHeader { get; init; }
    public string SummaryText { get; init; } = string.Empty;
    public string ProjectPath { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;
    public TaskPriority Priority { get; init; }
    public string PriorityLabel { get; init; } = string.Empty;
    public WorkspaceTaskStatus Status { get; init; }
    public string StatusLabel { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string GroupStatus { get; init; } = string.Empty;
    public string GroupProject { get; init; } = string.Empty;
    public string GroupOwner { get; init; } = string.Empty;
    public string GroupMonth { get; init; } = string.Empty;
    public string GroupDay { get; init; } = string.Empty;
}
