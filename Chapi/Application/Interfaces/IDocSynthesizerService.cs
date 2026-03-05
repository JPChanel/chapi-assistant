using Chapi.Domain.Documentation;

namespace Chapi.Application.Interfaces;

public interface IDocSynthesizerService
{
    /// <summary>
    /// Genera el contenido Markdown para una sección textual del documento.
    /// </summary>
    Task<string> GenerateSectionContentAsync(string sectionTitle, string projectContext, CancellationToken cancellationToken = default);

    /// <summary>
    /// Genera código Mermaid o PlantUML para una sección de diagrama.
    /// </summary>
    Task<string> GenerateDiagramCodeAsync(string sectionTitle, DiagramFormat format, string projectContext, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analiza la estructura de un proyecto y devuelve un resumen de contexto para la IA.
    /// </summary>
    Task<string> AnalyzeProjectContextAsync(string projectPath, CancellationToken cancellationToken = default);
}
