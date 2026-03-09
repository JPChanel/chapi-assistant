using System.IO;
using System.Text.RegularExpressions;
using Chapi.Application.Interfaces;
using Chapi.Domain.Documentation;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Chapi.Infrastructure.Documentation;

public class OpenXmlExportService : IDocumentExportService
{
    private readonly IKrokiDiagramService _krokiService;
    private uint _nextDrawingId = 1;

    public OpenXmlExportService(IKrokiDiagramService krokiService)
    {
        _krokiService = krokiService;
    }

    public async Task<bool> ExportToWordAsync(DocumentSession session, string outputPath)
    {
        try
        {
            var templatePath = GetTemplatePath(session.Template);
            if (!File.Exists(templatePath)) return false;

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDir))
                Directory.CreateDirectory(outputDir);

            File.Copy(templatePath, outputPath, true);

            using var doc = WordprocessingDocument.Open(outputPath, true);
            var mainPart = doc.MainDocumentPart;
            if (mainPart?.Document?.Body == null) return false;

            var tags = BuildTagMap(session);

            await HandleImagesInContainerAsync(mainPart, mainPart.Document.Body, tags);
            foreach (var headerPart in mainPart.HeaderParts)
            {
                if (headerPart.Header != null)
                    await HandleImagesInContainerAsync(mainPart, headerPart.Header, tags);
            }
            foreach (var footerPart in mainPart.FooterParts)
            {
                if (footerPart.Footer != null)
                    await HandleImagesInContainerAsync(mainPart, footerPart.Footer, tags);
            }

            ReplaceTagsInContainer(mainPart.Document.Body, tags);
            foreach (var headerPart in mainPart.HeaderParts)
            {
                if (headerPart.Header != null)
                    ReplaceTagsInContainer(headerPart.Header, tags);
            }
            foreach (var footerPart in mainPart.FooterParts)
            {
                if (footerPart.Footer != null)
                    ReplaceTagsInContainer(footerPart.Footer, tags);
            }

            mainPart.Document.Save();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetTemplatePath(DocTemplate template)
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var templateName = template == DocTemplate.ModeloSoftware
            ? "01.Modelo de Software.docx"
            : "02.Diseño del Sistema de Información.docx";

        var directPath = Path.Combine(appDir, "PlantillaWord", templateName);
        if (File.Exists(directPath)) return directPath;

        var presentationPath = Path.Combine(appDir, "Presentation", "PlantillaWord", templateName);
        return presentationPath;
    }

    private static Dictionary<string, string> BuildTagMap(DocumentSession session)
    {
        var tags = new Dictionary<string, string>(session.Metadata, StringComparer.OrdinalIgnoreCase);

        // Nunca dejar placeholders crudos en el documento final.
        foreach (var key in tags.Keys.ToList())
        {
            if (IsPendingPlaceholder(tags[key]))
                tags[key] = string.Empty;
        }

        SetIfMissing(tags, "PROYECTO_NOMBRE", session.ProjectName);
        SetIfMissing(tags, "PROYECTO_CODIGO", string.IsNullOrWhiteSpace(session.Id) ? string.Empty : session.Id[..Math.Min(8, session.Id.Length)]);
        SetIfMissing(tags, "DOC_VERSION", session.Version);
        SetIfMissing(tags, "DOC_MES_ANIO", DateTime.Now.ToString("MMMM, yyyy"));

        return tags;
    }

    private static void SetIfMissing(Dictionary<string, string> tags, string key, string value)
    {
        if (!tags.TryGetValue(key, out var existing) || string.IsNullOrWhiteSpace(existing) || IsPendingPlaceholder(existing))
            tags[key] = value;
    }

    private static bool IsPendingPlaceholder(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.StartsWith("[") && value.EndsWith("]");

    private static void ReplaceTagsInContainer(OpenXmlElement container, Dictionary<string, string> tags)
    {
        var texts = container.Descendants<Text>().ToList();
        foreach (var text in texts)
        {
            var current = text.Text;
            foreach (var tag in tags)
            {
                var fullTag = $"[{tag.Key}]";
                if (current.Contains(fullTag, StringComparison.Ordinal))
                    current = current.Replace(fullTag, tag.Value ?? string.Empty, StringComparison.Ordinal);
            }
            text.Text = current;
        }
    }

    private async Task HandleImagesInContainerAsync(MainDocumentPart mainPart, OpenXmlElement container, Dictionary<string, string> metadata)
    {
        var imageTexts = container.Descendants<Text>()
            .Where(t => t.Text.Contains("[IMG_", StringComparison.Ordinal) || t.Text.Contains("[DIAGRAMA_", StringComparison.Ordinal))
            .ToList();

        foreach (var text in imageTexts)
        {
            var matches = Regex.Matches(text.Text, @"\[(IMG_[^\]]+|DIAGRAMA_[^\]]+)\]");
            if (matches.Count == 0) continue;

            foreach (Match match in matches)
            {
                var tagName = match.Groups[1].Value;
                if (!metadata.TryGetValue(tagName, out var diagramCode))
                    continue;

                if (string.IsNullOrWhiteSpace(diagramCode) || IsPendingPlaceholder(diagramCode))
                    continue;

                try
                {
                    var format = DetectDiagramFormat(diagramCode);
                    var pngBytes = await _krokiService.RenderToPngAsync(diagramCode, format);
                    if (pngBytes == null) continue;

                    var imagePart = mainPart.AddImagePart(ImagePartType.Png);
                    using var stream = new MemoryStream(pngBytes);
                    imagePart.FeedData(stream);

                    var relationshipId = mainPart.GetIdOfPart(imagePart);
                    var drawing = CreateImageDrawing(relationshipId, tagName, 5394960, 3600000, _nextDrawingId++);

                    if (text.Parent is Run parentRun)
                        parentRun.InsertAfterSelf(new Run(drawing));

                    text.Text = text.Text.Replace($"[{tagName}]", string.Empty, StringComparison.Ordinal);
                }
                catch
                {
                    text.Text = text.Text.Replace($"[{tagName}]", "(Error al generar imagen)", StringComparison.Ordinal);
                }
            }
        }
    }

    private static string DetectDiagramFormat(string diagramCode)
    {
        var code = diagramCode.TrimStart();
        if (code.StartsWith("@startuml", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("@enduml", StringComparison.OrdinalIgnoreCase))
            return "plantuml";

        if (code.StartsWith("graph ", StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith("flowchart ", StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith("sequenceDiagram", StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith("classDiagram", StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith("erDiagram", StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith("stateDiagram", StringComparison.OrdinalIgnoreCase))
            return "mermaid";

        return "plantuml";
    }

    private static Drawing CreateImageDrawing(string relationshipId, string imageName, long widthEmus, long heightEmus, uint drawingId)
    {
        return new Drawing(
            new DW.Inline(
                new DW.Extent() { Cx = widthEmus, Cy = heightEmus },
                new DW.EffectExtent() { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties() { Id = drawingId, Name = imageName },
                new DW.NonVisualGraphicFrameDrawingProperties(
                    new A.GraphicFrameLocks() { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties() { Id = 0U, Name = imageName },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip() { Embed = relationshipId, CompressionState = A.BlipCompressionValues.Print },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset() { X = 0L, Y = 0L },
                                    new A.Extents() { Cx = widthEmus, Cy = heightEmus }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
            )
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U,
                EditId = "50D07946"
            });
    }

    public async Task<bool> ExportToMarkdownAsync(DocumentSession session, string outputPath)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# {session.Title}");
        foreach (var section in session.Sections.OrderBy(s => s.Order))
        {
            sb.AppendLine($"## {section.Title}");
            sb.AppendLine(section.Content);
        }
        await File.WriteAllTextAsync(outputPath, sb.ToString());
        return true;
    }
}
