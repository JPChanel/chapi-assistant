using Chapi.Application.UseCases.Projects;
using CommunityToolkit.Mvvm.Input;
using Chapi.Domain.Interfaces;
using Chapi.Domain.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;
using Chapi.Presentation.Shared.Mvvm;

namespace Chapi.Presentation.Features.Projects.ViewModels;

public class CloneRepositoryViewModel : ViewModelBase
{
    private readonly IGitAuthProviderFactory _authFactory;
    private readonly ICredentialStorageService _credentialStorage;
    private readonly CloneProjectUseCase _cloneUseCase;

    private ObservableCollection<RemoteRepository> _githubRepos = new();
    private ObservableCollection<RemoteRepository> _gitlabRepos = new();
    private ObservableCollection<RemoteRepository> _filteredRepos = new();
    private string _searchText = string.Empty;
    private string _url = string.Empty;
    private string _localPath = string.Empty;
    private bool _isLoading;
    private int _selectedTabIndex;
    private RemoteRepository? _selectedRepo;
    private System.Threading.Timer? _searchDebounceTimer;

    private bool _isGitHubAuthenticated;
    private bool _isGitLabAuthenticated;

    public ObservableCollection<RemoteRepository> GitHubRepos => _githubRepos;
    public ObservableCollection<RemoteRepository> GitLabRepos => _gitlabRepos;
    public ObservableCollection<RemoteRepository> FilteredRepos => _filteredRepos;

    public ICollectionView FilteredView { get; }

    public bool IsGitHubAuthenticated
    {
        get => _isGitHubAuthenticated;
        set => SetProperty(ref _isGitHubAuthenticated, value);
    }

    public bool IsGitLabAuthenticated
    {
        get => _isGitLabAuthenticated;
        set => SetProperty(ref _isGitLabAuthenticated, value);
    }

    public bool IsCurrentProviderAuthenticated => SelectedTabIndex == 0 ? IsGitHubAuthenticated : (SelectedTabIndex == 1 ? IsGitLabAuthenticated : true);

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _searchDebounceTimer?.Dispose();
                _searchDebounceTimer = new System.Threading.Timer(_ =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(FilterRepos);
                }, null, 250, Timeout.Infinite);
            }
        }
    }

    public string Url
    {
        get => _url;
        set => SetProperty(ref _url, value);
    }

    public string LocalPath
    {
        get => _localPath;
        set => SetProperty(ref _localPath, value);
    }

    private string _loadingMessage = "Fetching repositories...";
    public string LoadingMessage
    {
        get => _loadingMessage;
        set => SetProperty(ref _loadingMessage, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetProperty(ref _selectedTabIndex, value))
            {
                OnPropertyChanged(nameof(IsCurrentProviderAuthenticated));
                FilterRepos();
            }
        }
    }

    public RemoteRepository? SelectedRepo
    {
        get => _selectedRepo;
        set
        {
            if (SetProperty(ref _selectedRepo, value) && value != null)
            {
                Url = value.CloneUrl;
            }
        }
    }

    public ICommand BrowseCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand LoginCommand { get; }

    public CloneRepositoryViewModel(
        IGitAuthProviderFactory authFactory,
        ICredentialStorageService credentialStorage,
        CloneProjectUseCase cloneUseCase)
    {
        _authFactory = authFactory;
        _credentialStorage = credentialStorage;
        _cloneUseCase = cloneUseCase;

        FilteredView = CollectionViewSource.GetDefaultView(FilteredRepos);
        FilteredView.GroupDescriptions.Add(new PropertyGroupDescription("Owner"));

        BrowseCommand = new AsyncRelayCommand(ExecuteBrowseAsync);
        RefreshCommand = new AsyncRelayCommand(LoadReposAsync);
        LoginCommand = new AsyncRelayCommand<object?>(ExecuteLoginAsync);

        LocalPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos");

        Task.Run(LoadReposAsync);
    }

    private async Task ExecuteLoginAsync(object? obj)
    {
        if (obj is string providerName && Enum.TryParse<Domain.Enums.GitProvider>(providerName, out var provider))
        {
            try
            {
                IsLoading = true;
                LoadingMessage = $"Conectando con {provider}...";
                var authProvider = _authFactory.GetProvider(provider);
                var result = await authProvider.AuthenticateAsync();

                if (result.IsSuccess)
                {
                    if (provider == Domain.Enums.GitProvider.GitHub) IsGitHubAuthenticated = true;
                    else if (provider == Domain.Enums.GitProvider.GitLab) IsGitLabAuthenticated = true;

                    OnPropertyChanged(nameof(IsCurrentProviderAuthenticated));
                    await LoadReposAsync();
                }
            }
            catch (Exception) { }
            finally
            {
                IsLoading = false;
                LoadingMessage = "Fetching repositories...";
            }
        }
    }

    private async Task LoadReposAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            await Task.WhenAll(
                LoadProviderReposAsync(Domain.Enums.GitProvider.GitHub, _githubRepos),
                LoadProviderReposAsync(Domain.Enums.GitProvider.GitLab, _gitlabRepos)
            );
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(FilterRepos);
        }
        catch (Exception) { }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadProviderReposAsync(Domain.Enums.GitProvider provider, ObservableCollection<RemoteRepository> targetList)
    {
        var cred = await _credentialStorage.GetCredentialAsync(provider.ToString());
        string token = cred.HasValue ? cred.Value.token : string.Empty;
        bool isAuthenticated = !string.IsNullOrEmpty(token);

        if (isAuthenticated)
        {
            var authProvider = _authFactory.GetProvider(provider);

            // 1. Validar si el token sigue siendo válido
            if (!await authProvider.ValidateTokenAsync(token))
            {
                // 2. Si no es válido, intentar refrescar (crítico para GitLab que expira)
                var refreshResult = await authProvider.RefreshTokenAsync();
                if (refreshResult.IsSuccess)
                {
                    token = refreshResult.Data.AccessToken;
                    // El storage se actualiza dentro de RefreshTokenAsync
                }
                else
                {
                    isAuthenticated = false;
                }
            }
        }

        // Actualizar estado de autenticación en UI
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (provider == Domain.Enums.GitProvider.GitHub) IsGitHubAuthenticated = isAuthenticated;
            else if (provider == Domain.Enums.GitProvider.GitLab) IsGitLabAuthenticated = isAuthenticated;
            OnPropertyChanged(nameof(IsCurrentProviderAuthenticated));
        });

        if (!isAuthenticated) return;

        try
        {
            var authProvider = _authFactory.GetProvider(provider);
            var result = await authProvider.GetRepositoriesAsync(token);

            if (result.IsSuccess)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    targetList.Clear();
                    foreach (var repo in result.Data) targetList.Add(repo);
                });
            }
        }
        catch (Exception ex)
        {

        }
    }

    private void FilterRepos()
    {
        var source = SelectedTabIndex == 0 ? _githubRepos : _gitlabRepos;
        var query = source.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(r =>
                (r.FullName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.Name?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var sortedList = query.OrderBy(r => r.FullName).ToList();
        if (FilteredRepos.SequenceEqual(sortedList)) return;

        FilteredRepos.Clear();
        foreach (var repo in sortedList) FilteredRepos.Add(repo);
    }

    private async Task ExecuteBrowseAsync()
    {
        LoadingMessage = "Seleccionando carpeta...";
        IsLoading = true;

        // Pequeño delay para que la animación de entrada del overlay se vea suave
        await Task.Delay(50);

        string? selectedPath = await Task.Run(() =>
        {
            string? path = null;
            // El diálogo de carpetas requiere un hilo en estado STA
            var thread = new System.Threading.Thread(() =>
            {
                using (var dialog = new FolderBrowserDialog { SelectedPath = LocalPath })
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        path = dialog.SelectedPath;
                    }
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();
            return path;
        });

        if (selectedPath != null)
        {
            LocalPath = selectedPath;
        }

        IsLoading = false;
        LoadingMessage = "Fetching repositories...";
    }
}
