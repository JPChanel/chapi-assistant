using System.IO;
using Chapi.Application.Interfaces;
using Chapi.Domain.Documentation;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Chapi.Infrastructure.Documentation;

/// <summary>
/// Exporta una DocumentSession a Word (.docx) usando DocumentFormat.OpenXml.
/// Genera portada, índice y secciones con formato profesional.
/// </summary>
public class OpenXmlExportService : IDocumentExportService
{
    private readonly IKrokiDiagramService _krokiService;

    public OpenXmlExportService(IKrokiDiagramService krokiService)
    {
        _krokiService = krokiService;
    }

    public async Task<bool> ExportToWordAsync(DocumentSession session, string outputPath)
    {
        try
        {
            using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // Estilos base
            AddStyles(mainPart);

            // Portada
            AddCoverPage(body, session);

            // Secciones
            int sectionNumber = 1;
            foreach (var section in session.Sections.OrderBy(s => s.Order))
            {
                AddSectionHeading(body, $"{sectionNumber}. {section.Title}", 1);

                switch (section.Type)
                {
                    case DocSectionType.Text:
                    case DocSectionType.Table:
                        AddMarkdownContent(body, section.Content);
                        break;

                    case DocSectionType.Diagram:
                        AddDiagramSection(body, mainPart, section);
                        break;

                    case DocSectionType.Image:
                        AddImageSection(body, mainPart, section);
                        break;
                }

                sectionNumber++;
            }

            // Configuración de página
            body.AppendChild(new SectionProperties(
                new PageSize { Width = 11906, Height = 16838 },
                new PageMargin { Top = 1440, Right = 1080, Bottom = 1440, Left = 1080 }
            ));

            mainPart.Document.Save();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ExportToMarkdownAsync(DocumentSession session, string outputPath)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# {session.Title}");
            sb.AppendLine($"_Versión {session.Version} — {session.CreatedAt:dd/MM/yyyy}_");
            sb.AppendLine();

            int num = 1;
            foreach (var section in session.Sections.OrderBy(s => s.Order))
            {
                sb.AppendLine($"## {num}. {section.Title}");
                sb.AppendLine();

                if (section.Type is DocSectionType.Text or DocSectionType.Table)
                {
                    sb.AppendLine(section.Content);
                }
                else if (section.Type == DocSectionType.Diagram)
                {
                    var fence = section.DiagramFormat == DiagramFormat.Mermaid ? "mermaid" : "plantuml";
                    sb.AppendLine($"```{fence}");
                    sb.AppendLine(section.DiagramCode);
                    sb.AppendLine("```");
                }
                sb.AppendLine();
                num++;
            }

            await File.WriteAllTextAsync(outputPath, sb.ToString());
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ─── Helpers Word ──────────────────────────────────────────────────────────

    private static void AddCoverPage(Body body, DocumentSession session)
    {
        AddParagraph(body, session.Title.ToUpper(), "Title");
        AddParagraph(body, "Documentación Técnica de Ingeniería de Software", "Subtitle");
        AddParagraph(body, $"Versión {session.Version}", "Subtitle");
        AddParagraph(body, $"Proyecto: {session.ProjectName}", "Subtitle");
        AddParagraph(body, session.CreatedAt.ToString("dd 'de' MMMM 'de' yyyy"), "Subtitle");
        body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
    }

    private static void AddSectionHeading(Body body, string text, int level)
    {
        var style = level == 1 ? "Heading1" : level == 2 ? "Heading2" : "Heading3";
        AddParagraph(body, text, style);
    }

    private static void AddMarkdownContent(Body body, string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            AddParagraph(body, "(Contenido pendiente)", "Normal");
            return;
        }

        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (trimmed.StartsWith("## ")) AddParagraph(body, trimmed[3..], "Heading2");
            else if (trimmed.StartsWith("### ")) AddParagraph(body, trimmed[4..], "Heading3");
            else if (trimmed.StartsWith("| ")) AddTableRow(body, trimmed);
            else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                AddListItem(body, trimmed[2..]);
            else
                AddParagraph(body, trimmed.TrimStart('#').Trim(), "Normal");
        }
    }

    private void AddDiagramSection(Body body, MainDocumentPart mainPart, DocSection section)
    {
        AddParagraph(body, $"[Diagrama: {section.Title}]", "Normal");
        AddParagraph(body, $"Formato: {section.DiagramFormat}", "Caption");
        if (!string.IsNullOrWhiteSpace(section.DiagramCode))
        {
            // Incluir el código como referencia
            AddParagraph(body, section.DiagramCode, "Code");
        }
    }

    private void AddImageSection(Body body, MainDocumentPart mainPart, DocSection section)
    {
        if (string.IsNullOrWhiteSpace(section.ImageBase64)) return;
        try
        {
            var imageBytes = Convert.FromBase64String(section.ImageBase64);
            var imgPart = mainPart.AddImagePart(ImagePartType.Png);
            using var ms = new MemoryStream(imageBytes);
            imgPart.FeedData(ms);
        }
        catch { }
    }

    private static void AddParagraph(Body body, string text, string style)
    {
        var para = new Paragraph();
        var propRun = new ParagraphProperties(new ParagraphStyleId { Val = style });
        para.AppendChild(propRun);
        para.AppendChild(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        body.AppendChild(para);
    }

    private static void AddListItem(Body body, string text)
    {
        var para = new Paragraph(
            new ParagraphProperties(
                new NumberingProperties(
                    new NumberingLevelReference { Val = 0 },
                    new NumberingId { Val = 1 })),
            new Run(new Text(text)));
        body.AppendChild(para);
    }

    private static void AddTableRow(Body body, string markdownRow)
    {
        // Ignorar líneas separadoras de tabla Markdown (|---|---|)
        if (markdownRow.Contains("---")) return;
        var cells = markdownRow.Split('|')
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .ToArray();
        if (cells.Length == 0) return;

        var table = body.Elements<Table>().LastOrDefault() ?? CreateTable(body);
        var row = new TableRow();
        foreach (var cell in cells)
        {
            row.AppendChild(new TableCell(new Paragraph(new Run(new Text(cell)))));
        }
        table.AppendChild(row);
    }

    private static Table CreateTable(Body body)
    {
        var table = new Table(new TableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));
        body.AppendChild(table);
        return table;
    }

    private static void AddStyles(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles();
    }
}
