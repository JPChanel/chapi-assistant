using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using Chapi.Application.Interfaces;

namespace Chapi.Infrastructure.Documentation;

/// <summary>
/// Renderiza diagramas Mermaid o PlantUML usando la API de Kroki.io.
/// Usa el endpoint POST con JSON para evitar problemas de encoding en la URL.
/// GET: https://kroki.io/{format}/svg/{base64-zlib}
/// POST: https://kroki.io/{format}/svg  body: { "diagram_source": "...", "output_format": "svg" }
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
            var url = $"{BaseUrl}/{krokiFormat}/svg";

            // Usamos POST con JSON — más simple y no tiene problemas de encoding
            var payload = new { diagram_source = code, output_format = "svg" };
            var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                return $"<p style='color:red'>Error {(int)response.StatusCode}: {error}</p>";
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
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
            var url = $"{BaseUrl}/{krokiFormat}/png";

            var payload = new { diagram_source = code, output_format = "png" };
            var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
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
}
