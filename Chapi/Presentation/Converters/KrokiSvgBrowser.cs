using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;

namespace Chapi.Presentation.Converters;

/// <summary>
/// Muestra diagramas Kroki como SVG en la UI (preview nítido).
/// Para exportación Word se sigue usando PNG en OpenXmlExportService.
/// </summary>
public class KrokiSvgBrowser : UserControl
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly ConcurrentDictionary<string, string> HtmlCache = new();
    private readonly WebView2 _webView;
    private readonly WebBrowser _legacyBrowser;
    private bool _webViewReady;
    private bool _useLegacyBrowser;
    private string? _pendingHtml;

    public KrokiSvgBrowser()
    {
        _webView = new WebView2();
        _legacyBrowser = new WebBrowser();
        Content = _webView;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        ClipToBounds = true;
        MinWidth = 520;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_webViewReady && !_useLegacyBrowser)
        {
            try
            {
                await _webView.EnsureCoreWebView2Async();
                _webViewReady = true;
            }
            catch
            {
                _useLegacyBrowser = true;
                Content = _legacyBrowser;
                _legacyBrowser.NavigateToString(GetErrorHtml("WebView2 no disponible. Instala Microsoft Edge WebView2 Runtime para ver SVG."));
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(_pendingHtml))
        {
            NavigateHtml(_pendingHtml);
            _pendingHtml = null;
            return;
        }

        _ = RenderAsync(DiagramCode);
    }

    public static readonly DependencyProperty DiagramCodeProperty =
        DependencyProperty.Register(
            nameof(DiagramCode),
            typeof(string),
            typeof(KrokiSvgBrowser),
            new PropertyMetadata(string.Empty, OnDiagramCodeChanged));

    public string DiagramCode
    {
        get => (string)GetValue(DiagramCodeProperty);
        set => SetValue(DiagramCodeProperty, value);
    }

    private static void OnDiagramCodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KrokiSvgBrowser browser) return;
        var raw = e.NewValue as string;
        _ = browser.RenderAsync(raw);
    }

    private async Task RenderAsync(string? raw)
    {
        try
        {
            var (code, hint) = NormalizeInput(raw ?? string.Empty);
            if (string.IsNullOrWhiteSpace(code))
            {
                NavigateHtml(GetEmptyHtml());
                return;
            }

            var format = DetectFormat(code, hint);
            var preparedCode = PrepareCodeForKroki(code, format);
            var cacheKey = $"{format}:{preparedCode}";
            if (HtmlCache.TryGetValue(cacheKey, out var cached))
            {
                NavigateHtml(cached);
                return;
            }

            var svg = await RenderSvgAsync(preparedCode, format);
            if (string.Equals(format, "plantuml", StringComparison.OrdinalIgnoreCase) &&
                (!IsValidSvg(svg) || LooksLikeKrokiErrorSvg(svg)))
            {
                if (TryBuildPackageListDiagram(code, out var packageFallback) &&
                    !string.Equals(packageFallback, preparedCode, StringComparison.Ordinal))
                {
                    svg = await RenderSvgAsync(packageFallback, format);
                }

                if (!IsValidSvg(svg) || LooksLikeKrokiErrorSvg(svg))
                {
                    var fallback = BuildFallbackUseCaseLikeDiagram(preparedCode);
                    if (!string.IsNullOrWhiteSpace(fallback))
                    {
                        svg = await RenderSvgAsync(fallback, format);
                    }
                }
            }

            var html = WrapSvgInHtml(svg);
            if (IsValidSvg(svg) && !LooksLikeKrokiErrorSvg(svg))
                HtmlCache[cacheKey] = html;
            else
                HtmlCache.TryRemove(cacheKey, out _);
            NavigateHtml(html);
        }
        catch
        {
            NavigateHtml(GetErrorHtml("No se pudo renderizar el diagrama SVG."));
        }
    }

    private void NavigateHtml(string html)
    {
        if (_useLegacyBrowser)
        {
            _legacyBrowser.NavigateToString(html);
            return;
        }

        if (_webViewReady && _webView.CoreWebView2 != null)
        {
            _webView.NavigateToString(html);
            return;
        }

        _pendingHtml = html;
    }

    private static async Task<string?> RenderSvgAsync(string source, string format)
    {
        var url = $"https://kroki.io/{format}/svg";

        using var plain = new StringContent(source ?? string.Empty, Encoding.UTF8, "text/plain");
        using var plainRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = plain };
        plainRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/svg+xml"));
        var response = await Http.SendAsync(plainRequest);
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadAsStringAsync();

        // Fallback JSON (compatibilidad)
        var payload = new { diagram_source = source };
        using var json = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var jsonRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = json };
        jsonRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/svg+xml"));
        response = await Http.SendAsync(jsonRequest);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadAsStringAsync();
    }

    private static bool IsValidSvg(string? svg) =>
        !string.IsNullOrWhiteSpace(svg) &&
        svg.Contains("<svg", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeKrokiErrorSvg(string? svg)
    {
        if (string.IsNullOrWhiteSpace(svg))
            return true;

        return svg.Contains("Syntax Error", StringComparison.OrdinalIgnoreCase) ||
               svg.Contains("PlantUML", StringComparison.OrdinalIgnoreCase) && svg.Contains("Error", StringComparison.OrdinalIgnoreCase);
    }

    private static string WrapSvgInHtml(string? svg)
    {
        if (!IsValidSvg(svg))
            return GetErrorHtml("Diagrama no disponible.");

        return "<html>" +
               "<head>" +
               "<meta charset=\"utf-8\" />" +
               "<style>" +
               "html, body { margin:0; padding:0; background:#fff; overflow:auto; }" +
               ".wrap { display:flex; justify-content:center; align-items:flex-start; padding:6px; }" +
               "svg { max-width:100%; height:auto; }" +
               "</style>" +
               "</head>" +
               "<body><div class=\"wrap\">" + svg + "</div></body>" +
               "</html>";
    }

    private static string GetEmptyHtml() =>
        """
        <html><body style="margin:0;background:#fff;"></body></html>
        """;

    private static string GetErrorHtml(string message) =>
        "<html><body style=\"margin:0;padding:10px;background:#fff;color:#b91c1c;font-family:Segoe UI,Arial,sans-serif;font-size:12px;\">" +
        System.Net.WebUtility.HtmlEncode(message) +
        "</body></html>";

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

        normalized = Regex.Replace(
            normalized,
            "package\\s+\"(?<pkg>[^\"]+)\"\\s*\\{\\s*\\[(?<node>[^\\]]+)\\]\\s*\\}",
            "package \"${pkg}\" {\n  [${node}]\n}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

        if (useCases.Count == 0) return null;

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
