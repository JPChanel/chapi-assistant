using Microsoft.Extensions.AI;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Chapi.Infrastructure.AI;

public class ClaudeChatClient : IChatClient
{
    private readonly string _apiKey;
    private readonly HttpClient _httpClient;
    private readonly string _modelId;

    public ClaudeChatClient(string apiKey, string modelId = "claude-3-opus-20240229")
    {
        _apiKey = apiKey;
        _modelId = modelId;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public ChatClientMetadata Metadata => new("ClaudeChatClient", new Uri("https://api.anthropic.com/"));

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var messages = chatMessages.Select(m => new { role = m.Role.Value.ToLower(), content = m.Text }).ToList();

        var requestBody = new
        {
            model = _modelId,
            messages = messages,
            max_tokens = 1024
        };

        var response = await _httpClient.PostAsJsonAsync("https://api.anthropic.com/v1/messages", requestBody, cancellationToken);
        response.EnsureSuccessStatusCode();

        var jsonResponse = await response.Content.ReadFromJsonAsync<ClaudeResponse>(cancellationToken: cancellationToken);
        var content = jsonResponse?.Content?.FirstOrDefault()?.Text ?? string.Empty;

        return new ChatResponse(new[] { new ChatMessage(ChatRole.Assistant, content) });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Implementación simplificada sin streaming real por ahora, reutiliza la síncrona
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

    // Clases auxiliares para deserialización
    private class ClaudeResponse
    {
        [JsonPropertyName("content")]
        public List<ContentBlock>? Content { get; set; }
    }

    private class ContentBlock
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
