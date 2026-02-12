using System;
using System.Collections.Generic;

namespace Chapi.Domain.Entities.Workspace;

public class WorkspaceData
{
    public string ProjectPath { get; set; } = string.Empty;
    public List<WorkspaceTask> Tasks { get; set; } = new();
    public List<DeploymentAsset> DeploymentQueue { get; set; } = new();
    public string SessionNotes { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; } = DateTime.Now;
}
