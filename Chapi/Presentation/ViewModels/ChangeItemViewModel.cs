using MaterialDesignThemes.Wpf;
using System.Windows.Media;

namespace Chapi.Presentation.ViewModels;

/// <summary>
/// ViewModel para un item de cambio en la lista de cambios.
/// Representa un archivo modificado con su estado, iconos y estadisticas.
/// </summary>
public class ChangeItemViewModel : ViewModelBase
{
    private static readonly char[] PathSeparators = ['/', '\\'];
    private bool _isSelected;
    private string _filePath = string.Empty;
    private string _status = string.Empty;
    private string _shortStatus = string.Empty;
    private PackIconKind _icon;
    private Brush _color = Brushes.Gray;
    private int _additions;
    private int _deletions;

    /// <summary>
    /// Indica si el item esta seleccionado para commit.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// Ruta completa del archivo.
    /// </summary>
    public string FilePath
    {
        get => _filePath;
        set
        {
            if (SetProperty(ref _filePath, value))
            {
                OnPropertyChanged(nameof(FileName));
                OnPropertyChanged(nameof(DirectoryPath));
            }
        }
    }

    public string FileName => GetFileName(_filePath);
    public string DirectoryPath => GetDirectoryPath(_filePath);

    /// <summary>
    /// Estado del archivo (ej: "Modificado", "Anadido").
    /// </summary>
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    /// <summary>
    /// Estado corto (ej: "M", "A", "D").
    /// </summary>
    public string ShortStatus
    {
        get => _shortStatus;
        set => SetProperty(ref _shortStatus, value);
    }

    /// <summary>
    /// Icono Material Design para el estado.
    /// </summary>
    public PackIconKind Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    /// <summary>
    /// Color asociado al estado.
    /// </summary>
    public Brush Color
    {
        get => _color;
        set => SetProperty(ref _color, value);
    }

    /// <summary>
    /// Numero de lineas anadidas.
    /// </summary>
    public int Additions
    {
        get => _additions;
        set => SetProperty(ref _additions, value);
    }

    /// <summary>
    /// Numero de lineas eliminadas.
    /// </summary>
    public int Deletions
    {
        get => _deletions;
        set => SetProperty(ref _deletions, value);
    }

    private static string GetFileName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var normalized = path.TrimEnd(PathSeparators);
        if (normalized.Length == 0)
            return string.Empty;

        var separatorIndex = normalized.LastIndexOfAny(PathSeparators);
        return separatorIndex >= 0 ? normalized[(separatorIndex + 1)..] : normalized;
    }

    private static string GetDirectoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var normalized = path.TrimEnd(PathSeparators);
        if (normalized.Length == 0)
            return string.Empty;

        var separatorIndex = normalized.LastIndexOfAny(PathSeparators);
        return separatorIndex > 0 ? normalized[..separatorIndex] : string.Empty;
    }
}
