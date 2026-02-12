namespace Chapi.Domain.Interfaces;

/// <summary>
/// Servicio para almacenar credenciales de forma segura en Windows Credential Manager.
/// </summary>
public interface ICredentialStorageService
{
    /// <summary>
    /// Guarda credenciales en Windows Credential Manager.
    /// </summary>
    Task SaveCredentialAsync(string service, string username, string token);

    /// <summary>
    /// Obtiene credenciales guardadas.
    /// </summary>
    Task<(string username, string token)?> GetCredentialAsync(string service);

    /// <summary>
    /// Elimina credenciales guardadas.
    /// </summary>
    Task DeleteCredentialAsync(string service);

    /// <summary>
    /// Verifica si existen credenciales para un servicio.
    /// </summary>
    Task<bool> HasCredentialAsync(string service);
}
