namespace Chapi.Domain.Entities;

/// <summary>
/// Representa un conflicto de fusión en un archivo de Git.
/// </summary>
public class GitConflict
{
    /// <summary>
    /// Ruta relativa del archivo en el repositorio.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Lista de bloques de conflicto encontrados en el archivo.
    /// </summary>
    public List<ConflictBlock> Blocks { get; set; } = new();

    /// <summary>
    /// Indica si todos los bloques de conflicto han sido resueltos.
    /// </summary>
    public bool IsResolved => Blocks.All(b => b.IsResolved);
}
