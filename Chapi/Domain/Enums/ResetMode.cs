namespace Chapi.Domain.Enums;

/// <summary>
/// Modo de reset para el commit.
/// </summary>
public enum ResetMode
{
    /// <summary>
    /// Mantiene los cambios en el area de staging (--soft)
    /// </summary>
    Soft,

    /// <summary>
    /// Mantiene los cambios en el working directory (--mixed)
    /// </summary>
    Mixed,

    /// <summary>
    /// Descarta todos los cambios (--hard)
    /// </summary>
    Hard
}
