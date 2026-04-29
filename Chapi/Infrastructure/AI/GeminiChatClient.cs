using Microsoft.Extensions.AI;
using Mscc.GenerativeAI;
using System.Runtime.CompilerServices;
using System.Text;

namespace Chapi.Infrastructure.AI;

public class GeminiChatClient : Microsoft.Extensions.AI.IChatClient
{
    private readonly string _apiKey;
    private static readonly TimeSpan BaseRequestTimeout = TimeSpan.FromSeconds(180);
    private static readonly TimeSpan BaseIdleChunkTimeout = TimeSpan.FromSeconds(35);

    private readonly string[] _models = new[]
    {
        "gemini-3.1-flash-lite-preview",
        "gemini-3.0-flash",
        "gemini-2.5-flash",
    };

    public GeminiChatClient(string apiKey)
    {
        _apiKey = apiKey;
    }

    public Microsoft.Extensions.AI.ChatClientMetadata Metadata => new("GeminiChatClient", new Uri("https://ai.google.dev/"));

    public async Task<ChatResponse> GetResponseAsync(
    IEnumerable<Microsoft.Extensions.AI.ChatMessage> chatMessages,
    ChatOptions? options = null,
    CancellationToken cancellationToken = default)
    {
        var prompt = BuildPrompt(chatMessages);
        var lastError = string.Empty;
        var requestTimeout = GetRequestTimeout(prompt);
        var idleChunkTimeout = GetIdleChunkTimeout(prompt);

        foreach (var modelId in _models)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(requestTimeout);

                var googleAI = new GoogleAI(apiKey: _apiKey);
                var model = googleAI.GenerativeModel(model: modelId);

                var fullResponse = new StringBuilder();

                await foreach (var chunk in model.GenerateContentStream(prompt, cancellationToken: cts.Token))
                {
                    if (!string.IsNullOrEmpty(chunk.Text))
                    {
                        fullResponse.Append(chunk.Text);

                        // Reinicia el timeout de inactividad entre chunks.
                        cts.CancelAfter(idleChunkTimeout);
                    }
                }

                var text = CleanResponse(fullResponse.ToString());

                if (!string.IsNullOrWhiteSpace(text))
                {
                    return new ChatResponse(new[]
                    {
                    new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, text)
                });
                }

                // fallback si vino vacío
                lastError = $"Respuesta vacía ({modelId})";
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;

                lastError = $"Timeout stream ({modelId}, idle {idleChunkTimeout.TotalSeconds:0}s, total {requestTimeout.TotalSeconds:0}s)";

                var fallbackText = await TryGenerateNonStreamingAsync(modelId, prompt, cancellationToken);
                if (!string.IsNullOrWhiteSpace(fallbackText))
                {
                    return new ChatResponse(new[]
                    {
                        new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, fallbackText)
                    });
                }
            }
            catch (Exception ex)
            {
                lastError = HandleError(ex, modelId);

                var fallbackText = await TryGenerateNonStreamingAsync(modelId, prompt, cancellationToken);
                if (!string.IsNullOrWhiteSpace(fallbackText))
                {
                    return new ChatResponse(new[]
                    {
                        new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, fallbackText)
                    });
                }
            }
        }

        throw new Exception($"Fallaron todos los modelos. Último error: {lastError}");
    }
    public async IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> chatMessages,
        Microsoft.Extensions.AI.ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
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
        if (ex.Message.Contains("429") || ex.Message.Contains("Quota"))
        {
            return "⚠️ Se alcanzó el límite de cuota del modelo de IA. Verifica tu plan.";
        }
        return ex.Message;
    }

    private async Task<string?> TryGenerateNonStreamingAsync(
        string modelId,
        string prompt,
        CancellationToken cancellationToken)
    {
        try
        {
            var googleAI = new GoogleAI(apiKey: _apiKey);
            var model = googleAI.GenerativeModel(model: modelId);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(GetRequestTimeout(prompt));

            var response = await model.GenerateContent(prompt, cancellationToken: cts.Token);
            var text = CleanResponse(response.Text);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static TimeSpan GetRequestTimeout(string prompt)
    {
        var length = prompt?.Length ?? 0;
        if (length >= 40000) return TimeSpan.FromSeconds(360);
        if (length >= 20000) return TimeSpan.FromSeconds(300);
        if (length >= 10000) return TimeSpan.FromSeconds(240);
        return BaseRequestTimeout;
    }

    private static TimeSpan GetIdleChunkTimeout(string prompt)
    {
        var length = prompt?.Length ?? 0;
        if (length >= 40000) return TimeSpan.FromSeconds(90);
        if (length >= 20000) return TimeSpan.FromSeconds(60);
        if (length >= 10000) return TimeSpan.FromSeconds(45);
        return BaseIdleChunkTimeout;
    }

    public void Dispose()
    {
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    private string BuildPrompt(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages)
    {
        return string.Join("\n", messages.Select(m => m.Text));
    }


    private string CleanResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        text = text.Trim();

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
