using Mscc.GenerativeAI;
using System.Runtime.CompilerServices;

namespace Chapi.Infrastructure.AI;

public class GeminiChatClient : Microsoft.Extensions.AI.IChatClient
{
    private readonly string _apiKey;

    private readonly string[] _models = new[]
    {
        "gemini-3.0-flash",
        "gemini-2.5-flash",
        "gemma-3",
    };

    public GeminiChatClient(string apiKey)
    {
        _apiKey = apiKey;
    }

    public Microsoft.Extensions.AI.ChatClientMetadata Metadata => new("GeminiChatClient", new Uri("https://ai.google.dev/"));

    public async Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> chatMessages, Microsoft.Extensions.AI.ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var prompt = BuildPrompt(chatMessages);
        var lastError = string.Empty;

        foreach (var modelId in _models)
        {
            try
            {
                // Timeout por modelo para evitar bloqueos largos (35s máx por intento)
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(35));

                var googleAI = new GoogleAI(apiKey: _apiKey);
                var model = googleAI.GenerativeModel(model: modelId);

                // Usar Streaming para evitar bloqueos en HTTP/2 (Fix connection hanging)
                var fullResponse = new System.Text.StringBuilder();

                // Usar el token con timeout específico para este intento
                await foreach (var chunk in model.GenerateContentStream(prompt, cancellationToken: cts.Token))
                {
                    if (chunk.Text != null)
                        fullResponse.Append(chunk.Text);
                }

                var text = CleanResponse(fullResponse.ToString());

                if (string.IsNullOrWhiteSpace(text)) continue;

                var role = Microsoft.Extensions.AI.ChatRole.Assistant;
                return new Microsoft.Extensions.AI.ChatResponse(new[] { new Microsoft.Extensions.AI.ChatMessage(role, text) });
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                lastError = $"Timeout con modelo {modelId}";
            }
            catch (Exception ex)
            {
                lastError = HandleError(ex, modelId);
            }
        }

        throw new Exception($"Fallaron todos los modelos de Gemini. Último error: {lastError}");
    }

    public async IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> chatMessages,
        Microsoft.Extensions.AI.ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Revertido a modo "Fake Streaming" por estabilidad:
        // Espera toda la respuesta y la devuelve de una vez.
        var response = await GetResponseAsync(chatMessages, options, cancellationToken);

        foreach (var message in response.Messages)
        {
            yield return new Microsoft.Extensions.AI.ChatResponseUpdate
            {
                Role = message.Role,
                Contents = { new Microsoft.Extensions.AI.TextContent(message.Text) },
                ResponseId = response.ResponseId,
                CreatedAt = response.CreatedAt,
                FinishReason = response.FinishReason
            };
        }
    }

    private string HandleError(Exception ex, string modelId)
    {
        if (!ex.Message.Contains("429") && !ex.Message.Contains("Quota"))
        {
            return "⚠️ Se alcanzó el límite de cuota del modelo de IA. Verifica tu plan.";
        }
        return ex.Message;
    }

    public void Dispose()
    {
        // No hay recursos persistentes que liberar aquí
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    private string BuildPrompt(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages)
    {
        // Convertir historial de mensajes a un solo string prompt (formato simple)
        return string.Join("\n", messages.Select(m => m.Text));
    }


    private string CleanResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        text = text.Trim();

        // Limpieza de JSON en markdown
        if (text.StartsWith("```"))
        {
            int start = text.IndexOf("{");
            int end = text.LastIndexOf("}");
            if (start >= 0 && end > start)
                text = text.Substring(start, end - start + 1);
        }
        return text;
    }
}
