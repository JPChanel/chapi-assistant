using Chapi.Domain.Enums;
using Chapi.Domain.Interfaces;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Chapi.Presentation.ViewModels;

/// <summary>
/// ViewModel para el diálogo de selección de proveedor Git.
/// </summary>
public class GitProviderSelectionViewModel : INotifyPropertyChanged
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
        set
        {
            _isAuthenticating = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        set
        {
            _isAuthenticated = value;
            OnPropertyChanged();
        }
    }

    public string AuthenticatedUser
    {
        get => _authenticatedUser;
        set
        {
            _authenticatedUser = value;
            OnPropertyChanged();
        }
    }

    public GitProvider AuthenticatedProvider
    {
        get => _authenticatedProvider;
        set
        {
            _authenticatedProvider = value;
            OnPropertyChanged();
        }
    }

    public ICommand LoginGitHubCommand { get; }
    public ICommand LoginGitLabCommand { get; }

    public GitProviderSelectionViewModel(IGitAuthProviderFactory providerFactory)
    {
        _providerFactory = providerFactory;

        LoginGitHubCommand = new AsyncRelayCommand(async _ => await LoginAsync(GitProvider.GitHub));
        LoginGitLabCommand = new AsyncRelayCommand(async _ => await LoginAsync(GitProvider.GitLab));
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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

