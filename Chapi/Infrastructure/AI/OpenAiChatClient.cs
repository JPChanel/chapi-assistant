using Microsoft.Extensions.AI;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Chapi.Infrastructure.AI;

public class OpenAiChatClient : IChatClient
{
    private readonly string _apiKey;
    private readonly HttpClient _httpClient;
    private readonly string _modelId;

    public OpenAiChatClient(string apiKey, string modelId = "gpt-5.4")
    {
        _apiKey = apiKey;
        _modelId = modelId;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
    }

    public ChatClientMetadata Metadata => new("OpenAiChatClient", new Uri("https://api.openai.com/"));

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messages = chatMessages
            .Select(m => new
            {
                role = NormalizeRole(m.Role.Value),
                content = m.Text
            })
            .ToList();

        var requestBody = new
        {
            model = _modelId,
            input = messages
        };

        using var response = await _httpClient.PostAsJsonAsync(
            "https://api.openai.com/v1/responses",
            requestBody,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw BuildOpenAiException(response.StatusCode, errorBody);
        }

        var jsonResponse = await response.Content.ReadFromJsonAsync<OpenAiResponsesApiResponse>(cancellationToken: cancellationToken);
        var content = jsonResponse?.OutputText
            ?? jsonResponse?.Output?.SelectMany(x => x.Content ?? new List<OutputContentBlock>())
                .FirstOrDefault(x => x.Type == "output_text")?.Text
            ?? string.Empty;

        return new ChatResponse(new[] { new ChatMessage(ChatRole.Assistant, content) });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(chatMessages, options, cancellationToken);
        foreach (var message in response.Messages)
        {
            yield return new ChatResponseUpdate
            {
                Role = message.Role,
                Contents = { new TextContent(message.Text) }
            };
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    private static string NormalizeRole(string role)
    {
        if (role.Equals("system", StringComparison.OrdinalIgnoreCase))
            return "developer";
        return role.ToLowerInvariant();
    }

    private static Exception BuildOpenAiException(HttpStatusCode statusCode, string errorBody)
    {
        var status = (int)statusCode;
        if (status == 429 && errorBody.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase))
        {
            return new InvalidOperationException(
                "OpenAI devolvió 429 insufficient_quota. Revisa billing/créditos del proyecto API en platform.openai.com.");
        }

        return new HttpRequestException($"OpenAI API error {status}: {errorBody}", null, statusCode);
    }

    private class OpenAiResponsesApiResponse
    {
        [JsonPropertyName("output_text")]
        public string? OutputText { get; set; }

        [JsonPropertyName("output")]
        public List<OutputItem>? Output { get; set; }
    }

    private class OutputItem
    {
        [JsonPropertyName("content")]
        public List<OutputContentBlock>? Content { get; set; }
    }

    private class OutputContentBlock
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
