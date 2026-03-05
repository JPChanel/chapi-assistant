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
        Action<DocSection, int, int> onSectionProgress)
    {
        var sectionList = sections.ToList();
        for (int i = 0; i < sectionList.Count; i++)
        {
            var section = sectionList[i];
            onSectionProgress?.Invoke(section, i + 1, sectionList.Count);
            await _generateSection.ExecuteAsync(section, instruction, projectContext);
        }
    }
}
