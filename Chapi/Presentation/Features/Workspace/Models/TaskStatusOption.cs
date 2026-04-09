using Chapi.Domain.Enums;

namespace Chapi.Presentation.Features.Workspace.Models;

public sealed class TaskStatusOption
{
    public WorkspaceTaskStatus Value { get; init; }
    public string Label { get; init; } = string.Empty;
}
