using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chapi.Application.Interfaces;
using Chapi.Domain.Documentation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Chapi.Infrastructure.Documentation;

/// <summary>
/// Sintetiza texto de secciones y código de diagramas usando el proveedor IA configurado.
/// Resuelve IChatClient en cada llamada para respetar cambios de proveedor (OpenAI/Gemini/Claude).
/// </summary>
public class GeminiDocSynthesizer : IDocSynthesizerService
{
    private readonly IServiceProvider _serviceProvider;
    private const string DbObjectsStart = "[DB_OBJECTS]";
    private const string DbObjectsEnd = "[/DB_OBJECTS]";
    private static readonly Regex CreateTableRegex = new(@"create\s+table\s+(?:if\s+not\s+exists\s+)?(?<name>[A-Za-z0-9_\.\[\]""`]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreateTableWithBodyRegex = new(@"create\s+table\s+(?:if\s+not\s+exists\s+)?(?<name>[A-Za-z0-9_\.\[\]""`]+)\s*\((?<body>[\s\S]*?)\)\s*;", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreateProcedureRegex = new(@"create\s+(?:or\s+replace\s+)?(?:proc|procedure)\s+(?<name>[A-Za-z0-9_\.\[\]""`]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreateFunctionRegex = new(@"create\s+(?:or\s+replace\s+)?function\s+(?<name>[A-Za-z0-9_\.\[\]""`]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreateViewRegex = new(@"create\s+(?:or\s+replace\s+)?view\s+(?<name>[A-Za-z0-9_\.\[\]""`]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreateIndexRegex = new(@"create\s+(?:unique\s+)?index\s+(?<name>[A-Za-z0-9_\.\[\]""`]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreatePackageRegex = new(@"create\s+(?:or\s+replace\s+)?package\s+(?<name>[A-Za-z0-9_\.\[\]""`]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TablePrimaryKeyRegex = new(@"primary\s+key\s*\((?<cols>[^\)]+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ColumnDefinitionRegex = new(@"^(?<name>[\[\]""`A-Za-z0-9_]+)\s+(?<type>[A-Za-z0-9_]+(?:\s*\([^\)]*\))?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public GeminiDocSynthesizer(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<string> GenerateSectionContentAsync(
        string sectionTitle, string projectContext, CancellationToken cancellationToken = default)
    {
        var prompt = Chapi.Infrastructure.AI.GetPrompt.DocSection(sectionTitle, projectContext);
        var messages = new[] { new ChatMessage(ChatRole.User, prompt) };
        var response = await GetChatClient().GetResponseAsync(messages, cancellationToken: cancellationToken);
        return response.Messages.FirstOrDefault()?.Text ?? string.Empty;
    }

    public async Task<string> GenerateDiagramCodeAsync(
        string sectionTitle, DiagramFormat format, string projectContext, CancellationToken cancellationToken = default)
    {
        var formatName = format == DiagramFormat.Mermaid ? "Mermaid" : "PlantUML";
        var prompt = Chapi.Infrastructure.AI.GetPrompt.DocDiagram(sectionTitle, formatName, projectContext);

        var messages = new[] { new ChatMessage(ChatRole.User, prompt) };
        var response = await GetChatClient().GetResponseAsync(messages, cancellationToken: cancellationToken);
        var raw = response.Messages.FirstOrDefault()?.Text ?? string.Empty;

        return CleanDiagramCode(raw, format);
    }

    public async Task<Dictionary<string, string>> GenerateMetadataAsync(
        IEnumerable<string> keys, string projectContext, string userPrompt, CancellationToken cancellationToken = default)
    {
        var requestedKeys = keys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var jsonKeys = string.Join("\n", requestedKeys.Select(k => $"- {k}"));
        var prompt = Chapi.Infrastructure.AI.GetPrompt.DocMetadata(jsonKeys, projectContext, userPrompt);

        var messages = new[] { new ChatMessage(ChatRole.User, prompt) };
        var response = await GetChatClient().GetResponseAsync(messages, cancellationToken: cancellationToken);
        var raw = response.Messages.FirstOrDefault()?.Text ?? "{}";

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var cleanedJson = Regex.Replace(raw, @"^```(json)?|```$", "", RegexOptions.Multiline).Trim();
            var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(cleanedJson);
            if (payload != null)
            {
                foreach (var (key, value) in payload)
                {
                    dict[key] = value.ValueKind switch
                    {
                        JsonValueKind.String => value.GetString() ?? string.Empty,
                        JsonValueKind.Array or JsonValueKind.Object => value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                        _ => value.ToString()
                    };
                }
            }
        }
        catch (JsonException)
        {
            // Si la IA no devuelve JSON válido, aplicamos fallback con contexto local.
        }

        ApplyDatabaseObjectGuardrails(dict, requestedKeys, projectContext);
        return dict;
    }

    public async Task<string> AnalyzeProjectContextAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(projectPath))
            return $"Proyecto en: {projectPath}";

        var structure = BuildDirectoryTree(projectPath, depth: 3);
        var configFiles = FindConfigFiles(projectPath);
        var dbObjects = ExtractDatabaseObjects(projectPath);
        var dbBlock = BuildDbObjectsBlock(dbObjects);

        var prompt = Chapi.Infrastructure.AI.GetPrompt.DocAnalyzeContext(structure, configFiles);

        var messages = new[] { new ChatMessage(ChatRole.User, prompt) };
        var response = await GetChatClient().GetResponseAsync(messages, cancellationToken: cancellationToken);
        var summary = response.Messages.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(summary))
            summary = structure;

        return $"{summary}{Environment.NewLine}{Environment.NewLine}{dbBlock}";
    }

    private static DbExtractionResult ExtractDatabaseObjects(string projectPath)
    {
        var result = new DbExtractionResult();
        IEnumerable<string> sqlFiles;
        try
        {
            sqlFiles = Directory.EnumerateFiles(projectPath, "*.sql", SearchOption.AllDirectories)
                .Where(path => !IsInsideSkippedDirectory(path))
                .Take(500)
                .ToList();
        }
        catch
        {
            return result;
        }

        foreach (var file in sqlFiles)
        {
            string sql;
            try
            {
                sql = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            ExtractObjectNames(sql, CreateTableRegex, result.Tables);
            ExtractObjectNames(sql, CreatePackageRegex, result.Packages);
            ExtractObjectNames(sql, CreateProcedureRegex, result.Procedures);
            ExtractObjectNames(sql, CreateViewRegex, result.Views);
            ExtractObjectNames(sql, CreateFunctionRegex, result.Functions);
            ExtractObjectNames(sql, CreateIndexRegex, result.Indexes);
            ExtractTableColumns(sql, result.TableColumns);
        }

        return result;
    }

    private static void ExtractObjectNames(string sql, Regex pattern, HashSet<string> target)
    {
        foreach (Match match in pattern.Matches(sql))
        {
            if (!match.Success) continue;
            var name = NormalizeSqlIdentifier(match.Groups["name"].Value);
            if (!string.IsNullOrWhiteSpace(name))
                target.Add(name);
        }
    }

    private static void ExtractTableColumns(string sql, Dictionary<string, List<DbColumnInfo>> target)
    {
        foreach (Match match in CreateTableWithBodyRegex.Matches(sql))
        {
            if (!match.Success) continue;

            var tableName = NormalizeSqlIdentifier(match.Groups["name"].Value);
            if (string.IsNullOrWhiteSpace(tableName)) continue;

            var body = match.Groups["body"].Value;
            var pkColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match pkMatch in TablePrimaryKeyRegex.Matches(body))
            {
                if (!pkMatch.Success) continue;
                var list = pkMatch.Groups["cols"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var rawCol in list)
                {
                    var col = NormalizeSqlIdentifier(rawCol);
                    if (!string.IsNullOrWhiteSpace(col))
                        pkColumns.Add(col);
                }
            }

            var columns = new List<DbColumnInfo>();
            var fragments = body.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var fragment in fragments)
            {
                var line = fragment.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("constraint", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("primary key", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("foreign key", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("unique", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("check", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var colMatch = ColumnDefinitionRegex.Match(line);
                if (!colMatch.Success) continue;

                var colName = NormalizeSqlIdentifier(colMatch.Groups["name"].Value);
                if (string.IsNullOrWhiteSpace(colName)) continue;

                var colType = colMatch.Groups["type"].Value.Trim();
                if (string.IsNullOrWhiteSpace(colType))
                    colType = "N/A";

                var isPk = pkColumns.Contains(colName) || line.Contains("primary key", StringComparison.OrdinalIgnoreCase);
                if (columns.Any(c => string.Equals(c.Name, colName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                columns.Add(new DbColumnInfo(colName, colType, isPk));
            }

            if (columns.Count == 0) continue;

            if (!target.TryGetValue(tableName, out var existing))
            {
                target[tableName] = columns;
                continue;
            }

            foreach (var column in columns)
            {
                if (existing.Any(c => string.Equals(c.Name, column.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                existing.Add(column);
            }
        }
    }

    private static string NormalizeSqlIdentifier(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var normalized = Regex.Replace(raw.Trim(), @"[\[\]""`]", string.Empty);
        normalized = normalized.Trim().TrimEnd(';', ',', ')', '(');
        return normalized;
    }

    private static bool IsInsideSkippedDirectory(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(ShouldSkip);
    }

    private static string BuildDbObjectsBlock(DbExtractionResult db)
    {
        static string JoinOrNone(IEnumerable<string> values)
        {
            var items = values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return items.Count == 0 ? "No identificado en contexto" : string.Join("; ", items);
        }

        var sb = new StringBuilder();
        sb.AppendLine(DbObjectsStart);
        sb.AppendLine($"TABLES: {JoinOrNone(db.Tables)}");
        sb.AppendLine($"PACKAGES: {JoinOrNone(db.Packages)}");
        sb.AppendLine($"PROCEDURES: {JoinOrNone(db.Procedures)}");
        sb.AppendLine($"VIEWS: {JoinOrNone(db.Views)}");
        sb.AppendLine($"FUNCTIONS: {JoinOrNone(db.Functions)}");
        sb.AppendLine($"INDEXES: {JoinOrNone(db.Indexes)}");

        if (db.TableColumns.Count > 0)
        {
            sb.AppendLine("TABLE_COLUMNS:");
            foreach (var entry in db.TableColumns.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                var columns = entry.Value
                    .Select(c => $"{c.Name}:{c.Type}:{(c.IsPrimaryKey ? "PK" : "NO-PK")}");
                sb.AppendLine($"{entry.Key} => {string.Join(" | ", columns)}");
            }
        }

        sb.Append(DbObjectsEnd);
        return sb.ToString();
    }

    private static void ApplyDatabaseObjectGuardrails(
        Dictionary<string, string> metadata,
        IReadOnlyCollection<string> requestedKeys,
        string projectContext)
    {
        var db = ParseDbObjectsBlock(projectContext);
        if (db == null) return;

        void SetFromList(string key, IEnumerable<string> values)
        {
            if (!requestedKeys.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)))
                return;

            var clean = values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();

            metadata[key] = clean.Count == 0
                ? "No identificado en contexto"
                : string.Join("; ", clean);
        }

        SetFromList("TABLA_OBJ_PQ", db.Packages);
        SetFromList("TABLA_OBJ_PROC", db.Procedures);
        SetFromList("TABLA_OBJ_VISTAS", db.Views);
        SetFromList("TABLA_OBJ_FUNC", db.Functions);
        SetFromList("TABLA_OBJ_IDX", db.Indexes);
        SetFromList("TABLA_DICC_RESUMEN", db.Tables);

        if (requestedKeys.Any(k => string.Equals(k, "BLOQUE_DICC_TABLA_ITEMS", StringComparison.OrdinalIgnoreCase)))
        {
            metadata["BLOQUE_DICC_TABLA_ITEMS"] = BuildDictionaryItemsJson(db);
        }

        if (!metadata.ContainsKey("DICC_TABLA_TITULO") && db.Tables.Count > 0 &&
            requestedKeys.Any(k => string.Equals(k, "DICC_TABLA_TITULO", StringComparison.OrdinalIgnoreCase)))
        {
            metadata["DICC_TABLA_TITULO"] = db.Tables.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).First();
        }
    }

    private static string BuildDictionaryItemsJson(DbExtractionResult db)
    {
        var orderedTables = db.Tables
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new List<Dictionary<string, string>>();
        foreach (var table in orderedTables)
        {
            var columns = db.TableColumns.TryGetValue(table, out var list) ? list : new List<DbColumnInfo>();
            if (columns.Count == 0)
            {
                rows.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["DICC_TABLA_TITULO"] = table,
                    ["COL_NOM"] = "No identificado en contexto",
                    ["COL_TIPO"] = "No identificado en contexto",
                    ["COL_PK"] = "No identificado en contexto",
                    ["COL_DESC"] = "No identificado en contexto"
                });
                continue;
            }

            rows.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DICC_TABLA_TITULO"] = table,
                ["COL_NOM"] = string.Join('\n', columns.Select(c => c.Name)),
                ["COL_TIPO"] = string.Join('\n', columns.Select(c => c.Type)),
                ["COL_PK"] = string.Join('\n', columns.Select(c => c.IsPrimaryKey ? "SI" : "NO")),
                ["COL_DESC"] = string.Join('\n', columns.Select(c => $"Campo {c.Name} de {table}"))
            });
        }

        return rows.Count == 0
            ? "[]"
            : JsonSerializer.Serialize(rows);
    }

    private static DbExtractionResult? ParseDbObjectsBlock(string projectContext)
    {
        if (string.IsNullOrWhiteSpace(projectContext))
            return null;

        var blockMatch = Regex.Match(
            projectContext,
            @"\[DB_OBJECTS\](?<body>[\s\S]*?)\[/DB_OBJECTS\]",
            RegexOptions.IgnoreCase);
        if (!blockMatch.Success)
            return null;

        var result = new DbExtractionResult();
        var body = blockMatch.Groups["body"].Value;
        var lines = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("TABLES:", StringComparison.OrdinalIgnoreCase))
            {
                ParseList(line["TABLES:".Length..], result.Tables);
                continue;
            }

            if (line.StartsWith("PACKAGES:", StringComparison.OrdinalIgnoreCase))
            {
                ParseList(line["PACKAGES:".Length..], result.Packages);
                continue;
            }

            if (line.StartsWith("PROCEDURES:", StringComparison.OrdinalIgnoreCase))
            {
                ParseList(line["PROCEDURES:".Length..], result.Procedures);
                continue;
            }

            if (line.StartsWith("VIEWS:", StringComparison.OrdinalIgnoreCase))
            {
                ParseList(line["VIEWS:".Length..], result.Views);
                continue;
            }

            if (line.StartsWith("FUNCTIONS:", StringComparison.OrdinalIgnoreCase))
            {
                ParseList(line["FUNCTIONS:".Length..], result.Functions);
                continue;
            }

            if (line.StartsWith("INDEXES:", StringComparison.OrdinalIgnoreCase))
            {
                ParseList(line["INDEXES:".Length..], result.Indexes);
                continue;
            }

            var split = line.Split("=>", 2, StringSplitOptions.TrimEntries);
            if (split.Length != 2) continue;

            var tableName = NormalizeSqlIdentifier(split[0]);
            if (string.IsNullOrWhiteSpace(tableName)) continue;

            var columns = new List<DbColumnInfo>();
            foreach (var token in split[1].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = token.Split(':', StringSplitOptions.TrimEntries);
                if (parts.Length < 2) continue;

                var columnName = NormalizeSqlIdentifier(parts[0]);
                var type = parts[1];
                var isPk = token.EndsWith(":PK", StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrWhiteSpace(columnName)) continue;

                columns.Add(new DbColumnInfo(columnName, type, isPk));
            }

            if (columns.Count > 0)
                result.TableColumns[tableName] = columns;
        }

        return result;
    }

    private static void ParseList(string value, HashSet<string> target)
    {
        var raw = value.Trim();
        if (string.IsNullOrWhiteSpace(raw) ||
            raw.Equals("No identificado en contexto", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var tokens = raw.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            var clean = NormalizeSqlIdentifier(token);
            if (!string.IsNullOrWhiteSpace(clean))
                target.Add(clean);
        }
    }

    private sealed class DbExtractionResult
    {
        public HashSet<string> Tables { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Packages { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Procedures { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Views { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Functions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Indexes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<DbColumnInfo>> TableColumns { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class DbColumnInfo
    {
        public DbColumnInfo(string name, string type, bool isPrimaryKey)
        {
            Name = name;
            Type = type;
            IsPrimaryKey = isPrimaryKey;
        }

        public string Name { get; }
        public string Type { get; }
        public bool IsPrimaryKey { get; }
    }

    private IChatClient GetChatClient() =>
        _serviceProvider.GetRequiredService<IChatClient>();

    private static string BuildDirectoryTree(string path, int depth, string indent = "")
    {
        if (depth == 0) return string.Empty;
        var sb = new System.Text.StringBuilder();
        try
        {
            foreach (var dir in Directory.GetDirectories(path)
                .Where(d => !ShouldSkip(Path.GetFileName(d)!))
                .Take(15))
            {
                sb.AppendLine($"{indent}📁 {Path.GetFileName(dir)}");
                sb.Append(BuildDirectoryTree(dir, depth - 1, indent + "  "));
            }
            foreach (var file in Directory.GetFiles(path).Take(10))
            {
                sb.AppendLine($"{indent}📄 {Path.GetFileName(file)}");
            }
        }
        catch { }
        return sb.ToString();
    }

    private static bool ShouldSkip(string name) =>
        name is "bin" or "obj" or "node_modules" or ".git" or ".vs" or "dist" or "build";

    private static string FindConfigFiles(string path)
    {
        var patterns = new[] { "*.csproj", "package.json", "*.sln", "appsettings.json", "*.py", "requirements.txt", "Dockerfile" };
        var found = new List<string>();
        foreach (var pattern in patterns)
        {
            try
            {
                found.AddRange(Directory.GetFiles(path, pattern, SearchOption.AllDirectories)
                    .Take(3)
                    .Select(Path.GetFileName)
                    .Where(x => !string.IsNullOrWhiteSpace(x))!);
            }
            catch { }
        }
        return found.Count > 0 ? string.Join(", ", found.Distinct()) : "No se encontraron archivos de configuración";
    }

    private static string CleanDiagramCode(string raw, DiagramFormat format)
    {
        raw = raw.Trim();
        if (raw.StartsWith("```", StringComparison.Ordinal))
        {
            var lines = raw.Split('\n').ToList();
            lines.RemoveAt(0);
            if (lines.LastOrDefault()?.TrimStart().StartsWith("```", StringComparison.Ordinal) == true)
                lines.RemoveAt(lines.Count - 1);
            raw = string.Join('\n', lines).Trim();
        }
        return raw;
    }
}

