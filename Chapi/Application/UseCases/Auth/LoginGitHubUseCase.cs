using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Domain.Interfaces;
using Chapi.Domain.Models;
using Chapi.Infrastructure.Persistence.Settings;

namespace Chapi.Application.UseCases.Auth;

/// <summary>
/// Caso de uso para manejar el proceso de login con GitHub.
/// </summary>
public class LoginGitHubUseCase
{
    private readonly IGitHubAuthService _authService;

    public LoginGitHubUseCase(IGitHubAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Paso 1: Solicitar códigos de autorización.
    /// </summary>
    public async Task<Result<GitHubDeviceCode>> RequestCodesAsync()
    {
        return await _authService.RequestDeviceCodeAsync();
    }

    /// <summary>
    /// Paso 2: Sondear el token y guardar la información del usuario.
    /// </summary>
    public async Task<Result<GitHubUser>> CompleteLoginAsync(string deviceCode, int interval)
    {
        var tokenResult = await _authService.PollForTokenAsync(deviceCode, interval);

        if (!tokenResult.IsSuccess)
            return Result<GitHubUser>.Fail(tokenResult.Error);

        var userResult = await _authService.GetUserInfoAsync(tokenResult.Data!);

        if (userResult.IsSuccess && userResult.Data != null)
        {
            // Guardar en settings
            var settings = UserSettingsService.LoadSettings();
            settings.GitHubToken = userResult.Data.AccessToken;
            settings.GitHubUserLogin = userResult.Data.Login;
            settings.GitHubUserName = userResult.Data.Name;
            settings.GitHubUserAvatar = userResult.Data.AvatarUrl;
            UserSettingsService.SaveSettings(settings);
        }

        return userResult;
    }
}
