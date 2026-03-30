using Chapi.Domain.Documentation;

namespace Chapi.Application.UseCases.AI;

public class GenerateAllDocumentSectionsUseCase
{
    private readonly GenerateDocumentSectionUseCase _generateSection;

    public GenerateAllDocumentSectionsUseCase(GenerateDocumentSectionUseCase generateSection)
    {
        _generateSection = generateSection;
    }

    public async Task ExecuteAsync(
        IEnumerable<DocSection> sections, 
        string instruction, 
        string projectContext,
        Func<DocSection, int, int, Task> onSectionProgress)
    {
        var sectionList = sections.ToList();
        for (int i = 0; i < sectionList.Count; i++)
        {
            var section = sectionList[i];
            
            // Omitir generación si ya tiene contenido, o si es solo una Imagen (ya contemplado por Kroki)
            if (section.Type == DocSectionType.Image) continue;
            if (section.Type is DocSectionType.Text or DocSectionType.Table && !string.IsNullOrWhiteSpace(section.Content)) continue;
            if (section.Type == DocSectionType.Diagram && !string.IsNullOrWhiteSpace(section.DiagramCode)) continue;

            if (onSectionProgress != null)
                await onSectionProgress(section, i + 1, sectionList.Count);
            
            await _generateSection.ExecuteAsync(section, instruction, projectContext);
            
            // Refrescar UI después de insertar el contenido nuevo
            if (onSectionProgress != null)
                await onSectionProgress(section, i + 1, sectionList.Count);

            // Retardo anti-bloqueo para Rate Limits de Free Tier en Gemini API (aprox 15 RPM)
            await Task.Delay(4000);
        }
    }
}
