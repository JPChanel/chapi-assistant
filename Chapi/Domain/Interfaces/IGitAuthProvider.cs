using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Domain.Enums;
using Chapi.Domain.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Chapi.Domain.Interfaces;

/// <summary>
/// Interfaz para proveedores de autenticación Git (GitHub, GitLab, etc).
/// </summary>
public interface IGitAuthProvider
{
    GitProvider Provider { get; }
    
    /// <summary>
    /// Autentica al usuario con el proveedor.
    /// </summary>
    Task<Result<GitCredential>> AuthenticateAsync();
    
    /// <summary>
    /// Valida si un token sigue siendo válido.
    /// </summary>
    Task<bool> ValidateTokenAsync(string token);
    
    /// <summary>
    /// Obtiene información del usuario autenticado.
    /// </summary>
    Task<Result<GitCredential>> GetUserInfoAsync(string token);

    /// <summary>
    /// Obtiene la lista de repositorios del usuario.
    /// </summary>
    Task<Result<List<RemoteRepository>>> GetRepositoriesAsync(string token);
}
