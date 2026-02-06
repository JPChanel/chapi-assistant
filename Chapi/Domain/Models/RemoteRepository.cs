namespace Chapi.Domain.Models;

public class RemoteRepository
{
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Owner => FullName.Contains('/') ? FullName.Split('/')[0] : "Other";
    public string CloneUrl { get; set; } = string.Empty;
    public bool IsPrivate { get; set; }
    public string? Description { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
