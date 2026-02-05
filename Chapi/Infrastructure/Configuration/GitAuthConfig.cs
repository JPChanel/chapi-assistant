namespace Chapi.Infrastructure.Configuration;

/// <summary>
/// Configuración de autenticación Git.
/// </summary>
public class GitAuthConfig
{
    public GitHubConfig GitHub { get; set; } = new();
    public GitLabConfig GitLab { get; set; } = new();
}

public class GitHubConfig
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = "http://localhost:8888/callback";
    public string Scope { get; set; } = "repo user";
}

public class GitLabConfig
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = "http://localhost:8889/callback";
    public string Scope { get; set; } = "api read_user read_repository write_repository";
    public string BaseUrl { get; set; } = "https://gitlab.com";
}
