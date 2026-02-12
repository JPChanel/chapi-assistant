using System;
using System.IO;

namespace Chapi.Domain.Entities.Workspace;

public class DeploymentAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FilePath { get; set; } = string.Empty;
    public bool IsPending { get; set; } = true;
    public DateTime AddedAt { get; set; } = DateTime.Now;

    public string FileName => !string.IsNullOrEmpty(FilePath) 
        ? Path.GetFileName(FilePath) 
        : string.Empty;

    public string Extension => !string.IsNullOrEmpty(FilePath)
        ? Path.GetExtension(FilePath).ToLower()
        : string.Empty;

    public bool Exists => File.Exists(FilePath);
}
