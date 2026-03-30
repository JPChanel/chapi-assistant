using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Chapi.Application.Interfaces;

namespace Chapi.Infrastructure.Documentation;

/// <summary>
/// Renderiza diagramas Mermaid o PlantUML usando la API de Kroki.
/// Usa endpoint POST (text/plain) segun docs de Kroki.
/// </summary>
public class KrokiDiagramService : IKrokiDiagramService
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://kroki.io";

    public KrokiDiagramService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<string> RenderToSvgAsync(string code, string format, CancellationToken cancellationToken = default)
    {
        try
        {
            var krokiFormat = NormalizeFormat(format);
            var preparedCode = PrepareCodeForKroki(code, krokiFormat);
            var firstAttempt = await PostDiagramAsync(preparedCode, krokiFormat, "svg", cancellationToken);
            if (firstAttempt.Ok && !LooksLikeKrokiErrorSvg(firstAttempt.Body))
                return firstAttempt.Body ?? string.Empty;

            if (string.Equals(krokiFormat, "plantuml", StringComparison.OrdinalIgnoreCase))
            {
                if (TryBuildPackageListDiagram(code, out var packageFallback) &&
                    !string.Equals(packageFallback, preparedCode, StringComparison.Ordinal))
                {
                    var packageAttempt = await PostDiagramAsync(packageFallback, krokiFormat, "svg", cancellationToken);
                    if (packageAttempt.Ok && !LooksLikeKrokiErrorSvg(packageAttempt.Body))
                        return packageAttempt.Body ?? string.Empty;
                }

                var fallback = BuildFallbackUseCaseLikeDiagram(preparedCode);
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    var secondAttempt = await PostDiagramAsync(fallback, krokiFormat, "svg", cancellationToken);
                    if (secondAttempt.Ok)
                        return secondAttempt.Body ?? string.Empty;
                    return $"<p style='color:red'>Error {secondAttempt.StatusCode}: {secondAttempt.Body}</p>";
                }
            }

            return $"<p style='color:red'>Error {firstAttempt.StatusCode}: {firstAttempt.Body}</p>";
        }
        catch (Exception ex)
        {
            return $"<p style='color:red'>Error al conectar con Kroki.io: {ex.Message}</p>";
        }
    }

    public async Task<byte[]?> RenderToPngAsync(string code, string format, CancellationToken cancellationToken = default)
    {
        try
        {
            var krokiFormat = NormalizeFormat(format);
            var preparedCode = PrepareCodeForKroki(code, krokiFormat);
            var firstAttempt = await PostDiagramAsync(preparedCode, krokiFormat, "png", cancellationToken);
            if (firstAttempt.Ok && firstAttempt.Bytes != null)
                return firstAttempt.Bytes;

            if (string.Equals(krokiFormat, "plantuml", StringComparison.OrdinalIgnoreCase))
            {
                var fallback = BuildFallbackUseCaseLikeDiagram(preparedCode);
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    var secondAttempt = await PostDiagramAsync(fallback, krokiFormat, "png", cancellationToken);
                    if (secondAttempt.Ok)
                        return secondAttempt.Bytes;
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeFormat(string format) =>
        format.ToLowerInvariant() switch
        {
            "plantuml" or "plantml" or "plant" => "plantuml",
            "mermaid" => "mermaid",
            _ => format.ToLowerInvariant()
        };

    private static bool LooksLikeKrokiErrorSvg(string? svg)
    {
        if (string.IsNullOrWhiteSpace(svg))
            return true;

        return svg.Contains("Syntax Error", StringComparison.OrdinalIgnoreCase) ||
               svg.Contains("PlantUML", StringComparison.OrdinalIgnoreCase) &&
               svg.Contains("Error", StringComparison.OrdinalIgnoreCase);
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

        // Convierte package en una línea a bloque multilinea válido.
        normalized = Regex.Replace(
            normalized,
            "package\\s+\"(?<pkg>[^\"]+)\"\\s*\\{\\s*\\[(?<node>[^\\]]+)\\]\\s*\\}",
            "package \"${pkg}\" {\n  [${node}]\n}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Si hay flechas al nombre del package, redirigir al nodo interno.
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
            normalized = normalized.Replace($"[{pair.Key}]", $"[{pair.Value}]", StringComparison.Ordinal);

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

    private async Task<(bool Ok, int StatusCode, string? Body, byte[]? Bytes)> PostDiagramAsync(
        string source, string format, string outputFormat, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/{format}/{outputFormat}";
        using var plain = new StringContent(source ?? string.Empty, Encoding.UTF8, "text/plain");
        using var plainRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = plain };
        plainRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            string.Equals(outputFormat, "svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml" : "image/png"));
        var response = await _httpClient.SendAsync(plainRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Fallback JSON (compatibilidad)
            var payload = new { diagram_source = source };
            using var json = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var jsonRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = json };
            jsonRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
                string.Equals(outputFormat, "svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml" : "image/png"));
            response = await _httpClient.SendAsync(jsonRequest, cancellationToken);
        }
        var statusCode = (int)response.StatusCode;

        if (string.Equals(outputFormat, "svg", StringComparison.OrdinalIgnoreCase))
        {
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                return (false, statusCode, err, null);
            }
            var svg = await response.Content.ReadAsStringAsync(cancellationToken);
            return (true, statusCode, svg, null);
        }

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            return (false, statusCode, err, null);
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return (true, statusCode, null, bytes);
    }

    private static string? BuildFallbackUseCaseLikeDiagram(string source)
    {
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

        if (useCases.Count == 0)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine("@startuml");
        sb.AppendLine("left to right direction");
        sb.AppendLine("actor \"Usuario\" as A1");
        sb.AppendLine("actor \"Sistema Externo\" as A2");
        sb.AppendLine("rectangle \"Sistema\" {");
        for (int i = 0; i < useCases.Count; i++)
            sb.AppendLine($"  usecase \"{useCases[i]}\" as UC{i + 1}");
        sb.AppendLine("}");
        sb.AppendLine("A1 --> UC1");
        if (useCases.Count > 1)
            sb.AppendLine($"A2 --> UC{Math.Min(2, useCases.Count)}");
        sb.AppendLine("@enduml");
        return sb.ToString();
    }
}
