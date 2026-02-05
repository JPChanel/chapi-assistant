using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Domain.Enums;
using Chapi.Domain.Interfaces;
using Chapi.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chapi.Infrastructure.Services.Auth;

/// <summary>
/// Proveedor de autenticación OAuth para GitLab.
/// </summary>
public class GitLabOAuthProvider : IGitAuthProvider
{
    private readonly ICredentialStorageService _credentialStorage;
    private readonly HttpClient _httpClient;
    private readonly GitLabConfig _config;

    public GitProvider Provider => GitProvider.GitLab;

    public GitLabOAuthProvider(
        ICredentialStorageService credentialStorage, 
        HttpClient httpClient,
        IOptions<GitAuthConfig> config)
    {
        _credentialStorage = credentialStorage;
        _httpClient = httpClient;
        _config = config.Value.GitLab;
    }

    public async Task<Result<GitCredential>> AuthenticateAsync()
    {
        try
        {
            // 1. Verificar credenciales existentes
            var existing = await _credentialStorage.GetCredentialAsync("GitLab");
            if (existing.HasValue && await ValidateTokenAsync(existing.Value.token))
            {
                return await GetUserInfoAsync(existing.Value.token);
            }

            // 2. Iniciar flujo OAuth
            var state = Guid.NewGuid().ToString();
            var authUrl = $"{_config.BaseUrl}/oauth/authorize?client_id={_config.ClientId}&redirect_uri={_config.RedirectUri}&response_type=code&state={state}&scope={_config.Scope}";

            // 3. Abrir navegador
            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });

            // 4. Escuchar callback
            var code = await ListenForCallbackAsync(state);
            if (string.IsNullOrEmpty(code))
                return Result<GitCredential>.Fail("Autenticación cancelada");

            // 5. Intercambiar código por token
            var tokenResponse = await ExchangeCodeForTokenAsync(code);
            if (tokenResponse == null)
                return Result<GitCredential>.Fail("Error al obtener token de acceso");

            // 6. Obtener información del usuario
            var userResult = await GetUserInfoAsync(tokenResponse.AccessToken);
            if (!userResult.IsSuccess)
                return userResult;

            // 7. Guardar credenciales
            await _credentialStorage.SaveCredentialAsync("GitLab", userResult.Data.Username, tokenResponse.AccessToken);

            return userResult;
        }
        catch (Exception ex)
        {
            return Result<GitCredential>.Fail($"Error en autenticación: {ex.Message}");
        }
    }

    public async Task<bool> ValidateTokenAsync(string token)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_config.BaseUrl}/api/v4/user");
            request.Headers.Add("Authorization", $"Bearer {token}");

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<Result<GitCredential>> GetUserInfoAsync(string token)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_config.BaseUrl}/api/v4/user");
            request.Headers.Add("Authorization", $"Bearer {token}");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return Result<GitCredential>.Fail($"Error obteniendo usuario: {response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync();
            var user = JsonSerializer.Deserialize<GitLabUserDto>(json);

            if (user == null)
                return Result<GitCredential>.Fail("No se pudo deserializar la info del usuario");

            return Result<GitCredential>.Success(new GitCredential
            {
                Provider = GitProvider.GitLab,
                Username = user.Username,
                Email = user.Email ?? string.Empty,
                AvatarUrl = user.AvatarUrl,
                AccessToken = token
            });
        }
        catch (Exception ex)
        {
            return Result<GitCredential>.Fail($"Error obteniendo info: {ex.Message}");
        }
    }

    private async Task<string?> ListenForCallbackAsync(string expectedState)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(_config.RedirectUri + "/");
        listener.Start();

        var context = await listener.GetContextAsync();
        var query = context.Request.QueryString;

        var code = query["code"];
        var state = query["state"];

        // Responder al navegador
        var response = context.Response;
        string responseString = state == expectedState
            ? "<html><body style='font-family:Arial;text-align:center;padding:50px'><h1 style='color:#fc6d26'>✅ Autenticación GitLab exitosa</h1><p>Puedes cerrar esta ventana</p></body></html>"
            : "<html><body style='font-family:Arial;text-align:center;padding:50px'><h1 style='color:#dc3545'>❌ Error de autenticación</h1></body></html>";

        var buffer = Encoding.UTF8.GetBytes(responseString);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        response.OutputStream.Close();
        listener.Stop();

        return state == expectedState ? code : null;
    }

    private async Task<TokenResponse?> ExchangeCodeForTokenAsync(string code)
    {
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _config.ClientId,
                ["client_secret"] = _config.ClientSecret,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = _config.RedirectUri
            });

            var response = await _httpClient.PostAsync($"{_config.BaseUrl}/oauth/token", content);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<TokenResponse>(json);
        }
        catch
        {
            return null;
        }
    }

    private class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;
    }

    private class GitLabUserDto
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("avatar_url")]
        public string AvatarUrl { get; set; } = string.Empty;
    }
}
