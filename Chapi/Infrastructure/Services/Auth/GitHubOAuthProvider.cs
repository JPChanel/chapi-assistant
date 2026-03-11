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
/// Proveedor de autenticación OAuth para GitHub.
/// </summary>
public class GitHubOAuthProvider : IGitAuthProvider
{
    private static readonly SemaphoreSlim AuthenticationSemaphore = new(1, 1);
    private readonly ICredentialStorageService _credentialStorage;
    private readonly HttpClient _httpClient;
    private readonly GitHubConfig _config;

    public GitProvider Provider => GitProvider.GitHub;

    public GitHubOAuthProvider(
        ICredentialStorageService credentialStorage,
        HttpClient httpClient,
        IOptions<GitAuthConfig> config)
    {
        _credentialStorage = credentialStorage;

        // Configuramos el HttpClient para ignorar el proxy del sistema si da problemas
        var handler = new HttpClientHandler { UseProxy = false };
        _httpClient = httpClient;

        _config = config.Value.GitHub;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ChapiAssistant");
    }

    public async Task<Result<GitCredential>> AuthenticateAsync()
    {
        await AuthenticationSemaphore.WaitAsync();
        try
        {
            // 1. Verificar credenciales existentes
            var existing = await _credentialStorage.GetCredentialAsync("GitHub");
            if (existing.HasValue && await ValidateTokenAsync(existing.Value.token))
            {
                return await GetUserInfoAsync(existing.Value.token);
            }

            // 2. Iniciar flujo OAuth
            var state = Guid.NewGuid().ToString();
            var authUrl = $"https://github.com/login/oauth/authorize?client_id={_config.ClientId}&redirect_uri={_config.RedirectUri}&scope={_config.Scope}&state={state}";

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
            var tokenResult = await ExchangeCodeForTokenResultAsync(code);
            if (!tokenResult.IsSuccess)
                return Result<GitCredential>.Fail(tokenResult.Error);

            var tokenResponse = tokenResult.Data;

            // 6. Obtener información del usuario
            var userResult = await GetUserInfoAsync(tokenResponse.AccessToken);
            if (!userResult.IsSuccess)
                return userResult;

            // 7. Guardar credenciales
            await _credentialStorage.SaveCredentialAsync("GitHub", userResult.Data.Username, tokenResponse.AccessToken);

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

    private async Task<Result<TokenResponse>> ExchangeCodeForTokenResultAsync(string code)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
            request.Headers.Add("Accept", "application/json");

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _config.ClientId,
                ["client_secret"] = _config.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = _config.RedirectUri
            });

            request.Content = content;

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return Result<TokenResponse>.Fail($"GitHub devolvió error HTTP {response.StatusCode}: {json}");
            }

            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json);

            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error_description", out var desc))
                    return Result<TokenResponse>.Fail($"GitHub Error: {desc.GetString()}");
                if (doc.RootElement.TryGetProperty("error", out var err))
                    return Result<TokenResponse>.Fail($"GitHub Error: {err.GetString()}");

                return Result<TokenResponse>.Fail($"Error al procesar respuesta de GitHub: {json}");
            }

            return Result<TokenResponse>.Success(tokenResponse);
        }
        catch (Exception ex)
        {
            return Result<TokenResponse>.Fail($"Error de conexión con GitHub: {ex.Message}");
        }
    }

    public async Task<bool> ValidateTokenAsync(string token)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            request.Headers.Add("Authorization", $"Bearer {token}");

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public Task<Result<GitCredential>> RefreshTokenAsync()
    {
        return Task.FromResult(Result<GitCredential>.Fail("GitHub no soporta refresh token en este flujo."));
    }

    public async Task<Result<List<RemoteRepository>>> GetRepositoriesAsync(string token)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/repos?sort=updated&per_page=100");
            request.Headers.Add("Authorization", $"Bearer {token}");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return Result<List<RemoteRepository>>.Fail($"Error obteniendo repositorios: {response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync();
            var repos = JsonSerializer.Deserialize<List<GitHubRepoDto>>(json);

            if (repos == null)
                return Result<List<RemoteRepository>>.Fail("No se pudo deserializar la lista de repositorios");

            var result = repos.Select(r => new RemoteRepository
            {
                Name = r.Name,
                FullName = r.FullName,
                CloneUrl = r.CloneUrl,
                IsPrivate = r.Private,
                Description = r.Description,
                UpdatedAt = r.UpdatedAt
            }).ToList();

            return Result<List<RemoteRepository>>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<List<RemoteRepository>>.Fail($"Error obteniendo repositorios: {ex.Message}");
        }
    }

    private class GitHubRepoDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("full_name")]
        public string FullName { get; set; } = string.Empty;

        [JsonPropertyName("clone_url")]
        public string CloneUrl { get; set; } = string.Empty;

        [JsonPropertyName("private")]
        public bool Private { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }

    public async Task<Result<GitCredential>> GetUserInfoAsync(string token)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            request.Headers.Add("Authorization", $"Bearer {token}");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return Result<GitCredential>.Fail($"Error obteniendo usuario: {response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync();
            var user = JsonSerializer.Deserialize<GitHubUserDto>(json);

            if (user == null)
                return Result<GitCredential>.Fail("No se pudo deserializar la info del usuario");

            return Result<GitCredential>.Success(new GitCredential
            {
                Provider = GitProvider.GitHub,
                Username = user.Login,
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
            string statusColor = isSuccess ? "#28a745" : "#dc3545";
            string statusMessage = isSuccess
                ? "GitHub se ha vinculado correctamente con Chapi."
                : (state != expectedState ? "Error de seguridad: el estado de la sesión no coincide." : "No se pudo verificar la identidad o el usuario canceló el acceso.");
            string brandColor = isSuccess ? "linear-gradient(135deg, #2abb47 0%, #28a745 100%)" : "linear-gradient(135deg, #ff4b2b 0%, #ff416c 100%)";

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



    private class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;

        [JsonPropertyName("scope")]
        public string Scope { get; set; } = string.Empty;
    }

    private class GitHubUserDto
    {
        [JsonPropertyName("login")]
        public string Login { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("avatar_url")]
        public string AvatarUrl { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }
}
