using Chapi.Domain.Enums;

namespace Chapi.Domain.Entities.Workspace;

public sealed class WorkspaceActivityRecord
{
    public Guid TaskId { get; set; }
    public string ProjectPath { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public TaskPriority Priority { get; set; } = TaskPriority.Media;
    public WorkspaceTaskStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
