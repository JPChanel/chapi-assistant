using Chapi.Application.Interfaces;
using Chapi.Domain.Interfaces;
using Chapi.Infrastructure.Configuration;
using Chapi.Infrastructure.Persistence.Settings;
using Microsoft.Extensions.Options;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;

namespace Chapi.Infrastructure.Services;

public sealed class SupabaseUsageTelemetryService : IUsageTelemetryService
{
    private const string EventTypeAppOpen = "app_open";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly ICredentialStorageService _credentialStorage;
    private readonly SupabaseTelemetryConfig _config;
    private readonly SemaphoreSlim _queueLock = new(1, 1);
    private readonly string _queueFilePath;

    public SupabaseUsageTelemetryService(
        HttpClient httpClient,
        ICredentialStorageService credentialStorage,
        IOptions<SupabaseTelemetryConfig> config)
    {
        _httpClient = httpClient;
        _credentialStorage = credentialStorage;
        _config = config.Value;

        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Chapi");
        Directory.CreateDirectory(appDataPath);
        _queueFilePath = Path.Combine(appDataPath, "usage.telemetry.queue.json");
    }

    public async Task TrackAppOpenAsync()
    {
        if (!IsConfigured())
        {
            return;
        }

        var telemetryEvent = await BuildEventAsync(EventTypeAppOpen);
        await EnqueueAsync(telemetryEvent);
        await FlushPendingAsync();
    }

    public async Task FlushPendingAsync()
    {
        if (!IsConfigured() || !NetworkInterface.GetIsNetworkAvailable())
        {
            return;
        }

        await _queueLock.WaitAsync();
        try
        {
            var pending = await LoadQueueInternalAsync();
            if (pending.Count == 0)
            {
                return;
            }

            var requestUri = BuildUpsertUri();
            var payload = JsonSerializer.Serialize(pending, JsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("apikey", _config.AnonKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.AnonKey);
            request.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            await SaveQueueInternalAsync([]);
        }
        catch
        {
        }
        finally
        {
            _queueLock.Release();
        }
    }

    private bool IsConfigured()
    {
        return _config.Enabled
            && !string.IsNullOrWhiteSpace(_config.Url)
            && !string.IsNullOrWhiteSpace(_config.AnonKey)
            && !string.IsNullOrWhiteSpace(_config.TableName);
    }

    private async Task<UsageTelemetryEvent> BuildEventAsync(string eventType)
    {
        var settings = UserSettingsService.LoadSettings();
        var installId = EnsureInstallId(settings);
        var gitHubLogin = settings.GitHubUserLogin?.Trim() ?? string.Empty;
        var gitHubName = settings.GitHubUserName?.Trim() ?? string.Empty;

        string userLogin = gitHubLogin;
        string userName = gitHubName;

        if (string.IsNullOrWhiteSpace(userLogin))
        {
            var gitLabCredential = await _credentialStorage.GetCredentialAsync("GitLab");
            if (gitLabCredential.HasValue)
            {
                userLogin = gitLabCredential.Value.username;
                userName = gitLabCredential.Value.username;
            }
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            userName = userLogin;
        }

        if (string.IsNullOrWhiteSpace(userLogin))
        {
            userLogin = Environment.UserName;
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            userName = Environment.UserName;
        }

        return new UsageTelemetryEvent
        {
            InstallId = installId,
            UserLogin = userLogin,
            UserName = userName,
            AppVersion = ResolveAppVersion(),
            EventType = eventType,
            MachineName = Environment.MachineName,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static string EnsureInstallId(UserApiSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.InstallId))
        {
            return settings.InstallId;
        }

        settings.InstallId = Guid.NewGuid().ToString("N");
        UserSettingsService.SaveSettings(settings);
        return settings.InstallId;
    }

    private static string ResolveAppVersion()
    {
        var assembly = System.Reflection.Assembly.GetEntryAssembly();
        if (assembly == null)
        {
            return "unknown";
        }

        var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion;
        return string.IsNullOrWhiteSpace(version) ? "unknown" : version.Split('+')[0];
    }

    private string BuildUpsertUri()
    {
        return $"{_config.Url.TrimEnd('/')}/rest/v1/{_config.TableName}?on_conflict={Uri.EscapeDataString("install_id")}";
    }

    private async Task EnqueueAsync(UsageTelemetryEvent telemetryEvent)
    {
        await _queueLock.WaitAsync();
        try
        {
            var pending = await LoadQueueInternalAsync();
            pending.Add(telemetryEvent);
            await SaveQueueInternalAsync(MergePendingEvents(pending));
        }
        finally
        {
            _queueLock.Release();
        }
    }

    private static List<UsageTelemetryEvent> MergePendingEvents(List<UsageTelemetryEvent> pending)
    {
        var merged = new Dictionary<string, UsageTelemetryEvent>(StringComparer.OrdinalIgnoreCase);

        foreach (var telemetryEvent in pending)
        {
            merged[telemetryEvent.InstallId] = telemetryEvent;
        }

        return merged.Values.ToList();
    }

    private async Task<List<UsageTelemetryEvent>> LoadQueueInternalAsync()
    {
        if (!File.Exists(_queueFilePath))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(_queueFilePath);
            return JsonSerializer.Deserialize<List<UsageTelemetryEvent>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task SaveQueueInternalAsync(List<UsageTelemetryEvent> pending)
    {
        if (pending.Count == 0)
        {
            if (File.Exists(_queueFilePath))
            {
                File.Delete(_queueFilePath);
            }

            return;
        }

        var json = JsonSerializer.Serialize(pending, JsonOptions);
        await File.WriteAllTextAsync(_queueFilePath, json);
    }

    private sealed class UsageTelemetryEvent
    {
        public string InstallId { get; init; } = string.Empty;
        public string UserLogin { get; init; } = string.Empty;
        public string UserName { get; init; } = string.Empty;
        public string AppVersion { get; init; } = string.Empty;
        public string EventType { get; init; } = string.Empty;
        public string MachineName { get; init; } = string.Empty;
        public DateTime UpdatedAt { get; init; }
    }
}
