using CommunityToolkit.Mvvm.Input;
using Chapi.Domain.Enums;
using Chapi.Domain.Interfaces;
using Chapi.Presentation.Shared.Mvvm;

namespace Chapi.Presentation.Features.Git.ViewModels;

/// <summary>
/// ViewModel para el diálogo de selección de proveedor Git.
/// </summary>
public class GitProviderSelectionViewModel : ViewModelBase
{
    private readonly IGitAuthProviderFactory _providerFactory;
    private bool _isAuthenticating;
    private string _statusMessage = string.Empty;
    private bool _isAuthenticated;
    private string _authenticatedUser = string.Empty;
    private GitProvider _authenticatedProvider;

    public bool IsAuthenticating
    {
        get => _isAuthenticating;
        set => SetProperty(ref _isAuthenticating, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        set => SetProperty(ref _isAuthenticated, value);
    }

    public string AuthenticatedUser
    {
        get => _authenticatedUser;
        set => SetProperty(ref _authenticatedUser, value);
    }

    public GitProvider AuthenticatedProvider
    {
        get => _authenticatedProvider;
        set => SetProperty(ref _authenticatedProvider, value);
    }

    public IAsyncRelayCommand LoginGitHubCommand { get; }
    public IAsyncRelayCommand LoginGitLabCommand { get; }

    public GitProviderSelectionViewModel(IGitAuthProviderFactory providerFactory)
    {
        _providerFactory = providerFactory;

        LoginGitHubCommand = new AsyncRelayCommand(() => LoginAsync(GitProvider.GitHub));
        LoginGitLabCommand = new AsyncRelayCommand(() => LoginAsync(GitProvider.GitLab));
    }

    private async Task LoginAsync(GitProvider provider)
    {
        try
        {
            IsAuthenticating = true;
            StatusMessage = $"Autenticando con {provider}...";

            var authProvider = _providerFactory.GetProvider(provider);
            var result = await authProvider.AuthenticateAsync();

            if (result.IsSuccess)
            {
                IsAuthenticated = true;
                AuthenticatedUser = result.Data.Username;
                AuthenticatedProvider = provider;
                StatusMessage = $"✅ Conectado como {result.Data.Username}";
            }
            else
            {
                StatusMessage = $"❌ Error: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsAuthenticating = false;
        }
    }
}

