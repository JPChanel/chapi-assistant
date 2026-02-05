using Chapi.Domain.Enums;

namespace Chapi.Domain.Entities;

/// <summary>
/// Credencial de Git genérica para cualquier proveedor.
/// </summary>
public class GitCredential
{
    public GitProvider Provider { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
