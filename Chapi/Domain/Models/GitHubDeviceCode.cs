namespace Chapi.Domain.Models;

/// <summary>
/// Información para que el usuario autorice la aplicación en GitHub.
/// </summary>
public record GitHubDeviceCode(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int ExpiresIn,
    int Interval);
