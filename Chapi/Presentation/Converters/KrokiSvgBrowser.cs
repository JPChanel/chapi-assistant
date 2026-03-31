using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Chapi.Presentation.Converters;

/// <summary>
/// PNG-only Kroki renderer for stable behavior in WPF.
/// </summary>
public class KrokiSvgBrowser : UserControl
{
    private const int PngScale = 2;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly ConcurrentDictionary<string, byte[]> PngCache = new();

    private readonly Grid _root;
    private readonly Image _image;
    private readonly TextBlock _message;
    private int _renderVersion;

    public KrokiSvgBrowser()
    {
        _image = new Image
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

        _message = new TextBlock
        {
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C")),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10),
            Visibility = Visibility.Collapsed
        };

        _root = new Grid
        {
            Background = Brushes.White,
            ClipToBounds = true
        };
        _root.Children.Add(_image);
        _root.Children.Add(_message);
        Panel.SetZIndex(_message, 1);

        Content = _root;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        Loaded += (_, _) => _ = RenderAsync(DiagramCode);
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
        _ = browser.RenderAsync(e.NewValue as string);
    }

    private async Task RenderAsync(string? raw)
    {
        var currentVersion = Interlocked.Increment(ref _renderVersion);

        try
        {
            var (code, hint) = NormalizeInput(raw ?? string.Empty);
            if (string.IsNullOrWhiteSpace(code))
            {
                ClearDiagram();
                return;
            }

            var format = DetectFormat(code, hint);
            var preparedCode = PrepareCodeForKroki(code, format);
            var cacheKey = $"{format}:{preparedCode}:png{PngScale}";
            if (PngCache.TryGetValue(cacheKey, out var cached))
            {
                SetPng(cached, currentVersion);
                return;
            }

            byte[]? png = null;
            foreach (var candidate in BuildCandidateCodes(code, preparedCode, format))
            {
                var svg = await RenderSvgAsync(candidate, format);
                if (!IsValidSvg(svg) || LooksLikeKrokiErrorSvg(svg))
                    continue;

                png = await RenderPngAsync(candidate, format);
                if (png is { Length: > 0 })
                    break;
            }

            if (png is not { Length: > 0 })
            {
                ShowMessage("No se pudo renderizar el diagrama.");
                return;
            }

            PngCache[cacheKey] = png;
            SetPng(png, currentVersion);
        }
        catch
        {
            ShowMessage("No se pudo renderizar el diagrama.");
        }
    }

    private static List<string> BuildCandidateCodes(string originalCode, string preparedCode, string format)
    {
        var candidates = new List<string> { preparedCode };

        if (!string.Equals(format, "plantuml", StringComparison.OrdinalIgnoreCase))
            return candidates;

        if (TryBuildPackageListDiagram(originalCode, out var packageFallback) &&
            !string.Equals(packageFallback, preparedCode, StringComparison.Ordinal))
        {
            candidates.Add(packageFallback);
        }

        var useCaseFallback = BuildFallbackUseCaseLikeDiagram(preparedCode);
        if (!string.IsNullOrWhiteSpace(useCaseFallback) &&
            !string.Equals(useCaseFallback, preparedCode, StringComparison.Ordinal))
        {
            candidates.Add(useCaseFallback);
        }

        return candidates.Distinct(StringComparer.Ordinal).ToList();
    }

    private void ClearDiagram()
    {
        _image.Source = null;
        _message.Text = string.Empty;
        _message.Visibility = Visibility.Collapsed;
    }

    private void SetPng(byte[] png, int version)
    {
        if (version != _renderVersion)
            return;

        try
        {
            _image.Source = CreateBitmapSource(png);
            _message.Text = string.Empty;
            _message.Visibility = Visibility.Collapsed;
        }
        catch
        {
            ShowMessage("No se pudo renderizar el diagrama.");
        }
    }

    private void ShowMessage(string text)
    {
        _image.Source = null;
        _message.Text = text;
        _message.Visibility = Visibility.Visible;
    }

    private static BitmapSource CreateBitmapSource(byte[] pngBytes)
    {
        using var stream = new MemoryStream(pngBytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static async Task<byte[]?> RenderPngAsync(string source, string format)
    {
        var url = $"https://kroki.io/{format}/png?scale={PngScale}";

        using var plain = new StringContent(source ?? string.Empty, Encoding.UTF8, "text/plain");
        using var plainRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = plain };
        plainRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
        var response = await Http.SendAsync(plainRequest);
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadAsByteArrayAsync();

        var payload = new { diagram_source = source };
        using var json = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var jsonRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = json };
        jsonRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
        response = await Http.SendAsync(jsonRequest);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadAsByteArrayAsync();
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
            .Replace("\u201C", "\"", StringComparison.Ordinal)
            .Replace("\u201D", "\"", StringComparison.Ordinal)
            .Replace("\u2018", "'", StringComparison.Ordinal)
            .Replace("\u2019", "'", StringComparison.Ordinal)
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
        sb.AppendLine("rectangle \"Vista logica\" {");
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
