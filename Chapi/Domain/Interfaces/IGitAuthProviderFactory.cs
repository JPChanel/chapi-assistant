using Chapi.Domain.Enums;

namespace Chapi.Domain.Interfaces;

/// <summary>
/// Factory para obtener el proveedor de autenticación correcto.
/// </summary>
public interface IGitAuthProviderFactory
{
    /// <summary>
    /// Obtiene el proveedor específico.
    /// </summary>
    IGitAuthProvider GetProvider(GitProvider provider);
    
    /// <summary>
    /// Detecta el proveedor desde una URL de repositorio.
    /// </summary>
    GitProvider DetectProviderFromUrl(string remoteUrl);
}
