using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Chapi.Presentation.Converters;

public class KrokiCodeToImageConverter : IValueConverter
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly ConcurrentDictionary<string, BitmapImage?> Cache = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string raw || string.IsNullOrWhiteSpace(raw))
            return null;

        var (code, hint) = NormalizeInput(raw);
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var format = DetectFormat(code, hint);
        var preparedCode = PrepareCodeForKroki(code, format);
        var cacheKey = $"{format}:{preparedCode}";
        if (Cache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var bytes = RenderPng(preparedCode, format);
            if ((bytes == null || bytes.Length == 0) && string.Equals(format, "plantuml", StringComparison.OrdinalIgnoreCase))
            {
                var fallback = BuildFallbackUseCaseLikeDiagram(preparedCode);
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    bytes = RenderPng(fallback, format);
                }
            }
            if (bytes == null || bytes.Length == 0)
            {
                Cache[cacheKey] = null;
                return null;
            }

            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            Cache[cacheKey] = image;
            return image;
        }
        catch
        {
            Debug.WriteLine("[KrokiCodeToImageConverter] Render error.");
            Cache[cacheKey] = null;
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static (string Code, string? FormatHint) NormalizeInput(string raw)
    {
        var text = raw.Trim();
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

    private static string DetectFormat(string code, string? hint)
    {
        if (!string.IsNullOrWhiteSpace(hint))
            return hint;

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

    private static byte[]? RenderPng(string source, string format)
    {
        using var plain = new StringContent(source ?? string.Empty, Encoding.UTF8, "text/plain");
        using var plainRequest = new HttpRequestMessage(HttpMethod.Post, $"https://kroki.io/{format}/png") { Content = plain };
        plainRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
        using var plainResponse = Http.Send(plainRequest);
        if (plainResponse.IsSuccessStatusCode)
            return plainResponse.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();

        // Fallback JSON (compatibilidad)
        var payload = System.Text.Json.JsonSerializer.Serialize(new { diagram_source = source });
        using var json = new StringContent(payload, Encoding.UTF8, "application/json");
        using var jsonRequest = new HttpRequestMessage(HttpMethod.Post, $"https://kroki.io/{format}/png") { Content = json };
        jsonRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
        using var response = Http.Send(jsonRequest);
        if (!response.IsSuccessStatusCode)
        {
            var err = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            Debug.WriteLine($"[KrokiCodeToImageConverter] {response.StatusCode}: {err}");
            return null;
        }

        return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
    }

    private static string PrepareCodeForKroki(string code, string format)
    {
        var normalized = (code ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('“', '"')
            .Replace('”', '"')
            .Replace('‘', '\'')
            .Replace('’', '\'')
            .Trim();

        if (normalized.Contains("\\n", StringComparison.Ordinal) && !normalized.Contains('\n'))
            normalized = normalized.Replace("\\n", "\n", StringComparison.Ordinal);

        if (!string.Equals(format, "plantuml", StringComparison.OrdinalIgnoreCase))
            return normalized;

        if (TryBuildPackageListDiagram(normalized, out var packageDiagram))
            return packageDiagram;

        if (!normalized.Contains("@startuml", StringComparison.OrdinalIgnoreCase))
            normalized = "@startuml\n" + normalized;
        if (!normalized.Contains("@enduml", StringComparison.OrdinalIgnoreCase))
            normalized += "\n@enduml";

        // Kroki/PlantUML suele fallar con package en una sola linea.
        normalized = Regex.Replace(
            normalized,
            "package\\s+\"(?<pkg>[^\"]+)\"\\s*\\{\\s*\\[(?<node>[^\\]]+)\\]\\s*\\}",
            "package \"${pkg}\" {\n  [${node}]\n}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Si las flechas apuntan al nombre del package, redirigir al nodo interno.
        var packageToNode = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     normalized,
                     "package\\s+\"(?<pkg>[^\"]+)\"\\s*\\{\\s*\\r?\\n\\s*\\[(?<node>[^\\]]+)\\]",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var pkg = match.Groups["pkg"].Value.Trim();
            var node = match.Groups["node"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(pkg) && !string.IsNullOrWhiteSpace(node) &&
                !string.Equals(pkg, node, StringComparison.Ordinal))
            {
                packageToNode[pkg] = node;
            }
        }

        foreach (var pair in packageToNode)
        {
            normalized = normalized.Replace($"[{pair.Key}]", $"[{pair.Value}]", StringComparison.Ordinal);
        }

        return normalized;
    }

    private static bool TryBuildPackageListDiagram(string source, out string diagram)
    {
        diagram = string.Empty;
        if (string.IsNullOrWhiteSpace(source))
            return false;

        if (LooksLikeDiagramDsl(source))
            return false;

        var candidates = ExtractPackageCandidates(source);
        if (candidates.Count < 2)
            return false;

        var sb = new StringBuilder();
        sb.AppendLine("@startuml");
        sb.AppendLine("left to right direction");
        sb.AppendLine("skinparam packageStyle rectangle");
        sb.AppendLine("skinparam shadowing false");
        sb.AppendLine("rectangle \"Vista lógica\" {");
        for (int i = 0; i < candidates.Count; i++)
        {
            var label = candidates[i].Replace("\"", "'");
            sb.AppendLine($"  [{label}] as P{i + 1}");
        }
        sb.AppendLine("}");
        for (int i = 0; i < candidates.Count - 1; i++)
            sb.AppendLine($"P{i + 1} --> P{i + 2}");
        sb.AppendLine("@enduml");

        diagram = sb.ToString();
        return true;
    }

    private static bool LooksLikeDiagramDsl(string source)
    {
        var text = source.ToLowerInvariant();
        if (text.Contains("@startuml", StringComparison.Ordinal) || text.Contains("@enduml", StringComparison.Ordinal))
            return true;
        if (text.StartsWith("graph ", StringComparison.Ordinal) ||
            text.StartsWith("flowchart ", StringComparison.Ordinal) ||
            text.StartsWith("sequencediagram", StringComparison.Ordinal))
            return true;

        return Regex.IsMatch(
            source,
            "(-->|->|<--|class\\s+|interface\\s+|actor\\s+|usecase\\s+|participant\\s+|state\\s+|component\\s+|entity\\s+|package\\s+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static List<string> ExtractPackageCandidates(string source)
    {
        var tokens = Regex.Split(source, "[,;\\n\\r|]+")
            .Select(t => Regex.Replace(t, "^\\s*[-*•\\d\\.)\\(]+\\s*", string.Empty))
            .Select(t => Regex.Replace(t, "\\s+", " ").Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        if (tokens.Count >= 2)
            return tokens;

        var repeatedPrefix = Regex.Matches(source, "([A-Za-z_][A-Za-z0-9_]*\\.)")
            .Select(m => m.Groups[1].Value)
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault(g => g.Count() > 1);
        if (repeatedPrefix != null)
        {
            var prefix = Regex.Escape(repeatedPrefix.Key);
            tokens = Regex.Split(source, $"(?={prefix})")
                .Select(t => Regex.Replace(t, "\\s+", " ").Trim().Trim(',', ';'))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
            if (tokens.Count >= 2)
                return tokens;
        }

        var rootMatches = Regex.Matches(source, "([A-Za-z_][A-Za-z0-9_]*)\\.")
            .Select(m => m.Groups[1].Value)
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        if (rootMatches != null && rootMatches.Count() > 1 && !string.IsNullOrWhiteSpace(rootMatches.Key))
        {
            var root = Regex.Escape(rootMatches.Key + ".");
            tokens = Regex.Split(source, $"(?={root})")
                .Select(t => Regex.Replace(t, "\\s+", " ").Trim().Trim(',', ';'))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
            if (tokens.Count >= 2)
                return tokens;
        }

        tokens = Regex.Matches(source, "[A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z_][A-Za-z0-9_]*)+")
            .Select(m => m.Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        return tokens;
    }

    private static string? BuildFallbackUseCaseLikeDiagram(string source)
    {
        // Fallback para evitar contenedores vacíos cuando la IA devolvió PlantUML inválido.
        // Construye un diagrama simple y legible con actores + casos si se pueden detectar.
        var useCases = Regex.Matches(source, "\"(CU\\d{3}:[^\"]+)\"", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        if (useCases.Count == 0)
        {
            useCases = Regex.Matches(source, "\\[(?<n>[A-Za-z0-9 _:-]{3,40})\\]")
                .Select(m => m.Groups["n"].Value.Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();
        }

        if (useCases.Count == 0) return null;

        var sb = new StringBuilder();
        sb.AppendLine("@startuml");
        sb.AppendLine("left to right direction");
        sb.AppendLine("actor \"Usuario\" as A1");
        sb.AppendLine("actor \"Sistema Externo\" as A2");
        sb.AppendLine("rectangle \"Sistema\" {");
        for (int i = 0; i < useCases.Count; i++)
        {
            sb.AppendLine($"  usecase \"{useCases[i]}\" as UC{i + 1}");
        }
        sb.AppendLine("}");
        sb.AppendLine("A1 --> UC1");
        if (useCases.Count > 1)
            sb.AppendLine($"A2 --> UC{Math.Min(2, useCases.Count)}");
        sb.AppendLine("@enduml");
        return sb.ToString();
    }
}
