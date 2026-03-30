using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Domain.Enums;
using Chapi.Domain.Interfaces;
using Chapi.Domain.Models;
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
    private static readonly SemaphoreSlim AuthenticationSemaphore = new(1, 1);
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
        await AuthenticationSemaphore.WaitAsync();
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

            // 5. Intercambiar código por token de OAuth
            var tokenResponse = await ExchangeCodeForTokenAsync(code);
            if (tokenResponse == null)
                return Result<GitCredential>.Fail("Error al obtener token de acceso");

            // 6. Obtener información del usuario
            var userResult = await GetUserInfoAsync(tokenResponse.AccessToken);
            if (!userResult.IsSuccess)
                return userResult;

            // 7. Guardar el token de OAuth
            await _credentialStorage.SaveCredentialAsync("GitLab", userResult.Data.Username, tokenResponse.AccessToken);

            // Guardar Refresh Token para renovación automática
            if (!string.IsNullOrEmpty(tokenResponse.RefreshToken))
            {
                await _credentialStorage.SaveCredentialAsync("GitLab_Refresh", "RefreshToken", tokenResponse.RefreshToken);
            }

            return userResult;
        }
        catch (Exception ex)
        {
            return Result<GitCredential>.Fail($"Error en autenticación: {ex.Message}");
        }
        finally
        {
            AuthenticationSemaphore.Release();
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

    public async Task<Result<GitCredential>> RefreshTokenAsync()
    {
        try
        {
            // 1. Recuperar Refresh Token guardado
            var refreshCred = await _credentialStorage.GetCredentialAsync("GitLab_Refresh");
            if (!refreshCred.HasValue || string.IsNullOrEmpty(refreshCred.Value.token))
                return Result<GitCredential>.Fail("No existe refresh token");

            // 2. Solicitar renovación
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _config.ClientId,
                ["client_secret"] = _config.ClientSecret,
                ["refresh_token"] = refreshCred.Value.token,
                ["grant_type"] = "refresh_token",
                ["redirect_uri"] = _config.RedirectUri
            });

            var response = await _httpClient.PostAsync($"{_config.BaseUrl}/oauth/token", content);
            if (!response.IsSuccessStatusCode)
            {
                // Si falla el refresh, borrarlo para forzar login
                await _credentialStorage.DeleteCredentialAsync("GitLab_Refresh");
                return Result<GitCredential>.Fail("El token de refresco expiró o es inválido.");
            }

            var json = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json);

            if (tokenResponse == null)
                return Result<GitCredential>.Fail("Error al procesar respuesta de renovación.");

            // 3. Obtener info de usuario para actualizar credenciales
            var userResult = await GetUserInfoAsync(tokenResponse.AccessToken);
            if (!userResult.IsSuccess) return userResult;

            // 4. Actualizar credenciales
            // Guardar Access Token (usando el nombre de usuario real obtenido)
            await _credentialStorage.SaveCredentialAsync("GitLab", userResult.Data.Username, tokenResponse.AccessToken);

            // Guardar Nuevo Refresh Token (rotación de tokens)
            if (!string.IsNullOrEmpty(tokenResponse.RefreshToken))
            {
                await _credentialStorage.SaveCredentialAsync("GitLab_Refresh", "RefreshToken", tokenResponse.RefreshToken);
            }

            return userResult;
        }
        catch (Exception ex)
        {
            return Result<GitCredential>.Fail($"Error al renovar token: {ex.Message}");
        }
    }

    public async Task<Result<List<RemoteRepository>>> GetRepositoriesAsync(string token)
    {
        try
        {
            // GitLab llama a los repositorios "projects"
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_config.BaseUrl}/api/v4/projects?membership=true&order_by=updated_at&per_page=100");
            request.Headers.Add("Authorization", $"Bearer {token}");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return Result<List<RemoteRepository>>.Fail($"Error obteniendo proyectos: {response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync();
            var projects = JsonSerializer.Deserialize<List<GitLabProjectDto>>(json);

            if (projects == null)
                return Result<List<RemoteRepository>>.Fail("No se pudo deserializar la lista de proyectos");

            var result = projects.Select(p => new RemoteRepository
            {
                Name = p.Name,
                FullName = p.PathWithNamespace,
                CloneUrl = p.HttpUrlToRepo,
                IsPrivate = p.Visibility == "private",
                Description = p.Description,
                UpdatedAt = p.UpdatedAt
            }).ToList();

            return Result<List<RemoteRepository>>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<List<RemoteRepository>>.Fail($"Error obteniendo proyectos: {ex.Message}");
        }
    }

    private class GitLabProjectDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("path_with_namespace")]
        public string PathWithNamespace { get; set; } = string.Empty;

        [JsonPropertyName("http_url_to_repo")]
        public string HttpUrlToRepo { get; set; } = string.Empty;

        [JsonPropertyName("visibility")]
        public string Visibility { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("last_activity_at")]
        public DateTime? UpdatedAt { get; set; }
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

        while (true)
        {
            var context = await listener.GetContextAsync();
            var query = context.Request.QueryString;

            // Ignorar peticiones de favicon o similares
            if (context.Request.Url?.AbsolutePath.EndsWith("favicon.ico") == true)
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.Close();
                continue;
            }

            var code = query["code"];
            var state = query["state"];

            // Responder al navegador con UI Premium
            var response = context.Response;
            response.ContentType = "text/html; charset=utf-8";

            bool isSuccess = !string.IsNullOrEmpty(code) && state == expectedState;
            string statusTitle = isSuccess ? "¡Conexión Exitosa!" : "Error de Conexión";
            string statusIcon = isSuccess ? "✅" : "❌";
            string statusColor = isSuccess ? "#fc6d26" : "#dc3545";
            string statusMessage = isSuccess
                ? "GitLab se ha vinculado correctamente con Chapi."
                : (state != expectedState ? "Error de seguridad: el estado de la sesión no coincide." : "No se pudo verificar la identidad o el usuario canceló el acceso.");
            string brandColor = isSuccess ? "linear-gradient(135deg, #fc6d26 0%, #e24329 100%)" : "linear-gradient(135deg, #ff4b2b 0%, #ff416c 100%)";

            string responseString = $@"
<!DOCTYPE html>
<html lang='es'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>{statusTitle}</title>
    <style>
        body {{
            background-color: #0f0f12;
            color: white;
            font-family: 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
            overflow: hidden;
        }}
        .container {{
            background: rgba(255, 255, 255, 0.03);
            backdrop-filter: blur(10px);
            border: 1px solid rgba(255, 255, 255, 0.1);
            padding: 40px;
            border-radius: 24px;
            text-align: center;
            box-shadow: 0 20px 50px rgba(0,0,0,0.5);
            max-width: 400px;
            animation: fadeIn 0.8s ease-out;
        }}
        @keyframes fadeIn {{
            from {{ opacity: 0; transform: translateY(20px); }}
            to {{ opacity: 1; transform: translateY(0); }}
        }}
        .icon {{
            font-size: 64px;
            margin-bottom: 20px;
            display: inline-block;
            filter: drop-shadow(0 0 10px {statusColor}44);
        }}
        h1 {{
            margin: 0;
            font-size: 28px;
            font-weight: 700;
            background: {brandColor};
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
        }}
        p {{
            color: rgba(255,255,255,0.7);
            margin: 15px 0 30px;
            line-height: 1.5;
        }}
        .badge {{
            background: rgba(255,255,255,0.05);
            padding: 8px 16px;
            border-radius: 100px;
            font-size: 13px;
            color: {statusColor};
            display: inline-block;
            border: 1px solid {statusColor}33;
        }}
        .footer {{
            margin-top: 30px;
            font-size: 12px;
            color: rgba(255,255,255,0.3);
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='icon'>{statusIcon}</div>
        <h1>{statusTitle}</h1>
        <p>{statusMessage}</p>
        <div class='badge'>{(isSuccess ? "Ya puedes cerrar esta pestaña y volver a la app" : "Puedes cerrar esta pestaña e intentar de nuevo")}</div>
        <div class='footer'>Chapi Assistant &bull; Secure Auth</div>
    </div>
</body>
</html>";

            var buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();

            // Salir solo si tenemos el código o si el estado es inválido (error real)
            if (isSuccess || !string.IsNullOrEmpty(state))
            {
                listener.Stop();
                return isSuccess ? code : null;
            }
        }
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
