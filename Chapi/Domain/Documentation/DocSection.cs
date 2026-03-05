using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;

namespace Chapi.Domain.Documentation;

public class DocSection : INotifyPropertyChanged
{
    private string _content = string.Empty;
    private string _diagramCode = string.Empty;
    private string _imageBase64 = string.Empty;
    private string _title = string.Empty;
    private DiagramFormat _diagramFormat = DiagramFormat.Mermaid;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int Order { get; set; }

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public DocSectionType Type { get; set; } = DocSectionType.Text;

    // Para secciones de tipo Text/Table — contenido en Markdown
    public string Content
    {
        get => _content;
        set { _content = value; OnPropertyChanged(); }
    }

    // Para secciones de tipo Diagram — código Mermaid o PlantUML
    public string DiagramCode
    {
        get => _diagramCode;
        set 
        { 
            _diagramCode = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(DiagramUrl));
        }
    }

    public DiagramFormat DiagramFormat
    {
        get => _diagramFormat;
        set 
        { 
            _diagramFormat = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(DiagramUrl));
        }
    }

    public string DiagramUrl
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DiagramCode)) return string.Empty;
            try
            {
                var format = DiagramFormat == DiagramFormat.Mermaid ? "mermaid" : "plantuml";
                var bytes = Encoding.UTF8.GetBytes(DiagramCode);
                using var output = new MemoryStream();
                using (var deflater = new ZLibStream(output, CompressionLevel.Optimal))
                {
                    deflater.Write(bytes, 0, bytes.Length);
                }
                var base64 = Convert.ToBase64String(output.ToArray())
                    .Replace('+', '-')
                    .Replace('/', '_')
                    .TrimEnd('=');
                return $"https://kroki.io/{format}/svg/{base64}";
            }
            catch { return string.Empty; }
        }
    }

    // Para secciones de tipo Image — imagen en base64
    public string ImageBase64
    {
        get => _imageBase64;
        set { _imageBase64 = value; OnPropertyChanged(); }
    }

    public string ImageMimeType { get; set; } = "image/png";

    // Helpers para XAML bindings dentro de DataTemplates
    public bool IsTextSection => Type is DocSectionType.Text or DocSectionType.Table;
    public bool IsDiagramSection => Type == DocSectionType.Diagram;
    public bool IsImageSection => Type == DocSectionType.Image;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
