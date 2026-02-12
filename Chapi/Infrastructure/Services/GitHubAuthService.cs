using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Domain.Interfaces;
using Chapi.Domain.Models;
using Chapi.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chapi.Infrastructure.Services;

public class GitHubAuthService : IGitHubAuthService
{
    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private const string OAuthUrl = "https://github.com/login/device/code";
    private const string TokenUrl = "https://github.com/login/oauth/access_token";
    private const string UserApiUrl = "https://api.github.com/user";

    public GitHubAuthService(IOptions<GitAuthConfig> config)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ChapiAssistant");
        _clientId = config.Value.GitHub.ClientId;
    }

    public async Task<Result<GitHubDeviceCode>> RequestDeviceCodeAsync()
    {
        try
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", _clientId),
                new KeyValuePair<string, string>("scope", "repo user workflow")
            });

            var response = await _httpClient.PostAsync(OAuthUrl, content);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return Result<GitHubDeviceCode>.Fail($"Error de GitHub: {json}");

            var data = JsonSerializer.Deserialize<DeviceCodeResponse>(json);
            if (data == null) return Result<GitHubDeviceCode>.Fail("No se pudo deserializar la respuesta de GitHub.");

            return Result<GitHubDeviceCode>.Success(new GitHubDeviceCode(
                data.DeviceCode,
                data.UserCode,
                data.VerificationUri,
                data.ExpiresIn,
                data.Interval));
        }
        catch (Exception ex)
        {
            return Result<GitHubDeviceCode>.Fail($"Error de red: {ex.Message}");
        }
    }

    public async Task<Result<string>> PollForTokenAsync(string deviceCode, int intervalSeconds)
    {
        try
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", _clientId),
                new KeyValuePair<string, string>("device_code", deviceCode),
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code")
            });

            var response = await _httpClient.PostAsync(TokenUrl, content);
            var json = await response.Content.ReadAsStringAsync();

            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json);

            if (tokenResponse?.AccessToken != null)
                return Result<string>.Success(tokenResponse.AccessToken);

            if (tokenResponse?.Error != null)
            {
                // "authorization_pending" es normal mientras el usuario no haya ingresado el código
                if (tokenResponse.Error == "authorization_pending")
                    return Result<string>.Fail("pending");

                return Result<string>.Fail(tokenResponse.ErrorDescription ?? tokenResponse.Error);
            }

            return Result<string>.Fail("Error desconocido al obtener el token.");
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Error de red: {ex.Message}");
        }
    }

    public async Task<Result<GitHubUser>> GetUserInfoAsync(string accessToken)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, UserApiUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return Result<GitHubUser>.Fail($"Error obteniendo usuario: {json}");

            var githubUserDto = JsonSerializer.Deserialize<GitHubUserDto>(json);
            if (githubUserDto == null) return Result<GitHubUser>.Fail("No se pudo deserializar la info del usuario.");

            return Result<GitHubUser>.Success(new GitHubUser
            {
                Login = githubUserDto.Login,
                Name = githubUserDto.Name ?? githubUserDto.Login,
                AvatarUrl = githubUserDto.AvatarUrl,
                Email = githubUserDto.Email,
                AccessToken = accessToken
            });
        }
        catch (Exception ex)
        {
            return Result<GitHubUser>.Fail($"Error de red: {ex.Message}");
        }
    }

    private class DeviceCodeResponse
    {
        [JsonPropertyName("device_code")] public string DeviceCode { get; set; } = string.Empty;
        [JsonPropertyName("user_code")] public string UserCode { get; set; } = string.Empty;
        [JsonPropertyName("verification_uri")] public string VerificationUri { get; set; } = string.Empty;
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("interval")] public int Interval { get; set; }
    }

    private class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("error_description")] public string? ErrorDescription { get; set; }
    }

    private class GitHubUserDto
    {
        [JsonPropertyName("login")] public string Login { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("avatar_url")] public string AvatarUrl { get; set; } = string.Empty;
        [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
    }
}
