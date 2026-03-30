using Chapi.Application.Interfaces;
using Chapi.Domain.Documentation;

namespace Chapi.Application.UseCases.Documentation;

/// <summary>
/// Encapsula la exportación de un DocumentSession a distintos formatos.
/// Mantiene la lógica de selección de ruta fuera del ViewModel.
/// </summary>
public class ExportDocumentUseCase
{
    private readonly IDocumentExportService _exportService;

    public ExportDocumentUseCase(IDocumentExportService exportService)
    {
        _exportService = exportService;
    }

    public Task<bool> ExportToWordAsync(DocumentSession session, string outputPath, CancellationToken cancellationToken = default) =>
        _exportService.ExportToWordAsync(session, outputPath);

    public Task<bool> ExportToMarkdownAsync(DocumentSession session, string outputPath, CancellationToken cancellationToken = default) =>
        _exportService.ExportToMarkdownAsync(session, outputPath);
}
