using Microsoft.Extensions.AI;
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

    public OpenAiChatClient(string apiKey, string modelId = "gpt-4o")
    {
        _apiKey = apiKey;
        _modelId = modelId;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
    }

    public ChatClientMetadata Metadata => new("OpenAiChatClient", new Uri("https://api.openai.com/"));

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var messages = chatMessages.Select(m => new { role = m.Role.Value.ToLower(), content = m.Text }).ToList();

        var requestBody = new
        {
            model = _modelId,
            messages = messages
        };

        var response = await _httpClient.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", requestBody, cancellationToken);
        response.EnsureSuccessStatusCode();

        var jsonResponse = await response.Content.ReadFromJsonAsync<OpenAiResponse>(cancellationToken: cancellationToken);
        var content = jsonResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;

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
    private class OpenAiResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }
    }

    private class Choice
    {
        [JsonPropertyName("message")]
        public Message? Message { get; set; }
    }

    private class Message
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
