namespace Chapi.Domain.Entities;

/// <summary>
/// Representa un bloque específico de conflicto dentro de un archivo de texto.
/// </summary>
public class ConflictBlock
{
    /// <summary>
    /// Número de línea donde inicia el bloque conflictivo (donde está <<<<<<<). 1-indexed.
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// Número de línea donde termina el bloque conflictivo (donde está >>>>>>>). 1-indexed.
    /// </summary>
    public int EndLine { get; set; }

    /// <summary>
    /// Contenido de los cambios locales (Current/HEAD).
    /// </summary>
    public string LocalContent { get; set; } = string.Empty;

    /// <summary>
    /// Contenido de los cambios entrantes (Incoming/Other).
    /// </summary>
    public string IncomingContent { get; set; } = string.Empty;

    /// <summary>
    /// El contenido que el usuario ha elegido finalmente para este bloque. 
    /// Puede ser local, entrante o una edición manual combinada.
    /// </summary>
    public string? ResolvedContent { get; set; }

    /// <summary>
    /// Si este bloque en específico ya fue resuelto por el usuario.
    /// </summary>
    public bool IsResolved => ResolvedContent != null;
}
