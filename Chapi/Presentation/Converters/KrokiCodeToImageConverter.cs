using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
        var cacheKey = $"{format}:{code}";
        if (Cache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var payload = JsonSerializer.Serialize(new { diagram_source = code, output_format = "png" });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://kroki.io/{format}/png")
            {
                Content = content
            };
            using var response = Http.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                var err = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Debug.WriteLine($"[KrokiCodeToImageConverter] {response.StatusCode}: {err}");
                Cache[cacheKey] = null;
                return null;
            }

            var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            if (bytes.Length == 0)
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
}
