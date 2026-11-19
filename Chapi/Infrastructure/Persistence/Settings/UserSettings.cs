using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Chapi.Infrastructure.Persistence.Settings;

public static class UserSettingsService
{
    private const string EncryptedPrefix = "enc:";
    private static readonly string AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chapi");
    private static readonly string SettingsFilePath = Path.Combine(AppDataPath, "user.api.settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    static UserSettingsService()
    {
        Directory.CreateDirectory(AppDataPath);
    }

    public static UserApiSettings LoadSettings()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new UserApiSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<UserApiSettings>(json, JsonOptions) ?? new UserApiSettings();

            bool hadPlaintextSecrets = IsPlaintextSecret(settings.GeminiApiKey)
                || IsPlaintextSecret(settings.OpenAiApiKey)
                || IsPlaintextSecret(settings.ClaudeApiKey)
                || IsPlaintextSecret(settings.ProxyPass)
                || IsPlaintextSecret(settings.GitHubToken);

            settings.GeminiApiKey = UnprotectValue(settings.GeminiApiKey);
            settings.OpenAiApiKey = UnprotectValue(settings.OpenAiApiKey);
            settings.ClaudeApiKey = UnprotectValue(settings.ClaudeApiKey);
            settings.ProxyPass = UnprotectValue(settings.ProxyPass);
            settings.GitHubToken = UnprotectValue(settings.GitHubToken);

            if (hadPlaintextSecrets)
            {
                SaveSettings(settings);
            }

            return settings;
        }
        catch (Exception)
        {
            return new UserApiSettings();
        }
    }

    public static void SaveSettings(UserApiSettings settings)
    {
        var persisted = new UserApiSettings
        {
            InstallId = settings.InstallId,
            GeminiApiKey = ProtectValue(settings.GeminiApiKey),
            OpenAiApiKey = ProtectValue(settings.OpenAiApiKey),
            ClaudeApiKey = ProtectValue(settings.ClaudeApiKey),
            PreferredAiProvider = settings.PreferredAiProvider,
            ThemeMode = settings.ThemeMode,
            ProxyEnabled = settings.ProxyEnabled,
            ProxyUrl = settings.ProxyUrl,
            ProxyUser = settings.ProxyUser,
            ProxyPass = ProtectValue(settings.ProxyPass),
            GitHubToken = ProtectValue(settings.GitHubToken),
            GitHubUserLogin = settings.GitHubUserLogin,
            GitHubUserName = settings.GitHubUserName,
            GitHubUserAvatar = settings.GitHubUserAvatar
        };

        var json = JsonSerializer.Serialize(persisted, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFilePath, json);
    }

    private static bool IsPlaintextSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!value.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
            return true;

        // Si empieza con "enc:" pero contiene directamente una clave en texto plano (corrupta o pegada por error)
        return ContainsKnownPlaintextSecret(value);
    }

    private static bool ContainsKnownPlaintextSecret(string value)
    {
        return value.Contains("AIzaSy", StringComparison.Ordinal)
            || value.Contains("sk-ant-", StringComparison.Ordinal)
            || value.Contains("sk-", StringComparison.Ordinal)
            || value.Contains("ghp_", StringComparison.Ordinal)
            || value.Contains("github_pat_", StringComparison.Ordinal);
    }

    private static bool TryExtractPlaintextSecret(string value, out string secret)
    {
        string[] prefixes = ["AIzaSy", "sk-ant-", "sk-", "ghp_", "github_pat_"];
        foreach (var p in prefixes)
        {
            int idx = value.IndexOf(p, StringComparison.Ordinal);
            if (idx >= 0)
            {
                secret = value[idx..].Trim();
                return true;
            }
        }

        secret = string.Empty;
        return false;
    }

    private static string ProtectValue(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
            return string.Empty;

        if (plainText.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
            return plainText;

        try
        {
            var bytes = Encoding.UTF8.GetBytes(plainText);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return EncryptedPrefix + Convert.ToBase64String(protectedBytes);
        }
        catch
        {
            return plainText;
        }
    }

    private static string UnprotectValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (!value.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
            return value;

        try
        {
            var base64 = value[EncryptedPrefix.Length..];
            var protectedBytes = Convert.FromBase64String(base64);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // Si DPAPI falla (por ejemplo, hash corrupto o clave en texto plano mezclada en el prefijo enc:)
            if (TryExtractPlaintextSecret(value, out var recoveredSecret))
            {
                return recoveredSecret;
            }

            return string.Empty;
        }
    }
}

public class UserApiSettings
{
    public string InstallId { get; set; } = string.Empty;
    public string GeminiApiKey { get; set; } = string.Empty;
    public string OpenAiApiKey { get; set; } = string.Empty;
    public string ClaudeApiKey { get; set; } = string.Empty;
    public string PreferredAiProvider { get; set; } = "Gemini"; // Gemini, OpenAI, Claude
    public string ThemeMode { get; set; } = "System"; // System, Dark, Light

    public bool ProxyEnabled { get; set; } = false;
    public string ProxyUrl { get; set; } = string.Empty;
    public string ProxyUser { get; set; } = string.Empty;
    public string ProxyPass { get; set; } = string.Empty;

    // GitHub Auth
    public string GitHubToken { get; set; } = string.Empty;
    public string GitHubUserLogin { get; set; } = string.Empty;
    public string GitHubUserName { get; set; } = string.Empty;
    public string GitHubUserAvatar { get; set; } = string.Empty;
}
