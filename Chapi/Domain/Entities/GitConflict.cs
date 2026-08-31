using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Chapi.Domain.Entities;

/// <summary>
/// Representa un conflicto de fusión en un archivo de Git.
/// </summary>
public class GitConflict : INotifyPropertyChanged
{
    private bool _isSaved;

    /// <summary>
    /// Ruta relativa del archivo en el repositorio.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Ruta absoluta del archivo cuando puede resolverse desde el repo local.
    /// </summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>
    /// Lista de bloques de conflicto encontrados en el archivo.
    /// </summary>
    public List<ConflictBlock> Blocks { get; set; } = new();

    /// <summary>
    /// Indica si el archivo conserva marcadores inline de Git.
    /// </summary>
    public bool HasInlineMarkers { get; set; } = true;

    /// <summary>
    /// Indica si el archivo fue editado externamente sin marcadores de conflicto restantes en disco.
    /// </summary>
    public bool IsExternallyResolved { get; set; }

    /// <summary>
    /// Indica si todos los bloques de conflicto han sido resueltos.
    /// </summary>
    public bool IsResolved => Blocks.All(b => b.IsResolved);

    /// <summary>
    /// Indica si el archivo ya fue guardado y agregado al indice tras resolverlo.
    /// </summary>
    public bool IsSaved
    {
        get => _isSaved;
        set
        {
            if (_isSaved == value)
            {
                return;
            }

            _isSaved = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StatusText
    {
        get
        {
            if (IsSaved) return "Guardado en Git";
            if (IsExternallyResolved) return "Resuelto en disco (Sin marcadores)";
            if (IsResolved) return "Resuelto";
            return "Pendiente";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
