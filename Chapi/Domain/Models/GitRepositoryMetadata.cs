namespace Chapi.Domain.Models;

/// <summary>
/// Contenedor consolidado de metadatos de un repositorio Git para optimizar llamadas remotas (WSL).
/// </summary>
public class GitRepositoryMetadata
{
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string RemoteUrl { get; set; } = string.Empty;
    public string CurrentBranch { get; set; } = string.Empty;
    public int Ahead { get; set; }
    public int Behind { get; set; }
    public bool HasUpstream { get; set; }
}
