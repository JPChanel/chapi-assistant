using Chapi.Application.Interfaces;
using Chapi.Domain.Documentation;

namespace Chapi.Application.UseCases.AI;

/// <summary>
/// Orquesta la generación de contenido IA para una sección de documento técnico.
/// Delega a IDocSynthesizerService según el tipo de sección.
/// </summary>
public class GenerateDocumentSectionUseCase
{
    private readonly IDocSynthesizerService _synthesizer;

    public GenerateDocumentSectionUseCase(IDocSynthesizerService synthesizer)
    {
        _synthesizer = synthesizer;
    }

    /// <summary>
    /// Genera contenido para la sección indicada y lo aplica directamente sobre ella.
    /// </summary>
    /// <param name="section">Sección a rellenar.</param>
    /// <param name="instruction">Instrucción del usuario; si está vacía, se usa el título de la sección.</param>
    /// <param name="projectContext">Contexto del proyecto analizado.</param>
    public async Task ExecuteAsync(
        DocSection section,
        string instruction,
        string projectContext,
        CancellationToken cancellationToken = default)
    {
        var prompt = string.IsNullOrWhiteSpace(instruction) ? section.Title : instruction;

        if (section.Type == DocSectionType.Diagram)
        {
            section.DiagramCode = await _synthesizer.GenerateDiagramCodeAsync(
                prompt, section.DiagramFormat, projectContext, cancellationToken);
        }
        else
        {
            section.Content = await _synthesizer.GenerateSectionContentAsync(
                prompt, projectContext, cancellationToken);
        }
    }
}
