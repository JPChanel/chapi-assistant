using Chapi.Domain.Enums;
using Chapi.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Chapi.Infrastructure.Services.Auth;

/// <summary>
/// Factory para obtener el proveedor de autenticación correcto.
/// </summary>
public class GitAuthProviderFactory : IGitAuthProviderFactory
{
    private readonly IServiceProvider _serviceProvider;

    public GitAuthProviderFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IGitAuthProvider GetProvider(GitProvider provider)
    {
        return provider switch
        {
            GitProvider.GitHub => _serviceProvider.GetRequiredService<GitHubOAuthProvider>(),
            GitProvider.GitLab => _serviceProvider.GetRequiredService<GitLabOAuthProvider>(),
            _ => throw new NotSupportedException($"Provider {provider} no está soportado")
        };
    }

    public GitProvider DetectProviderFromUrl(string remoteUrl)
    {
        if (string.IsNullOrEmpty(remoteUrl))
            return GitProvider.Unknown;

        var url = remoteUrl.ToLowerInvariant();

        // GitHub
        if (url.Contains("github.com"))
            return GitProvider.GitHub;

        // GitLab
        if (url.Contains("gitlab.com") || url.Contains("gitlab"))
            return GitProvider.GitLab;

        // Bitbucket
        if (url.Contains("bitbucket.org"))
            return GitProvider.Bitbucket;

        // Azure DevOps
        if (url.Contains("dev.azure.com") || url.Contains("visualstudio.com"))
            return GitProvider.AzureDevOps;

        return GitProvider.Unknown;
    }
}
