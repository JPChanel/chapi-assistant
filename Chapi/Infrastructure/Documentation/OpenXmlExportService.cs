using System.IO;
using System.Text.Json;
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
            var bodyBlockStarts = GetBlockStartTags(mainPart.Document.Body);

            ExpandDynamicBlocksInContainer(mainPart.Document.Body, tags);
            foreach (var headerPart in mainPart.HeaderParts)
            {
                if (headerPart.Header != null)
                    ExpandDynamicBlocksInContainer(headerPart.Header, tags);
            }
            foreach (var footerPart in mainPart.FooterParts)
            {
                if (footerPart.Footer != null)
                    ExpandDynamicBlocksInContainer(footerPart.Footer, tags);
            }

            AppendMissingDynamicBlockFallbacks(mainPart.Document.Body, tags, bodyBlockStarts);

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

    private static void ExpandDynamicBlocksInContainer(OpenXmlElement container, Dictionary<string, string> tags)
    {
        if (container is not OpenXmlCompositeElement composite)
            return;

        var imageTagCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        ExpandDynamicBlocksRecursive(composite, tags, imageTagCounters);
    }

    private static void ExpandDynamicBlocksRecursive(
        OpenXmlCompositeElement parent,
        Dictionary<string, string> tags,
        Dictionary<string, int> imageTagCounters)
    {
        while (TryExpandOneDynamicBlock(parent, tags, imageTagCounters))
        {
            // Expandimos repetidamente hasta que no queden bloques INICIO/FIN en este nivel.
        }

        foreach (var child in parent.Elements<OpenXmlCompositeElement>().ToList())
            ExpandDynamicBlocksRecursive(child, tags, imageTagCounters);
    }

    private static bool TryExpandOneDynamicBlock(
        OpenXmlCompositeElement parent,
        Dictionary<string, string> tags,
        Dictionary<string, int> imageTagCounters)
    {
        var children = parent.ChildElements.ToList();
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is not Paragraph startParagraph)
                continue;

            if (!TryGetBlockStartKey(startParagraph, out var startKey))
                continue;

            var endKey = startKey.Replace("_INICIO", "_FIN", StringComparison.OrdinalIgnoreCase);
            var itemsKey = startKey.Replace("_INICIO", "_ITEMS", StringComparison.OrdinalIgnoreCase);
            var endIndex = FindBlockEndIndex(children, i + 1, endKey);
            if (endIndex < 0)
                continue;

            var templateNodes = children
                .Skip(i + 1)
                .Take(endIndex - i - 1)
                .ToList();

            var rows = ParseDynamicRows(tags.TryGetValue(itemsKey, out var rawItems) ? rawItems : string.Empty);
            if (rows.Count > 0)
            {
                var endNode = children[endIndex];
                for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    foreach (var templateNode in templateNodes)
                    {
                        var clone = templateNode.CloneNode(true);
                        ApplyRowDataToElement(
                            clone,
                            rows[rowIndex],
                            tags,
                            itemsKey,
                            rowIndex + 1,
                            imageTagCounters);
                        parent.InsertBefore(clone, endNode);
                    }
                }

                foreach (var templateNode in templateNodes)
                    templateNode.Remove();
            }

            // Los marcadores no deben quedar en el documento final.
            children[i].Remove();
            children[endIndex].Remove();
            return true;
        }

        return false;
    }

    private static bool TryGetBlockStartKey(Paragraph paragraph, out string startKey)
    {
        startKey = string.Empty;
        var text = GetParagraphText(paragraph);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = Regex.Match(text, @"\[(?<key>BLOQUE_[A-Z0-9_]+_INICIO)\]", RegexOptions.IgnoreCase);
        if (!match.Success)
            return false;

        startKey = match.Groups["key"].Value.ToUpperInvariant();
        return true;
    }

    private static int FindBlockEndIndex(IReadOnlyList<OpenXmlElement> nodes, int startIndex, string endKey)
    {
        for (var i = startIndex; i < nodes.Count; i++)
        {
            if (nodes[i] is not Paragraph paragraph)
                continue;

            var text = GetParagraphText(paragraph);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (text.Contains($"[{endKey}]", StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static string GetParagraphText(Paragraph paragraph) =>
        string.Concat(paragraph.Descendants<Text>().Select(t => t.Text));

    private static List<Dictionary<string, string>> ParseDynamicRows(string raw)
    {
        var rows = new List<Dictionary<string, string>>();
        if (string.IsNullOrWhiteSpace(raw))
            return rows;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return rows;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in item.EnumerateObject())
                {
                    row[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                        JsonValueKind.Array or JsonValueKind.Object => prop.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                        _ => prop.Value.ToString()
                    };
                }

                rows.Add(row);
            }
        }
        catch (JsonException)
        {
            // Si el JSON es inválido, conservamos el bloque una sola vez (fallback).
        }

        return rows;
    }

    private static void ApplyRowDataToElement(
        OpenXmlElement element,
        IReadOnlyDictionary<string, string> rowData,
        Dictionary<string, string> tags,
        string itemsKey,
        int rowIndex,
        Dictionary<string, int> imageTagCounters)
    {
        foreach (var paragraph in element.Descendants<Paragraph>())
        {
            var textNodes = paragraph.Descendants<Text>().ToList();
            if (textNodes.Count == 0)
                continue;

            var paragraphText = string.Concat(textNodes.Select(t => t.Text));
            if (string.IsNullOrWhiteSpace(paragraphText))
                continue;

            var replacedAny = false;
            var hasLongValue = false;
            var replaced = Regex.Replace(
                paragraphText,
                @"\[(?<key>[A-Z0-9_]+)\]",
                match =>
                {
                    var key = match.Groups["key"].Value;
                    if (!rowData.TryGetValue(key, out var value))
                        return match.Value;

                    replacedAny = true;
                    value ??= string.Empty;
                    if (value.Length >= 80)
                        hasLongValue = true;

                    if (!IsImageKey(key))
                        return value;

                    if (string.IsNullOrWhiteSpace(value))
                        return string.Empty;

                    var uniqueImageKey = CreateUniqueImageTagKey(key, itemsKey, rowIndex, imageTagCounters);
                    tags[uniqueImageKey] = value;
                    return $"[{uniqueImageKey}]";
                },
                RegexOptions.CultureInvariant);

            if (!string.Equals(paragraphText, replaced, StringComparison.Ordinal))
            {
                RewriteParagraphText(paragraph, replaced);
                NormalizeParagraphFormatting(paragraph, justify: replacedAny && hasLongValue);
            }
        }
    }

    private static bool IsImageKey(string key) =>
        key.StartsWith("IMG_", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("DIAGRAMA_", StringComparison.OrdinalIgnoreCase);

    private static string CreateUniqueImageTagKey(
        string baseKey,
        string itemsKey,
        int rowIndex,
        Dictionary<string, int> counters)
    {
        var safeItemsKey = Regex.Replace(itemsKey.ToUpperInvariant(), @"[^A-Z0-9_]", "_");
        var counterKey = $"{baseKey}_{safeItemsKey}_{rowIndex}";
        if (!counters.TryGetValue(counterKey, out var count))
            count = 0;

        count++;
        counters[counterKey] = count;
        return $"{counterKey}_{count}";
    }

    private static HashSet<string> GetBlockStartTags(OpenXmlElement container)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var text in container.Descendants<Text>())
        {
            var value = text.Text;
            if (string.IsNullOrWhiteSpace(value))
                continue;

            foreach (Match match in Regex.Matches(value, @"\[(?<key>BLOQUE_[A-Z0-9_]+_INICIO)\]", RegexOptions.IgnoreCase))
            {
                if (match.Success)
                    set.Add(match.Groups["key"].Value.ToUpperInvariant());
            }
        }

        return set;
    }

    private static bool ContainsTag(OpenXmlElement container, string tagKey)
    {
        var needle = $"[{tagKey}]";
        foreach (var text in container.Descendants<Text>())
        {
            if (text.Text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void AppendMissingDynamicBlockFallbacks(
        Body body,
        Dictionary<string, string> tags,
        HashSet<string> originalBlockStarts)
    {
        var dynamicBlockKeys = GetOrderedDynamicItemsKeys(tags);
        if (dynamicBlockKeys.Count == 0)
            return;

        var imageTagCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var appendedAny = false;

        foreach (var itemsKey in dynamicBlockKeys)
        {
            if (!tags.TryGetValue(itemsKey, out var rawItems))
                continue;

            var rows = ParseDynamicRows(rawItems);
            if (rows.Count == 0)
                continue;

            var startKey = itemsKey.Replace("_ITEMS", "_INICIO", StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
            var hasStructuredBlock = originalBlockStarts.Contains(startKey) && !ContainsTag(body, startKey);
            if (hasStructuredBlock)
                continue;

            AppendDynamicFallbackSection(body, itemsKey, rows, tags, imageTagCounters);
            appendedAny = true;
        }

        if (!appendedAny)
            return;

        // Separador final para evitar que el footer quede pegado al último bloque agregado.
        body.AppendChild(CreateParagraph(string.Empty));
    }

    private static List<string> GetOrderedDynamicItemsKeys(Dictionary<string, string> tags)
    {
        var preferred = new[]
        {
            "BLOQUE_PQ_ITEMS",
            "BLOQUE_CU_ITEMS",
            "BLOQUE_ACT_ITEMS",
            "BLOQUE_SEQ_ITEMS",
            "BLOQUE_EST_ITEMS",
            "BLOQUE_CAPAS_ITEMS",
            "BLOQUE_COMP_ITEMS",
            "BLOQUE_CLASE_DET_ITEMS",
            "BLOQUE_DICC_TABLA_ITEMS"
        };

        var ordered = preferred
            .Where(tags.ContainsKey)
            .ToList();

        var extras = tags.Keys
            .Where(k => k.EndsWith("_ITEMS", StringComparison.OrdinalIgnoreCase))
            .Where(k => !ordered.Contains(k, StringComparer.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);
        ordered.AddRange(extras);

        return ordered;
    }

    private static void AppendDynamicFallbackSection(
        Body body,
        string itemsKey,
        IReadOnlyList<Dictionary<string, string>> rows,
        Dictionary<string, string> tags,
        Dictionary<string, int> imageTagCounters)
    {
        body.AppendChild(CreateParagraph(string.Empty));
        body.AppendChild(CreateParagraph($"[Auto] {itemsKey}", bold: true));

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            body.AppendChild(CreateParagraph($"Item {i + 1}", bold: true));

            foreach (var field in row.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var key = field.Key;
                var value = field.Value ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (!IsImageKey(key))
                {
                    body.AppendChild(CreateParagraph($"{key}: {value}"));
                    continue;
                }

                var uniqueImageKey = CreateUniqueImageTagKey(key, itemsKey, i + 1, imageTagCounters);
                tags[uniqueImageKey] = value;
                body.AppendChild(CreateParagraph($"{key}: [{uniqueImageKey}]"));
            }
        }
    }

    private static Paragraph CreateParagraph(string text, bool bold = false)
    {
        var paragraph = new Paragraph();
        var run = new Run();
        if (bold)
            run.RunProperties = new RunProperties(new Bold());

        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        paragraph.AppendChild(run);
        return paragraph;
    }

    private static void ReplaceTagsInContainer(OpenXmlElement container, Dictionary<string, string> tags)
    {
        // Word puede partir un placeholder entre varios w:t/runs.
        // Procesamos por párrafo para reconstruir y reemplazar correctamente.
        foreach (var paragraph in container.Descendants<Paragraph>())
        {
            var textNodes = paragraph.Descendants<Text>().ToList();
            if (textNodes.Count == 0) continue;

            var paragraphText = string.Concat(textNodes.Select(t => t.Text));
            if (string.IsNullOrWhiteSpace(paragraphText)) continue;

            var replacedAny = false;
            var hasLongValue = false;
            var replaced = Regex.Replace(
                paragraphText,
                @"\[(?<key>[A-Z0-9_]+)\]",
                m =>
                {
                    var key = m.Groups["key"].Value;
                    if (!tags.TryGetValue(key, out var value))
                        return m.Value;

                    replacedAny = true;

                    // Los tags de imagen se manejan en HandleImagesInContainerAsync.
                    if (key.StartsWith("IMG_", StringComparison.OrdinalIgnoreCase) ||
                        key.StartsWith("DIAGRAMA_", StringComparison.OrdinalIgnoreCase))
                        return string.Empty;

                    value ??= string.Empty;
                    if (value.Length >= 80)
                        hasLongValue = true;

                    return value;
                },
                RegexOptions.CultureInvariant);

            if (!string.Equals(paragraphText, replaced, StringComparison.Ordinal))
            {
                RewriteParagraphText(paragraph, replaced);
                NormalizeParagraphFormatting(paragraph, justify: replacedAny && hasLongValue);
            }
        }
    }

    private async Task HandleImagesInContainerAsync(MainDocumentPart mainPart, OpenXmlElement container, Dictionary<string, string> metadata)
    {
        foreach (var paragraph in container.Descendants<Paragraph>())
        {
            var textNodes = paragraph.Descendants<Text>().ToList();
            if (textNodes.Count == 0) continue;

            var paragraphText = string.Concat(textNodes.Select(t => t.Text));
            if (string.IsNullOrWhiteSpace(paragraphText)) continue;

            var matches = Regex.Matches(paragraphText, @"\[(IMG_[^\]]+|DIAGRAMA_[^\]]+)\]");
            if (matches.Count == 0) continue;

            var updatedText = paragraphText;

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

                    // Insertar imagen en el mismo párrafo donde estaba el tag.
                    paragraph.AppendChild(new Run(drawing));
                    updatedText = updatedText.Replace($"[{tagName}]", string.Empty, StringComparison.Ordinal);
                }
                catch
                {
                    updatedText = updatedText.Replace($"[{tagName}]", "(Error al generar imagen)", StringComparison.Ordinal);
                }
            }

            if (!string.Equals(updatedText, paragraphText, StringComparison.Ordinal))
                RewriteParagraphText(paragraph, updatedText);
        }
    }

    private static void RewriteParagraphText(Paragraph paragraph, string value)
    {
        var textNodes = paragraph.Descendants<Text>().ToList();
        if (textNodes.Count == 0)
        {
            paragraph.AppendChild(new Run(new Text(value) { Space = SpaceProcessingModeValues.Preserve }));
            return;
        }

        textNodes[0].Text = value;
        textNodes[0].Space = SpaceProcessingModeValues.Preserve;
        for (var i = 1; i < textNodes.Count; i++)
            textNodes[i].Text = string.Empty;
    }

    private static void NormalizeParagraphFormatting(Paragraph paragraph, bool justify)
    {
        // Quita formato inline del placeholder (hipervínculo/itálica/size custom).
        foreach (var run in paragraph.Elements<Run>())
            run.RunProperties = null;

        paragraph.ParagraphProperties ??= new ParagraphProperties();
        paragraph.ParagraphProperties.RemoveAllChildren<ParagraphMarkRunProperties>();

        if (!justify) return;

        var jc = paragraph.ParagraphProperties.GetFirstChild<Justification>();
        if (jc == null)
            paragraph.ParagraphProperties.Append(new Justification { Val = JustificationValues.Both });
        else
            jc.Val = JustificationValues.Both;
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

