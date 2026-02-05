using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Domain.Models;
using System.Threading.Tasks;

namespace Chapi.Domain.Interfaces;

/// <summary>
/// Interfaz para el servicio de autenticación con GitHub.
/// </summary>
public interface IGitHubAuthService
{
    /// <summary>
    /// Inicia el flujo de autenticación por dispositivo.
    /// </summary>
    Task<Result<GitHubDeviceCode>> RequestDeviceCodeAsync();

    /// <summary>
    /// Sondea a GitHub para obtener el token de acceso.
    /// </summary>
    Task<Result<string>> PollForTokenAsync(string deviceCode, int intervalSeconds);

    /// <summary>
    /// Obtiene la información del usuario autenticado.
    /// </summary>
    Task<Result<GitHubUser>> GetUserInfoAsync(string accessToken);
}
