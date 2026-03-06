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

    public OpenXmlExportService(IKrokiDiagramService krokiService)
    {
        _krokiService = krokiService;
    }

    public async Task<bool> ExportToWordAsync(DocumentSession session, string outputPath)
    {
        try
        {
            // Determinar la plantilla base
            string templatePath = GetTemplatePath(session.Template);
            if (!File.Exists(templatePath)) return false;

            // Crear una copia de la plantilla
            File.Copy(templatePath, outputPath, true);

            using (var doc = WordprocessingDocument.Open(outputPath, true))
            {
                var mainPart = doc.MainDocumentPart;
                if (mainPart == null) return false;

                // 1. Reemplazar tags globales (Metadata de sesión)
                ReplaceGlobalTags(mainPart, session);

                // 1.5 Procesar imágenes globales (Kroki API)
                await HandleImagesInContainerAsync(mainPart, mainPart.Document.Body, session.Metadata);

                // 2. Procesar Bloques Repetibles (Repeaters)
                await ProcessBlocksAsync(mainPart, session);

                // 3. Reemplazar tags en secciones de contenido
                ReplaceSectionTags(mainPart, session);

                mainPart.Document.Save();
            }

            return true;
        }
        catch (Exception ex)
        {
            // Log error here if needed
            return false;
        }
    }

    private string GetTemplatePath(DocTemplate template)
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var templateName = template == DocTemplate.ModeloSoftware ? "01.Modelo de Software.docx" : "02.Diseño del Sistema de Información.docx";
        return Path.Combine(appDir, "PlantillaWord", templateName);
    }

    private void ReplaceGlobalTags(MainDocumentPart mainPart, DocumentSession session)
    {
        var root = mainPart.Document.Body;
        if (root == null) return;

        var tags = new Dictionary<string, string>(session.Metadata)
        {
            ["PROYECTO_NOMBRE"] = session.ProjectName,
            ["PROYECTO_CODIGO"] = session.Id.Substring(0, 8), // O un campo real
            ["DOC_VERSION"] = session.Version,
            ["DOC_MES_ANIO"] = DateTime.Now.ToString("MMMM, yyyy")
        };

        ReplaceTagsInContainer(root, tags);
        
        // También en Headers y Footers
        foreach (var headerPart in mainPart.HeaderParts) ReplaceTagsInContainer(headerPart.Header, tags);
        foreach (var footerPart in mainPart.FooterParts) ReplaceTagsInContainer(footerPart.Footer, tags);
    }

    private async Task ProcessBlocksAsync(MainDocumentPart mainPart, DocumentSession session)
    {
        var body = mainPart.Document.Body;
        if (body == null) return;

        // Lista de tipos de bloques a procesar
        var blockTypes = new[] { "PQ", "CU", "ACT", "SEQ", "EST", "CAPAS", "COMP", "CLASE_DET", "DICC_TABLA", "HISTORIAL" };

        foreach (var type in blockTypes)
        {
            await ProcessRepeaterBlockAsync(mainPart, body, type, session);
        }
    }

    private async Task ProcessRepeaterBlockAsync(MainDocumentPart mainPart, Body body, string blockType, DocumentSession session)
    {
        var startTag = $"[BLOQUE_{blockType}_INICIO]";
        var endTag = $"[BLOQUE_{blockType}_FIN]";

        while (true)
        {
            var startElement = FindElementWithText(body, startTag);
            var endElement = FindElementWithText(body, endTag);

            if (startElement == null || endElement == null) break;

            // Extraer los elementos entre los tags
            var templateElements = GetElementsBetween(startElement, endElement);
            
            // Determinar qué datos usar para este bloque
            var itemsMetadata = GetMetadataForBlock(blockType, session);

            // Eliminar los tags de la plantilla
            startElement.Remove();
            endElement.Remove();

            if (itemsMetadata.Any())
            {
                var lastInserted = templateElements.LastOrDefault() ?? endElement;
                
                foreach (var metadata in itemsMetadata)
                {
                    // Clonar y rellenar
                    foreach (var element in templateElements)
                    {
                        var clone = element.CloneNode(true);
                        
                        // Reemplazar tags en el clon
                        ReplaceTagsInContainer(clone, metadata);
                        
                        // Manejo especial de imágenes en el bloque
                        await HandleImagesInContainerAsync(mainPart, clone, metadata);

                        body.InsertAfter(clone, lastInserted);
                        lastInserted = clone;
                    }
                }
            }

            // Eliminar los elementos originales de la plantilla
            foreach (var el in templateElements) el.Remove();
        }
    }

    private List<OpenXmlElement> GetElementsBetween(OpenXmlElement start, OpenXmlElement end)
    {
        var elements = new List<OpenXmlElement>();
        var current = start.NextSibling();
        while (current != null && current != end)
        {
            elements.Add(current);
            current = current.NextSibling();
        }
        return elements;
    }

    private OpenXmlElement? FindElementWithText(OpenXmlElement container, string text)
    {
        return container.Descendants<Text>()
            .FirstOrDefault(t => t.Text.Contains(text))?
            .Ancestors<Paragraph>().FirstOrDefault() ?? (OpenXmlElement?)
               container.Descendants<Text>()
            .FirstOrDefault(t => t.Text.Contains(text))?
            .Ancestors<TableRow>().FirstOrDefault();
    }

    private void ReplaceTagsInContainer(OpenXmlElement container, Dictionary<string, string> tags)
    {
        var texts = container.Descendants<Text>().ToList();
        foreach (var t in texts)
        {
            foreach (var tag in tags)
            {
                var fullTag = $"[{tag.Key}]";
                if (t.Text.Contains(fullTag))
                {
                    t.Text = t.Text.Replace(fullTag, tag.Value ?? "");
                }
            }
        }
    }

    private async Task HandleImagesInContainerAsync(MainDocumentPart mainPart, OpenXmlElement container, Dictionary<string, string> metadata)
    {
        var imageTags = container.Descendants<Text>().Where(t => t.Text.Contains("[IMG_") || t.Text.Contains("[DIAGRAMA_")).ToList();
        foreach (var t in imageTags)
        {
            var match = Regex.Match(t.Text, @"\[(IMG_[^\]]+|DIAGRAMA_[^\]]+)\]");
            if (match.Success)
            {
                var tagName = match.Groups[1].Value;
                if (metadata.TryGetValue(tagName, out var diagramCode) && !string.IsNullOrWhiteSpace(diagramCode) && !diagramCode.StartsWith("["))
                {
                    try
                    {
                        var pngBytes = await _krokiService.RenderToPngAsync(diagramCode, "plantuml"); // Asumimos plantuml o inferimos del código
                        if (pngBytes != null)
                        {
                            var imagePart = mainPart.AddImagePart(ImagePartType.Png);
                            using (var stream = new MemoryStream(pngBytes))
                            {
                                imagePart.FeedData(stream);
                            }

                            string relationshipId = mainPart.GetIdOfPart(imagePart);
                            
                            // Tamaño aproximado 15cm x 10cm (en EMUs)
                            long widthEmus = 5394960;
                            long heightEmus = 3600000;
                            
                            var drawing = CreateImageDrawing(relationshipId, tagName, widthEmus, heightEmus);

                            // Insertar Elemento Drawing y borrar el texto
                            var parentRun = t.Parent as Run;
                            if (parentRun != null)
                            {
                                parentRun.InsertAfterSelf(new Run(drawing));
                            }
                            t.Text = t.Text.Replace($"[{tagName}]", "");
                        }
                    }
                    catch
                    {
                        t.Text = t.Text.Replace($"[{tagName}]", "(Error al cargar imagen generada)");
                    }
                }
            }
        }
    }

    private Drawing CreateImageDrawing(string relationshipId, string imageName, long widthEmus, long heightEmus)
    {
        return new Drawing(
            new DW.Inline(
                new DW.Extent() { Cx = widthEmus, Cy = heightEmus },
                new DW.EffectExtent() { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties() { Id = (UInt32Value)1U, Name = imageName },
                new DW.NonVisualGraphicFrameDrawingProperties(
                    new A.GraphicFrameLocks() { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties() { Id = (UInt32Value)0U, Name = imageName },
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
            ) { DistanceFromTop = (UInt32Value)0U, DistanceFromBottom = (UInt32Value)0U, DistanceFromLeft = (UInt32Value)0U, DistanceFromRight = (UInt32Value)0U, EditId = "50D07946" });
    }

    private List<Dictionary<string, string>> GetMetadataForBlock(string blockType, DocumentSession session)
    {
        // Esta lógica mapea las secciones de la sesión a listas de metadata para los bloques
        // Ej: BLOQUE_CU mapea a los casos de uso generados
        var result = new List<Dictionary<string, string>>();

        if (blockType == "CU")
        {
            return session.Sections
                .Where(s => s.Title.Contains("CU") && s.Metadata.Any())
                .Select(s => s.Metadata)
                .ToList();
        }
        // ... otros mapeos
        
        return result;
    }

    private void ReplaceSectionTags(MainDocumentPart mainPart, DocumentSession session)
    {
        // Reemplazo final para secciones sueltas
        var tags = session.Sections.ToDictionary(s => s.Title, s => s.Content);
        ReplaceTagsInContainer(mainPart.Document.Body, tags);
    }

    public async Task<bool> ExportToMarkdownAsync(DocumentSession session, string outputPath)
    {
        // Mantener implementación básica o mejorarla
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
