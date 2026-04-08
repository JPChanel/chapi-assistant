using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chapi.Application.Interfaces;
using Chapi.Domain.Documentation;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SixLabors.ImageSharp;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Chapi.Infrastructure.Documentation;

public class OpenXmlExportService : IDocumentExportService
{
    private static readonly string DebugLogPath = Path.Combine(Path.GetTempPath(), "chapi-export-debug.log");
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
            ResetDebugLog(session, outputPath);
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDir))
                Directory.CreateDirectory(outputDir);

            using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());

            EnsureSettings(doc);

            var body = mainPart.Document.Body!;
            var sectionProperties = CreateHeaderFooter(mainPart, session);

            AppendCover(body, session);
            body.AppendChild(CreatePageBreakParagraph());

            AppendHeading(body, "Índice general", 1);
            body.AppendChild(CreateFieldParagraph(@"TOC \o ""1-3"" \h \z \u"));
            body.AppendChild(CreateParagraph("Actualiza los campos al abrir el documento si Word no refresca el índice automáticamente.", italic: true, fontSize: "18", color: "6B7280"));
            body.AppendChild(CreatePageBreakParagraph());

            AppendHeading(body, "Índice de tablas", 1);
            body.AppendChild(CreateFieldParagraph(@"TOC \h \z \c ""Tabla"""));
            body.AppendChild(CreateParagraph("Actualiza los campos al abrir el documento si Word no refresca el índice automáticamente.", italic: true, fontSize: "18", color: "6B7280"));
            body.AppendChild(CreatePageBreakParagraph());

            AppendHeading(body, "Índice de figuras", 1);
            body.AppendChild(CreateFieldParagraph(@"TOC \h \z \c ""Figura"""));
            body.AppendChild(CreateParagraph("Actualiza los campos al abrir el documento si Word no refresca el índice automáticamente.", italic: true, fontSize: "18", color: "6B7280"));
            body.AppendChild(CreatePageBreakParagraph());

            var chapterNumber = 0;
            foreach (var section in session.Sections.OrderBy(s => s.Order))
            {
                var heading = ResolveHeading(section.Title, ref chapterNumber, out var level);
                AppendHeading(body, heading, level);

                LogDebug($"SECTION START order={section.Order} title='{section.Title}' type={section.Type} contentLen={section.Content?.Length ?? 0} diagramLen={section.DiagramCode?.Length ?? 0} imageLen={section.ImageBase64?.Length ?? 0}");
                List<OpenXmlElement> elements;
                try
                {
                    elements = await BuildSectionElementsAsync(mainPart, session, section);
                }
                catch (Exception sectionEx)
                {
                    LogDebug($"SECTION ERROR order={section.Order} title='{section.Title}' ex={sectionEx}");
                    throw;
                }

                LogDebug($"SECTION END order={section.Order} title='{section.Title}' elements={elements.Count}");
                foreach (var element in elements)
                    body.AppendChild(element);

                body.AppendChild(CreateParagraph(string.Empty));
            }

            body.AppendChild(sectionProperties);
            mainPart.Document.Save();
            LogDebug("EXPORT OK");
            return true;
        }
        catch (Exception ex)
        {
            LogDebug($"EXPORT ERROR ex={ex}");
            return false;
        }
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

    private void EnsureSettings(WordprocessingDocument doc)
    {
        var settingsPart = doc.MainDocumentPart!.DocumentSettingsPart ?? doc.MainDocumentPart.AddNewPart<DocumentSettingsPart>();
        settingsPart.Settings = new Settings(new UpdateFieldsOnOpen { Val = true });
    }

    private static void AppendCover(Body body, DocumentSession session)
    {
        body.AppendChild(CreateParagraph("CHAPI ASSISTANT", bold: true, fontSize: "36", color: "1F3A5F", justify: JustificationValues.Center, spacingBefore: 900, spacingAfter: 120));
        body.AppendChild(CreateParagraph(session.Title, bold: true, fontSize: "32", color: "0F172A", justify: JustificationValues.Center, spacingAfter: 160));
        body.AppendChild(CreateParagraph($"Proyecto: {session.ProjectName}", fontSize: "22", justify: JustificationValues.Center, spacingAfter: 60));
        body.AppendChild(CreateParagraph($"Versión: {session.Version}", fontSize: "22", justify: JustificationValues.Center, spacingAfter: 60));
        body.AppendChild(CreateParagraph(DateTime.Now.ToString("dd 'de' MMMM 'de' yyyy", new CultureInfo("es-PE")), fontSize: "22", color: "475569", justify: JustificationValues.Center, spacingAfter: 320));

        body.AppendChild(CreateParagraph("Control del documento", bold: true, fontSize: "24", color: "1F3A5F", spacingBefore: 240, spacingAfter: 120));
        body.AppendChild(CreateKeyValueTable(new[]
        {
            ("Código", GetMetadataValue(session, "REF_CODIGO", session.Id[..Math.Min(8, session.Id.Length)])),
            ("Sistema", GetMetadataValue(session, "REF_SISTEMA", session.ProjectName)),
            ("Documentos de referencia", GetMetadataValue(session, "REF_DOCS", "N/A")),
            ("Elaborado por", GetMetadataValue(session, "ELAB_NOM")),
            ("Fecha elaboración", GetMetadataValue(session, "ELAB_FECHA")),
            ("Revisado por", GetMetadataValue(session, "REV_NOM")),
            ("Fecha revisión", GetMetadataValue(session, "REV_FECHA")),
            ("Aprobado por", GetMetadataValue(session, "APROB_NOM")),
            ("Fecha aprobación", GetMetadataValue(session, "APROB_FECHA"))
        }));

        body.AppendChild(CreateParagraph("Historial de versiones", bold: true, fontSize: "24", color: "1F3A5F", spacingBefore: 240, spacingAfter: 120));
        body.AppendChild(CreateTable(
            ["Versión", "Fecha", "Elaborado por", "Descripción", "Revisado por", "Fecha revisión"],
            [[
                GetMetadataValue(session, "HIST_VER", session.Version),
                GetMetadataValue(session, "HIST_FECHA_ELAB", GetMetadataValue(session, "ELAB_FECHA")),
                GetMetadataValue(session, "HIST_ELAB", GetMetadataValue(session, "ELAB_NOM")),
                GetMetadataValue(session, "HIST_DESC", "Versión inicial del documento"),
                GetMetadataValue(session, "HIST_REV", GetMetadataValue(session, "REV_NOM")),
                GetMetadataValue(session, "HIST_FECHA_REV", GetMetadataValue(session, "REV_FECHA"))
            ]]));
    }

    private SectionProperties CreateHeaderFooter(MainDocumentPart mainPart, DocumentSession session)
    {
        var headerPart = mainPart.AddNewPart<HeaderPart>();
        headerPart.Header = new Header(
            CreateTable(
                ["Documento", "Proyecto", "Versión"],
                [[session.Title, session.ProjectName, session.Version]],
                fontSize: "18",
                headerFill: "DCE6F1",
                bordersColor: "B8CCE4",
                columnWidths: [5200, 5200, 1600]));
        headerPart.Header.Save();

        var footerPart = mainPart.AddNewPart<FooterPart>();
        footerPart.Footer = new Footer(
            CreateParagraph($"{session.ProjectName} · Página", fontSize: "18", color: "64748B", justify: JustificationValues.Center),
            CreatePageNumberParagraph());
        footerPart.Footer.Save();

        return new SectionProperties(
            new HeaderReference { Type = HeaderFooterValues.Default, Id = mainPart.GetIdOfPart(headerPart) },
            new FooterReference { Type = HeaderFooterValues.Default, Id = mainPart.GetIdOfPart(footerPart) },
            new PageMargin { Top = 1134, Right = 1134, Bottom = 1134, Left = 1134, Header = 720, Footer = 720, Gutter = 0 },
            new PageSize { Width = 11906, Height = 16838 });
    }

    private async Task<List<OpenXmlElement>> BuildSectionElementsAsync(MainDocumentPart mainPart, DocumentSession session, DocSection section)
    {
        return session.Template switch
        {
            DocTemplate.ModeloSoftware => await BuildModeloSoftwareSectionAsync(mainPart, session, section),
            DocTemplate.DisenoSistema => await BuildDisenoSistemaSectionAsync(mainPart, session, section),
            _ => []
        };
    }

    private async Task<List<OpenXmlElement>> BuildModeloSoftwareSectionAsync(MainDocumentPart mainPart, DocumentSession session, DocSection section)
    {
        var elements = new List<OpenXmlElement>();
        switch (section.Order)
        {
            case 1:
                AppendMarkdownOrMetadata(elements, section, GetMetadataValue(session, "INTRODUCCION"));
                break;
            case 2:
                AppendMarkdownOrMetadata(elements, section, GetMetadataValue(session, "OBJETIVOS"));
                break;
            case 3:
                AppendMarkdownOrMetadata(elements, section, GetMetadataValue(session, "ALCANCE"));
                break;
            case 4:
                AppendMarkdownOrMetadata(elements, section, GetMetadataValue(session, "PQ_VISTA_LOGICA_DESC"));
                break;
            case 5:
                if (HasSectionText(section))
                    AppendMarkdownOrMetadata(elements, section, string.Empty);
                else
                {
                    AppendCaption(elements, "Tabla", "Listado de paquetes");
                    elements.Add(CreateSmartListTable(GetMetadataValue(session, "TABLA_RESUMEN_PQ"), "Paquete"));
                }
                break;
            case 6:
                if (!await TryAppendSectionVisualAsync(mainPart, elements, section, "Vista lógica del sistema"))
                    await AppendDiagramFromMetadataAsync(mainPart, elements, GetMetadataValue(session, "IMG_VISTA_LOGICA"), "Vista lógica del sistema");
                break;
            case 7:
                if (HasSectionText(section))
                    AppendMarkdownOrMetadata(elements, section, string.Empty);
                else
                    AppendPackageSpecification(elements, session);
                break;
            case 8:
                if (HasSectionText(section))
                    AppendMarkdownOrMetadata(elements, section, string.Empty);
                break;
            case 9:
                if (HasSectionText(section))
                    AppendMarkdownOrMetadata(elements, section, string.Empty);
                else
                {
                    AppendCaption(elements, "Tabla", "Listado de actores");
                    elements.Add(CreateSmartActorTable(GetMetadataValue(session, "TABLA_ACTORES_LISTA")));
                }
                break;
            case 10:
                if (!await TryAppendSectionVisualAsync(mainPart, elements, section, "Diagrama de actores"))
                    await AppendDiagramFromMetadataAsync(mainPart, elements, GetMetadataValue(session, "IMG_ACTORES"), "Diagrama de actores");
                break;
            case 11:
                if (HasSectionText(section))
                    AppendMarkdownOrMetadata(elements, section, string.Empty);
                if (!HasSectionVisual(section) && !string.IsNullOrWhiteSpace(GetMetadataValue(session, "IMG_CU_GENERAL")))
                    await AppendDiagramFromMetadataAsync(mainPart, elements, GetMetadataValue(session, "IMG_CU_GENERAL"), "Diagrama general de casos de uso");
                break;
            case 12:
                if (HasSectionText(section))
                    AppendMarkdownOrMetadata(elements, section, string.Empty);
                else
                {
                    AppendCaption(elements, "Tabla", "Listado de casos de uso");
                    elements.Add(CreateSmartListTable(GetMetadataValue(session, "TABLA_CU_LISTADO"), "Caso de uso"));
                }
                break;
            case 13:
                if (HasSectionText(section))
                    AppendMarkdownOrMetadata(elements, section, string.Empty);
                else
                    await AppendUseCaseSpecificationAsync(mainPart, elements, session);
                break;
            case 14:
                if (!await TryAppendSectionVisualAsync(mainPart, elements, section, "Diagrama de actividad"))
                    await AppendRepeatedDiagramAsync(mainPart, elements, session, "BLOQUE_ACT_ITEMS", "CU_NOM_ACT", "IMG_ACTIVIDAD", "Diagrama de actividad", "CU_ID_ACT", "CU_DESC_ACT");
                break;
            case 15:
                if (!await TryAppendSectionVisualAsync(mainPart, elements, section, "Diagrama de secuencia"))
                    await AppendRepeatedDiagramAsync(mainPart, elements, session, "BLOQUE_SEQ_ITEMS", "CU_NOM_SEQ", "IMG_SECUENCIA", "Diagrama de secuencia", "CU_ID_SEQ", "CU_DESC_SEQ");
                break;
            case 16:
                if (!await TryAppendSectionVisualAsync(mainPart, elements, section, "Diagrama de estados"))
                    await AppendRepeatedDiagramAsync(mainPart, elements, session, "BLOQUE_EST_ITEMS", "CU_NOM_EST", "IMG_ESTADO", "Diagrama de estados", "CU_ID_EST", "CU_DESC_EST");
                break;
            default:
                AppendMarkdownOrMetadata(elements, section, section.Content);
                break;
        }

        if (elements.Count == 0 && !IsContainerSection(session.Template, section.Order))
            elements.Add(CreateParagraph("Sin contenido disponible para esta sección.", italic: true, color: "6B7280"));

        return elements;
    }

    private async Task<List<OpenXmlElement>> BuildDisenoSistemaSectionAsync(MainDocumentPart mainPart, DocumentSession session, DocSection section)
    {
        var elements = new List<OpenXmlElement>();
        switch (section.Order)
        {
            case 1:
                AppendMarkdownOrMetadata(elements, section, GetMetadataValue(session, "INTRODUCCION"));
                break;
            case 2:
                AppendMarkdownOrMetadata(elements, section, GetMetadataValue(session, "OBJETIVOS"));
                break;
            case 3:
                AppendMarkdownOrMetadata(elements, section, GetMetadataValue(session, "ALCANCE"));
                break;
            case 4:
                AppendMarkdownOrMetadata(elements, section, GetMetadataValue(session, "ARQ_DESC_GENERAL"));
                await AppendDiagramFromMetadataAsync(mainPart, elements, GetMetadataValue(session, "IMG_ARQUITECTURA"), "Arquitectura del sistema");
                break;
            case 5:
                AppendCaption(elements, "Tabla", "Descripción de capas");
                elements.Add(CreateJsonRowsTable(session, "BLOQUE_CAPAS_ITEMS", [("CAPA_NOM", "Capa"), ("CAPA_DESC", "Descripción")]));
                break;
            case 6:
                AppendMarkdownOrMetadata(elements, section, section.Content);
                break;
            case 7:
                await AppendDiagramFromMetadataAsync(mainPart, elements, GetMetadataValue(session, "IMG_COMPONENTES"), "Diagrama de componentes");
                break;
            case 8:
                AppendCaption(elements, "Tabla", "Descripción de componentes");
                elements.Add(CreateJsonRowsTable(session, "BLOQUE_COMP_ITEMS", [("COMP_NOM", "Componente"), ("COMP_DESC", "Descripción")]));
                break;
            case 9:
                AppendMarkdownOrMetadata(elements, section, section.Content);
                break;
            case 10:
                await AppendDiagramFromMetadataAsync(mainPart, elements, GetMetadataValue(session, "IMG_CLASES_SISTEMA"), "Diagrama de clases");
                break;
            case 11:
                AppendClassSpecification(elements, session);
                break;
            case 12:
                if (!string.IsNullOrWhiteSpace(section.DiagramCode))
                    await AppendDiagramCodeAsync(mainPart, elements, section.DiagramCode, section.DiagramFormat == DiagramFormat.Mermaid ? "mermaid" : "plantuml", "Diagrama entidad - relación");
                else
                    await AppendDiagramFromMetadataAsync(mainPart, elements, GetMetadataValue(session, "IMG_DER"), "Diagrama entidad - relación");
                break;
            case 13:
                AppendMarkdownOrMetadata(elements, section, section.Content);
                break;
            case 14:
                AppendCaption(elements, "Tabla", "Listado de tablas");
                elements.Add(CreateSmartListTable(GetMetadataValue(session, "TABLA_DICC_RESUMEN"), "Tabla"));
                break;
            case 15:
                AppendDictionaryDetails(elements, session);
                break;
            case 16:
                AppendCaption(elements, "Tabla", "Listado de paquetes");
                elements.Add(CreateSmartListTable(GetMetadataValue(session, "TABLA_OBJ_PQ"), "Paquete"));
                break;
            case 17:
                AppendCaption(elements, "Tabla", "Listado de procedimientos");
                elements.Add(CreateSmartListTable(GetMetadataValue(session, "TABLA_OBJ_PROC"), "Procedimiento"));
                break;
            case 18:
                AppendCaption(elements, "Tabla", "Listado de vistas");
                elements.Add(CreateSmartListTable(GetMetadataValue(session, "TABLA_OBJ_VISTAS"), "Vista"));
                break;
            case 19:
                AppendCaption(elements, "Tabla", "Listado de funciones");
                elements.Add(CreateSmartListTable(GetMetadataValue(session, "TABLA_OBJ_FUNC"), "Función"));
                break;
            case 20:
                AppendCaption(elements, "Tabla", "Listado de índices");
                elements.Add(CreateSmartListTable(GetMetadataValue(session, "TABLA_OBJ_IDX"), "Índice"));
                break;
            default:
                AppendMarkdownOrMetadata(elements, section, section.Content);
                break;
        }

        if (elements.Count == 0 && !IsContainerSection(session.Template, section.Order))
            elements.Add(CreateParagraph("Sin contenido disponible para esta sección.", italic: true, color: "6B7280"));

        return elements;
    }

    private async Task AppendUseCaseSpecificationAsync(MainDocumentPart mainPart, List<OpenXmlElement> elements, DocumentSession session)
    {
        var raw = GetMetadataValue(session, "BLOQUE_CU_ITEMS");
        var rows = GetDynamicRowsWithFallback(
            session,
            "BLOQUE_CU_ITEMS",
            "CU_ID",
            "CU_NOM",
            "CU_DESC",
            "CU_ACTORES",
            "CU_PRE",
            "CU_FLOW_BASE",
            "CU_FLOW_ALT",
            "CU_POST",
            "CU_RESTRIC",
            "CU_PADRE",
            "IMG_PROTOTIPO");
        LogDebug($"BLOCK BLOQUE_CU_ITEMS rawLen={raw.Length} rows={rows.Count}");
        if (rows.Count == 0)
        {
            elements.Add(CreateParagraph("No hay casos de uso detallados generados.", italic: true, color: "6B7280"));
            return;
        }

        var index = 1;
        foreach (var row in rows)
        {
            var title = $"{row.GetValueOrDefault("CU_ID", $"CU-{index}")} - {row.GetValueOrDefault("CU_NOM", $"Caso de uso {index}")}";
            elements.Add(CreateParagraph(title, bold: true, fontSize: "24", color: "1F3A5F", spacingBefore: 180, spacingAfter: 100));
            AppendCaption(elements, "Tabla", $"Especificación de caso de uso {title}");
            elements.Add(CreateKeyValueTable(new[]
            {
                ("Descripción", row.GetValueOrDefault("CU_DESC", string.Empty)),
                ("Actores", row.GetValueOrDefault("CU_ACTORES", string.Empty)),
                ("Precondiciones", row.GetValueOrDefault("CU_PRE", string.Empty)),
                ("Flujo base", row.GetValueOrDefault("CU_FLOW_BASE", string.Empty)),
                ("Flujos alternos", row.GetValueOrDefault("CU_FLOW_ALT", string.Empty)),
                ("Postcondiciones", row.GetValueOrDefault("CU_POST", string.Empty)),
                ("Restricciones", row.GetValueOrDefault("CU_RESTRIC", string.Empty)),
                ("Caso padre", row.GetValueOrDefault("CU_PADRE", string.Empty))
            }));

            if (!string.IsNullOrWhiteSpace(row.GetValueOrDefault("IMG_PROTOTIPO")))
                await AppendDiagramFromMetadataAsync(mainPart, elements, row.GetValueOrDefault("IMG_PROTOTIPO", string.Empty), $"Prototipo de {title}");

            index++;
        }
    }

    private static void AppendPackageSpecification(List<OpenXmlElement> elements, DocumentSession session)
    {
        var raw = GetMetadataValue(session, "BLOQUE_PQ_ITEMS");
        var rows = GetDynamicRowsWithFallback(
            session,
            "BLOQUE_PQ_ITEMS",
            "PQ_ID_NOM",
            "PQ_DESC",
            "PQ_CLASES_LISTA");
        LogDebug($"BLOCK BLOQUE_PQ_ITEMS rawLen={raw.Length} rows={rows.Count}");
        if (rows.Count == 0)
        {
            elements.Add(CreateParagraph("No hay paquetes detallados generados.", italic: true, color: "6B7280"));
            return;
        }

        foreach (var row in rows)
        {
            var title = row.GetValueOrDefault("PQ_ID_NOM", "Paquete");
            elements.Add(CreateParagraph(title, bold: true, fontSize: "24", color: "1F3A5F", spacingBefore: 180, spacingAfter: 100));
            AppendCaption(elements, "Tabla", $"Especificación de paquete {title}");
            elements.Add(CreateKeyValueTable(new[]
            {
                ("Descripción", row.GetValueOrDefault("PQ_DESC", string.Empty)),
                ("Clases relacionadas", row.GetValueOrDefault("PQ_CLASES_LISTA", string.Empty))
            }));
        }
    }

    private static void AppendClassSpecification(List<OpenXmlElement> elements, DocumentSession session)
    {
        var rows = GetDynamicRowsWithFallback(
            session,
            "BLOQUE_CLASE_DET_ITEMS",
            "CLASE_TITULO",
            "CLASE_ATRIB",
            "CLASE_OPER",
            "CLASE_AGREG",
            "CLASE_ASOC");
        if (rows.Count == 0)
        {
            elements.Add(CreateParagraph("No hay clases detalladas generadas.", italic: true, color: "6B7280"));
            return;
        }

        foreach (var row in rows)
        {
            var title = row.GetValueOrDefault("CLASE_TITULO", "Clase");
            elements.Add(CreateParagraph(title, bold: true, fontSize: "24", color: "1F3A5F", spacingBefore: 180, spacingAfter: 100));
            AppendCaption(elements, "Tabla", $"Especificación de clase {title}");
            elements.Add(CreateKeyValueTable(new[]
            {
                ("Atributos", row.GetValueOrDefault("CLASE_ATRIB", string.Empty)),
                ("Operaciones", row.GetValueOrDefault("CLASE_OPER", string.Empty)),
                ("Agregaciones", row.GetValueOrDefault("CLASE_AGREG", string.Empty)),
                ("Asociaciones", row.GetValueOrDefault("CLASE_ASOC", string.Empty))
            }));
        }
    }

    private static void AppendDictionaryDetails(List<OpenXmlElement> elements, DocumentSession session)
    {
        var rows = GetDynamicRowsWithFallback(
            session,
            "BLOQUE_DICC_TABLA_ITEMS",
            "DICC_TABLA_TITULO",
            "COL_NOM",
            "COL_TIPO",
            "COL_PK",
            "COL_DESC");
        if (rows.Count == 0)
        {
            elements.Add(CreateParagraph("No hay tablas detalladas generadas para el diccionario de datos.", italic: true, color: "6B7280"));
            return;
        }

        foreach (var row in rows)
        {
            var title = row.GetValueOrDefault("DICC_TABLA_TITULO", "Tabla");
            elements.Add(CreateParagraph(title, bold: true, fontSize: "24", color: "1F3A5F", spacingBefore: 180, spacingAfter: 100));
            AppendCaption(elements, "Tabla", $"Descripción de tabla {title}");

            var names = SplitByNewLine(row.GetValueOrDefault("COL_NOM", string.Empty));
            var types = SplitByNewLine(row.GetValueOrDefault("COL_TIPO", string.Empty));
            var pks = SplitByNewLine(row.GetValueOrDefault("COL_PK", string.Empty));
            var descriptions = SplitByNewLine(row.GetValueOrDefault("COL_DESC", string.Empty));
            var max = new[] { names.Count, types.Count, pks.Count, descriptions.Count }.Max();

            var dataRows = new List<IReadOnlyList<string>>();
            for (var i = 0; i < max; i++)
            {
                dataRows.Add([
                    names.ElementAtOrDefault(i) ?? string.Empty,
                    types.ElementAtOrDefault(i) ?? string.Empty,
                    pks.ElementAtOrDefault(i) ?? string.Empty,
                    descriptions.ElementAtOrDefault(i) ?? string.Empty
                ]);
            }

            elements.Add(CreateTable(["Columna", "Tipo", "PK", "Descripción"], dataRows));
        }
    }

    private async Task AppendRepeatedDiagramAsync(
        MainDocumentPart mainPart,
        List<OpenXmlElement> elements,
        DocumentSession session,
        string itemsKey,
        string titleKey,
        string imageKey,
        string baseCaption,
        string? idKey = null,
        string? descriptionKey = null)
    {
        var raw = GetMetadataValue(session, itemsKey);
        var fallbackKeys = new List<string> { titleKey, imageKey };
        if (!string.IsNullOrWhiteSpace(idKey))
            fallbackKeys.Add(idKey);
        if (!string.IsNullOrWhiteSpace(descriptionKey))
            fallbackKeys.Add(descriptionKey);

        var rows = GetDynamicRowsWithFallback(session, itemsKey, fallbackKeys.ToArray());
        LogDebug($"BLOCK {itemsKey} rawLen={raw.Length} rows={rows.Count}");
        if (rows.Count == 0)
        {
            elements.Add(CreateParagraph("No hay diagramas generados para esta sección.", italic: true, color: "6B7280"));
            return;
        }

        foreach (var row in rows)
        {
            var id = string.IsNullOrWhiteSpace(idKey) ? string.Empty : row.GetValueOrDefault(idKey, string.Empty);
            var name = row.GetValueOrDefault(titleKey, "Elemento");
            var heading = string.IsNullOrWhiteSpace(id) ? name : $"{id} - {name}";
            elements.Add(CreateParagraph(heading, bold: true, fontSize: "24", color: "1F3A5F", spacingBefore: 180, spacingAfter: 100));

            if (!string.IsNullOrWhiteSpace(descriptionKey))
            {
                var description = row.GetValueOrDefault(descriptionKey, string.Empty);
                if (!string.IsNullOrWhiteSpace(description))
                    elements.Add(CreateKeyValueTable([("Descripción", description)]));
            }

            await AppendDiagramFromMetadataAsync(mainPart, elements, row.GetValueOrDefault(imageKey, string.Empty), $"{baseCaption} - {heading}");
        }
    }

    private async Task AppendDiagramFromMetadataAsync(MainDocumentPart mainPart, List<OpenXmlElement> elements, string code, string caption)
    {
        var (normalizedCode, formatHint) = NormalizeDiagramInput(code);
        LogDebug($"DIAGRAM caption='{caption}' rawLen={code?.Length ?? 0} normalizedLen={normalizedCode.Length} formatHint={formatHint ?? "(auto)"}");
        if (string.IsNullOrWhiteSpace(normalizedCode) || IsPendingPlaceholder(normalizedCode))
        {
            elements.Add(CreateParagraph("Diagrama pendiente de generación.", italic: true, color: "6B7280"));
            return;
        }

        await AppendDiagramCodeAsync(mainPart, elements, normalizedCode, formatHint ?? DetectDiagramFormat(normalizedCode), caption);
    }

    private async Task AppendDiagramCodeAsync(MainDocumentPart mainPart, List<OpenXmlElement> elements, string code, string format, string caption)
    {
        try
        {
            var pngBytes = await _krokiService.RenderToPngAsync(code, format);
            LogDebug($"DIAGRAM RENDER caption='{caption}' format={format} bytes={(pngBytes?.Length ?? 0)}");
            if (pngBytes == null || pngBytes.Length == 0)
            {
                elements.Add(CreateParagraph("No se pudo generar la imagen del diagrama.", italic: true, color: "B91C1C"));
                return;
            }

            AppendCaption(elements, "Figura", caption);
            elements.Add(await CreateImageParagraphAsync(mainPart, pngBytes, caption));
        }
        catch (Exception ex)
        {
            LogDebug($"DIAGRAM ERROR caption='{caption}' format={format} ex={ex}");
            elements.Add(CreateParagraph("Error al generar el diagrama.", italic: true, color: "B91C1C"));
        }
    }

    private async Task<Paragraph> CreateImageParagraphAsync(MainDocumentPart mainPart, byte[] imageBytes, string imageName)
    {
        var imagePart = mainPart.AddImagePart(ImagePartType.Png);
        await using (var stream = new MemoryStream(imageBytes))
        {
            imagePart.FeedData(stream);
        }

        var relationshipId = mainPart.GetIdOfPart(imagePart);
        var (widthEmus, heightEmus) = GetImageSize(imageBytes);
        var drawing = CreateImageDrawing(relationshipId, imageName, widthEmus, heightEmus, _nextDrawingId++);

        return new Paragraph(
            new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
            new Run(drawing));
    }

    private static (long WidthEmus, long HeightEmus) GetImageSize(byte[] imageBytes)
    {
        using var stream = new MemoryStream(imageBytes);
        var info = Image.Identify(stream);
        const long maxWidthEmus = 5_800_000L;
        var widthPx = info?.Width ?? 1600;
        var heightPx = info?.Height ?? 900;
        var ratio = heightPx == 0 ? 1d : (double)widthPx / heightPx;
        var width = maxWidthEmus;
        var height = (long)(width / Math.Max(ratio, 0.1d));
        return (width, Math.Max(height, 1_500_000L));
    }

    private static bool HasSectionText(DocSection section) =>
        !string.IsNullOrWhiteSpace(section.Content) && !IsPendingPlaceholder(section.Content);

    private static bool HasSectionVisual(DocSection section) =>
        (!string.IsNullOrWhiteSpace(section.DiagramCode) && !IsPendingPlaceholder(section.DiagramCode)) ||
        !string.IsNullOrWhiteSpace(section.ImageBase64);

    private static bool IsContainerSection(DocTemplate template, int order) =>
        (template, order) switch
        {
            (DocTemplate.ModeloSoftware, 8) => true,
            (DocTemplate.DisenoSistema, 6) => true,
            (DocTemplate.DisenoSistema, 9) => true,
            (DocTemplate.DisenoSistema, 13) => true,
            _ => false
        };

    private async Task<bool> TryAppendSectionVisualAsync(MainDocumentPart mainPart, List<OpenXmlElement> elements, DocSection section, string caption)
    {
        if (!string.IsNullOrWhiteSpace(section.DiagramCode) && !IsPendingPlaceholder(section.DiagramCode))
        {
            var (normalizedCode, formatHint) = NormalizeDiagramInput(section.DiagramCode);
            await AppendDiagramCodeAsync(
                mainPart,
                elements,
                normalizedCode,
                formatHint ?? (section.DiagramFormat == DiagramFormat.Mermaid ? "mermaid" : "plantuml"),
                caption);
            return true;
        }

        if (string.IsNullOrWhiteSpace(section.ImageBase64))
            return false;

        var imageBytes = TryDecodeImageBase64(section.ImageBase64);
        if (imageBytes == null || imageBytes.Length == 0)
            return false;

        AppendCaption(elements, "Figura", caption);
        elements.Add(await CreateImageParagraphAsync(mainPart, imageBytes, caption));
        return true;
    }

    private static byte[]? TryDecodeImageBase64(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var base64 = raw.Trim();
        var markerIndex = base64.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
            base64 = base64[(markerIndex + "base64,".Length)..];

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch
        {
            return null;
        }
    }
    private static void AppendMarkdownOrMetadata(List<OpenXmlElement> elements, DocSection section, string fallbackMetadata)
    {
        var content = !string.IsNullOrWhiteSpace(section.Content)
            ? section.Content
            : fallbackMetadata;

        if (string.IsNullOrWhiteSpace(content) || IsPendingPlaceholder(content))
        {
            elements.Add(CreateParagraph("Sin contenido generado.", italic: true, color: "6B7280"));
            return;
        }

        foreach (var element in RenderMarkdown(content))
            elements.Add(element);
    }

    private static IEnumerable<OpenXmlElement> RenderMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            yield break;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var index = 0;

        while (index < lines.Length)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                index++;
                continue;
            }

            if (TryReadMarkdownTable(lines, ref index, out var headers, out var rows))
            {
                yield return CreateTable(headers, rows);
                continue;
            }

            if (TryReadBulletList(lines, ref index, out var bullets))
            {
                foreach (var bullet in bullets)
                    yield return CreateParagraph($"• {bullet}", fontSize: "20", leftIndent: "420");
                continue;
            }

            var paragraphLines = new List<string>();
            while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]))
            {
                paragraphLines.Add(lines[index].Trim());
                index++;
            }

            yield return CreateParagraph(string.Join(" ", paragraphLines), fontSize: "20", justify: JustificationValues.Both);
        }
    }

    private static bool TryReadMarkdownTable(string[] lines, ref int index, out IReadOnlyList<string> headers, out IReadOnlyList<IReadOnlyList<string>> rows)
    {
        headers = Array.Empty<string>();
        rows = Array.Empty<IReadOnlyList<string>>();

        if (index + 1 >= lines.Length)
            return false;

        if (!lines[index].Contains('|') || !Regex.IsMatch(lines[index + 1], @"^\s*\|?[\s:-]+\|[\s|:-]*$"))
            return false;

        headers = ParsePipeRow(lines[index]);
        index += 2;

        var data = new List<IReadOnlyList<string>>();
        while (index < lines.Length && lines[index].Contains('|') && !string.IsNullOrWhiteSpace(lines[index]))
        {
            data.Add(ParsePipeRow(lines[index]));
            index++;
        }

        rows = data;
        return headers.Count > 0;
    }

    private static IReadOnlyList<string> ParsePipeRow(string line) =>
        line.Trim().Trim('|').Split('|').Select(x => x.Trim()).ToList();

    private static bool TryReadBulletList(string[] lines, ref int index, out IReadOnlyList<string> bullets)
    {
        bullets = Array.Empty<string>();
        var list = new List<string>();
        var cursor = index;

        while (cursor < lines.Length)
        {
            var line = lines[cursor].Trim();
            if (string.IsNullOrWhiteSpace(line))
                break;

            var match = Regex.Match(line, @"^(?:[-*•]|\d+[.)])\s+(.*)$");
            if (!match.Success)
                break;

            list.Add(match.Groups[1].Value.Trim());
            cursor++;
        }

        if (list.Count == 0)
            return false;

        index = cursor;
        bullets = list;
        return true;
    }

    private static Table CreateSmartActorTable(string raw)
    {
        if (TryReadStructuredTable(raw, out var headers, out var rows))
        {
            LogDebug($"TABLE ACTORS structured rawLen={raw?.Length ?? 0} headers={headers.Count} rows={rows.Count}");
            return CreateTable(headers, rows);
        }

        var parsedRows = SplitListItems(raw)
            .Select(item =>
            {
                var parts = item.Split(':', 2);
                return parts.Length == 2 ? (IReadOnlyList<string>)[parts[0].Trim(), parts[1].Trim()] : [item, string.Empty];
            })
            .ToList();

        LogDebug($"TABLE ACTORS fallback rawLen={raw?.Length ?? 0} rows={parsedRows.Count}");
        return CreateTable(["Actor", "Responsabilidad"], parsedRows);
    }

    private static Table CreateSmartListTable(string raw, string header)
    {
        if (TryReadStructuredTable(raw, out var headers, out var rows))
        {
            LogDebug($"TABLE LIST header='{header}' structured rawLen={raw?.Length ?? 0} headers={headers.Count} rows={rows.Count}");
            return CreateTable(headers, rows);
        }

        var items = SplitListItems(raw).Select(item => (IReadOnlyList<string>)[item]).ToList();
        LogDebug($"TABLE LIST header='{header}' fallback rawLen={raw?.Length ?? 0} rows={items.Count}");
        return CreateTable([header], items.Count > 0 ? items : [[ "No identificado en contexto" ]]);
    }

    private static Table CreateJsonRowsTable(string rawJson, IReadOnlyList<(string Key, string Header)> columns)
    {
        var rows = ParseDynamicRows(rawJson);
        if (rows.Count == 0)
            return CreateTable(columns.Select(c => c.Header).ToList(), [[ "No identificado en contexto" ]]);

        var dataRows = rows.Select(row => (IReadOnlyList<string>)columns.Select(column => row.GetValueOrDefault(column.Key, string.Empty)).ToList()).ToList();
        return CreateTable(columns.Select(c => c.Header).ToList(), dataRows);
    }

    private static Table CreateJsonRowsTable(
        DocumentSession session,
        string itemsKey,
        IReadOnlyList<(string Key, string Header)> columns)
    {
        var rows = GetDynamicRowsWithFallback(session, itemsKey, columns.Select(column => column.Key).ToArray());
        if (rows.Count == 0)
            return CreateTable(columns.Select(c => c.Header).ToList(), [["No identificado en contexto"]]);

        var dataRows = rows
            .Select(row => (IReadOnlyList<string>)columns.Select(column => row.GetValueOrDefault(column.Key, string.Empty)).ToList())
            .ToList();
        return CreateTable(columns.Select(c => c.Header).ToList(), dataRows);
    }

    private static bool TryReadStructuredTable(string raw, out IReadOnlyList<string> headers, out IReadOnlyList<IReadOnlyList<string>> rows)
    {
        headers = Array.Empty<string>();
        rows = Array.Empty<IReadOnlyList<string>>();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (TryReadJsonObjectTable(raw, out headers, out rows))
            return true;

        var lines = raw.Replace("\r\n", "\n").Split('\n');
        var index = 0;
        return TryReadMarkdownTable(lines, ref index, out headers, out rows);
    }

    private static bool TryReadJsonObjectTable(string raw, out IReadOnlyList<string> headers, out IReadOnlyList<IReadOnlyList<string>> rows)
    {
        headers = Array.Empty<string>();
        rows = Array.Empty<IReadOnlyList<string>>();

        var parsedRows = ParseDynamicRows(raw);
        if (parsedRows.Count == 0)
            return false;

        var orderedHeaders = new List<string>();
        foreach (var row in parsedRows)
        {
            foreach (var key in row.Keys)
            {
                if (!orderedHeaders.Contains(key, StringComparer.OrdinalIgnoreCase))
                    orderedHeaders.Add(key);
            }
        }

        if (orderedHeaders.Count == 0)
            return false;

        headers = orderedHeaders;
        rows = parsedRows
            .Select(row => (IReadOnlyList<string>)orderedHeaders
                .Select(header => row.GetValueOrDefault(header, string.Empty))
                .ToList())
            .ToList();

        return true;
    }

    private static List<string> SplitListItems(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || IsPendingPlaceholder(raw))
            return ["No identificado en contexto"];

        var split = raw.Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => Regex.Replace(item.Trim(), @"^\s*(?:[-*•]|\d+[.)])\s*", string.Empty))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return split.Count > 0 ? split : [raw.Trim()];
    }

    private static List<string> SplitByNewLine(string raw) =>
        raw.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

    private static List<Dictionary<string, string>> GetDynamicRowsWithFallback(DocumentSession session, string itemsKey, params string[] fallbackKeys)
    {
        var rows = ParseDynamicRows(GetMetadataValue(session, itemsKey));
        if (rows.Count > 0)
            return rows;

        if (fallbackKeys.Length == 0)
            return [];

        var fallbackRow = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in fallbackKeys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var value = GetMetadataValue(session, key);
            if (!string.IsNullOrWhiteSpace(value))
                fallbackRow[key] = value;
        }

        return fallbackRow.Count > 0 ? [fallbackRow] : [];
    }

    private static Table CreateKeyValueTable(IEnumerable<(string Label, string Value)> rows)
    {
        var data = rows.Select(row => (IReadOnlyList<string>)[row.Label, string.IsNullOrWhiteSpace(row.Value) ? "—" : row.Value]).ToList();
        return CreateTable(["Campo", "Valor"], data, columnWidths: [2600, 9400]);
    }

    private static Table CreateTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, string fontSize = "20", string headerFill = "D9EAF7", string bordersColor = "B7CDE0", IReadOnlyList<int>? columnWidths = null)
    {
        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableStyle { Val = "TableGrid" },
            new TableWidth { Width = "0", Type = TableWidthUnitValues.Auto },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Color = bordersColor, Size = 8 },
                new BottomBorder { Val = BorderValues.Single, Color = bordersColor, Size = 8 },
                new LeftBorder { Val = BorderValues.Single, Color = bordersColor, Size = 8 },
                new RightBorder { Val = BorderValues.Single, Color = bordersColor, Size = 8 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Color = bordersColor, Size = 6 },
                new InsideVerticalBorder { Val = BorderValues.Single, Color = bordersColor, Size = 6 })));

        if (columnWidths != null && columnWidths.Count > 0)
        {
            var grid = new TableGrid();
            foreach (var width in columnWidths)
                grid.AppendChild(new GridColumn { Width = width.ToString(CultureInfo.InvariantCulture) });
            table.AppendChild(grid);
        }

        var headerRow = new TableRow();
        foreach (var header in headers)
            headerRow.AppendChild(CreateTableCell(header, bold: true, fontSize: fontSize, fill: headerFill));
        table.AppendChild(headerRow);

        foreach (var row in rows)
        {
            var tableRow = new TableRow();
            foreach (var cell in row)
                tableRow.AppendChild(CreateTableCell(string.IsNullOrWhiteSpace(cell) ? "—" : cell, fontSize: fontSize));
            table.AppendChild(tableRow);
        }

        return table;
    }

    private static TableCell CreateTableCell(string text, bool bold = false, string fontSize = "20", string? fill = null)
    {
        var cellProperties = new TableCellProperties(new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });
        if (!string.IsNullOrWhiteSpace(fill))
            cellProperties.AppendChild(new Shading { Fill = fill, Val = ShadingPatternValues.Clear });
        return new TableCell(cellProperties, CreateParagraph(NormalizeDisplayText(text), bold: bold, fontSize: fontSize, spacingAfter: 40));
    }

    private static Paragraph CreateParagraph(string text, bool bold = false, bool italic = false, string fontSize = "22", string? color = null, JustificationValues? justify = null, int spacingBefore = 0, int spacingAfter = 80, string? leftIndent = null)
    {
        text = NormalizeDisplayText(text);
        var runProperties = new RunProperties(new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" }, new FontSize { Val = fontSize });
        if (bold) runProperties.AppendChild(new Bold());
        if (italic) runProperties.AppendChild(new Italic());
        if (!string.IsNullOrWhiteSpace(color)) runProperties.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Color { Val = color });

        var paragraphProperties = new ParagraphProperties(new Justification { Val = justify ?? JustificationValues.Left }, new SpacingBetweenLines { Before = spacingBefore.ToString(CultureInfo.InvariantCulture), After = spacingAfter.ToString(CultureInfo.InvariantCulture) });
        if (!string.IsNullOrWhiteSpace(leftIndent))
            paragraphProperties.AppendChild(new Indentation { Left = leftIndent });

        return new Paragraph(paragraphProperties, new Run(runProperties, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static Paragraph CreateHeadingParagraph(string text, int level)
    {
        var size = level switch { 1 => "28", 2 => "24", _ => "22" };
        var color = level == 1 ? "1F3A5F" : "0F172A";
        var paragraph = CreateParagraph(text, bold: true, fontSize: size, color: color, spacingBefore: level == 1 ? 220 : 160, spacingAfter: 120);
        paragraph.ParagraphProperties ??= new ParagraphProperties();
        paragraph.ParagraphProperties.AppendChild(new OutlineLevel { Val = level - 1 });
        return paragraph;
    }

    private static void AppendHeading(Body body, string text, int level) => body.AppendChild(CreateHeadingParagraph(text, level));

    private static Paragraph CreateFieldParagraph(string instruction) =>
        new(
            new ParagraphProperties(new SpacingBetweenLines { After = "180" }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(new FieldCode(instruction) { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(new Text("")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.End }));

    private static Paragraph CreatePageNumberParagraph() =>
        new(
            new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(new FieldCode(" PAGE ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(new Text("1")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.End }));

    private static Paragraph CreatePageBreakParagraph() => new(new Run(new Break { Type = BreakValues.Page }));

    private static void AppendCaption(List<OpenXmlElement> elements, string kind, string text) =>
        elements.Add(CreateCaptionParagraph(kind, text));

    private static Paragraph CreateCaptionParagraph(string kind, string text)
    {
        kind = NormalizeDisplayText(kind);
        text = NormalizeDisplayText(text);
        var paragraphProperties = new ParagraphProperties(
            new Justification { Val = JustificationValues.Left },
            new SpacingBetweenLines { Before = "100", After = "80" });

        var runProperties = new RunProperties(
            new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" },
            new FontSize { Val = "20" },
            new Bold(),
            new DocumentFormat.OpenXml.Wordprocessing.Color { Val = "475569" });

        return new Paragraph(
            paragraphProperties,
            new Run(runProperties.CloneNode(true), new Text($"{kind} ")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(new FieldCode($" SEQ {kind} \\* ARABIC ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(runProperties.CloneNode(true), new Text("1")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.End }),
            new Run(runProperties.CloneNode(true), new Text($": {text}") { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static void ResetDebugLog(DocumentSession session, string outputPath)
    {
        try
        {
            var lines = new List<string>
            {
                "============================================================",
                $"UTC={DateTime.UtcNow:O}",
                $"OUTPUT={outputPath}",
                $"TITLE={session.Title}",
                $"PROJECT={session.ProjectName}",
                $"TEMPLATE={session.Template}",
                $"SECTIONS={session.Sections.Count}",
                $"METADATA={session.Metadata.Count}"
            };

            foreach (var key in new[]
                     {
                         "TABLA_RESUMEN_PQ",
                         "TABLA_ACTORES_LISTA",
                         "TABLA_CU_LISTADO",
                         "BLOQUE_PQ_ITEMS",
                         "BLOQUE_CU_ITEMS",
                         "BLOQUE_ACT_ITEMS",
                         "BLOQUE_SEQ_ITEMS",
                         "BLOQUE_EST_ITEMS",
                         "IMG_VISTA_LOGICA",
                         "IMG_ACTORES",
                         "IMG_CU_GENERAL"
                     })
            {
                var value = GetMetadataValue(session, key);
                lines.Add($"KEY {key} len={value.Length} placeholder={IsPendingPlaceholder(value)}");
            }

            File.WriteAllLines(DebugLogPath, lines);
        }
        catch
        {
        }
    }

    private static void LogDebug(string message)
    {
        try
        {
            File.AppendAllText(DebugLogPath, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static string NormalizeDisplayText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var normalized = text;
        if (LooksLikeMojibake(normalized))
        {
            try
            {
                var repaired = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(normalized));
                if (CountMojibakeMarkers(repaired) < CountMojibakeMarkers(normalized))
                    normalized = repaired;
            }
            catch
            {
            }
        }

        return normalized
            .Replace("Ã¡", "á", StringComparison.Ordinal)
            .Replace("Ã©", "é", StringComparison.Ordinal)
            .Replace("Ã­", "í", StringComparison.Ordinal)
            .Replace("Ã³", "ó", StringComparison.Ordinal)
            .Replace("Ãº", "ú", StringComparison.Ordinal)
            .Replace("Ã", "Á", StringComparison.Ordinal)
            .Replace("Ã‰", "É", StringComparison.Ordinal)
            .Replace("Ã", "Í", StringComparison.Ordinal)
            .Replace("Ã“", "Ó", StringComparison.Ordinal)
            .Replace("Ãš", "Ú", StringComparison.Ordinal)
            .Replace("Ã±", "ñ", StringComparison.Ordinal)
            .Replace("Ã‘", "Ñ", StringComparison.Ordinal)
            .Replace("Â¿", "¿", StringComparison.Ordinal)
            .Replace("Â¡", "¡", StringComparison.Ordinal)
            .Replace("â€”", "—", StringComparison.Ordinal)
            .Replace("â€“", "–", StringComparison.Ordinal)
            .Replace("â€œ", "“", StringComparison.Ordinal)
            .Replace("â€", "”", StringComparison.Ordinal)
            .Replace("â€˜", "‘", StringComparison.Ordinal)
            .Replace("â€™", "’", StringComparison.Ordinal)
            .Replace("â€¢", "•", StringComparison.Ordinal)
            .Replace("Â", string.Empty, StringComparison.Ordinal);
    }

    private static bool LooksLikeMojibake(string text) =>
        text.Contains('Ã') ||
        text.Contains('Â') ||
        text.Contains("â€", StringComparison.Ordinal) ||
        text.Contains("â€¢", StringComparison.Ordinal);

    private static int CountMojibakeMarkers(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var count = 0;
        foreach (var ch in text)
        {
            if (ch is 'Ã' or 'Â')
                count++;
        }

        count += Regex.Matches(text, "â€|â€¢", RegexOptions.CultureInvariant).Count;
        return count;
    }

    private static string ResolveHeading(string title, ref int chapterNumber, out int level)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        var numbered = Regex.Match(trimmed, @"^(?<num>\d+(?:\.\d+)*)\s+(?<text>.+)$");
        if (numbered.Success)
        {
            level = numbered.Groups["num"].Value.Count(c => c == '.') + 1;
            return trimmed;
        }

        chapterNumber++;
        level = 1;
        return $"{chapterNumber}. {trimmed}";
    }

    private static string GetMetadataValue(DocumentSession session, string key, string fallback = "")
    {
        if (!session.Metadata.TryGetValue(key, out var value))
            return fallback;

        return IsPendingPlaceholder(value) ? fallback : value;
    }

    private static bool IsPendingPlaceholder(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.StartsWith("[") && value.EndsWith("]");

    private static List<Dictionary<string, string>> ParseDynamicRows(string raw)
    {
        var rows = new List<Dictionary<string, string>>();
        if (string.IsNullOrWhiteSpace(raw) || IsPendingPlaceholder(raw))
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
        }

        return rows;
    }

    private static IReadOnlyList<string> GetTableIndexEntries(DocumentSession session)
    {
        var entries = new List<string>();
        foreach (var section in session.Sections.OrderBy(s => s.Order))
        {
            switch (session.Template, section.Order)
            {
                case (DocTemplate.ModeloSoftware, 5):
                case (DocTemplate.ModeloSoftware, 9):
                case (DocTemplate.ModeloSoftware, 12):
                case (DocTemplate.DisenoSistema, 5):
                case (DocTemplate.DisenoSistema, 8):
                case (DocTemplate.DisenoSistema, 14):
                case (DocTemplate.DisenoSistema, 16):
                case (DocTemplate.DisenoSistema, 17):
                case (DocTemplate.DisenoSistema, 18):
                case (DocTemplate.DisenoSistema, 19):
                case (DocTemplate.DisenoSistema, 20):
                    entries.Add(section.Title);
                    break;
                case (DocTemplate.ModeloSoftware, 7):
                    entries.AddRange(ParseDynamicRows(GetMetadataValue(session, "BLOQUE_PQ_ITEMS")).Select(x => $"Especificación de paquete {x.GetValueOrDefault("PQ_ID_NOM", "Paquete")}"));
                    break;
                case (DocTemplate.ModeloSoftware, 13):
                    entries.AddRange(ParseDynamicRows(GetMetadataValue(session, "BLOQUE_CU_ITEMS")).Select(x => $"Especificación de caso de uso {x.GetValueOrDefault("CU_ID", string.Empty)} - {x.GetValueOrDefault("CU_NOM", "Caso de uso")}"));
                    break;
                case (DocTemplate.DisenoSistema, 11):
                    entries.AddRange(ParseDynamicRows(GetMetadataValue(session, "BLOQUE_CLASE_DET_ITEMS")).Select(x => $"Especificación de clase {x.GetValueOrDefault("CLASE_TITULO", "Clase")}"));
                    break;
                case (DocTemplate.DisenoSistema, 15):
                    entries.AddRange(ParseDynamicRows(GetMetadataValue(session, "BLOQUE_DICC_TABLA_ITEMS")).Select(x => $"Descripción de tabla {x.GetValueOrDefault("DICC_TABLA_TITULO", "Tabla")}"));
                    break;
            }
        }

        return entries.Count > 0 ? entries : ["No se generaron tablas."];
    }

    private static IReadOnlyList<string> GetFigureIndexEntries(DocumentSession session)
    {
        var entries = new List<string>();
        foreach (var section in session.Sections.OrderBy(s => s.Order))
        {
            switch (session.Template, section.Order)
            {
                case (DocTemplate.ModeloSoftware, 6):
                case (DocTemplate.ModeloSoftware, 10):
                case (DocTemplate.DisenoSistema, 4):
                case (DocTemplate.DisenoSistema, 7):
                case (DocTemplate.DisenoSistema, 10):
                case (DocTemplate.DisenoSistema, 12):
                    entries.Add(section.Title);
                    break;
                case (DocTemplate.ModeloSoftware, 14):
                    entries.AddRange(ParseDynamicRows(GetMetadataValue(session, "BLOQUE_ACT_ITEMS")).Select(x => $"Diagrama de actividad - {x.GetValueOrDefault("CU_NOM_ACT", "Elemento")}"));
                    break;
                case (DocTemplate.ModeloSoftware, 15):
                    entries.AddRange(ParseDynamicRows(GetMetadataValue(session, "BLOQUE_SEQ_ITEMS")).Select(x => $"Diagrama de secuencia - {x.GetValueOrDefault("CU_NOM_SEQ", "Elemento")}"));
                    break;
                case (DocTemplate.ModeloSoftware, 16):
                    entries.AddRange(ParseDynamicRows(GetMetadataValue(session, "BLOQUE_EST_ITEMS")).Select(x => $"Diagrama de estados - {x.GetValueOrDefault("CU_NOM_EST", "Elemento")}"));
                    break;
            }
        }

        return entries.Count > 0 ? entries : ["No se generaron figuras."];
    }

    private static string DetectDiagramFormat(string diagramCode)
    {
        var code = diagramCode.TrimStart();
        if (code.StartsWith("@startuml", StringComparison.OrdinalIgnoreCase) || code.Contains("@enduml", StringComparison.OrdinalIgnoreCase))
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

    private static (string Code, string? FormatHint) NormalizeDiagramInput(string raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.StartsWith("[") && text.EndsWith("]"))
            return (string.Empty, null);

        var fenced = Regex.Match(
            text,
            "^```(?<lang>[a-zA-Z0-9_-]+)?\\s*\\r?\\n(?<body>[\\s\\S]*?)\\r?\\n```$",
            RegexOptions.Singleline);

        if (!fenced.Success)
            return (text, null);

        var lang = fenced.Groups["lang"].Value.Trim().ToLowerInvariant();
        var body = fenced.Groups["body"].Value.Trim();
        return (body, lang switch
        {
            "mermaid" => "mermaid",
            "plantuml" or "puml" or "uml" or "plant" => "plantuml",
            _ => null
        });
    }

    private static Drawing CreateImageDrawing(string relationshipId, string imageName, long widthEmus, long heightEmus, uint drawingId)
    {
        return new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = widthEmus, Cy = heightEmus },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = drawingId, Name = imageName },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = imageName },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relationshipId, CompressionState = A.BlipCompressionValues.Print },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = widthEmus, Cy = heightEmus }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U,
                EditId = "50D07946"
            });
    }
}



