using Chapi.Domain.Documentation;

namespace Chapi.Application.Interfaces;

public interface IDocumentExportService
{
    Task<bool> ExportToWordAsync(DocumentSession session, string outputPath);
    Task<bool> ExportToMarkdownAsync(DocumentSession session, string outputPath);
}
