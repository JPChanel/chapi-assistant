namespace Chapi.Application.Interfaces;

public interface IKrokiDiagramService
{
    /// <summary>
    /// Genera un SVG a partir de código de diagrama usando Kroki.io.
    /// </summary>
    /// <param name="code">Código Mermaid o PlantUML</param>
    /// <param name="format">Tipo de formato ("mermaid" o "plantuml")</param>
    /// <returns>SVG como string, o mensaje de error</returns>
    Task<string> RenderToSvgAsync(string code, string format, CancellationToken cancellationToken = default);

    /// <summary>
    /// Genera un PNG como bytes a partir de código de diagrama.
    /// </summary>
    Task<byte[]?> RenderToPngAsync(string code, string format, CancellationToken cancellationToken = default);
}
