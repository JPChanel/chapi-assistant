using Chapi.Domain.Common;
using Microsoft.Extensions.AI;

namespace Chapi.Application.UseCases.AI;

public class SendChatMessageUseCase
{
    private readonly IChatClient _chatClient;

    public SendChatMessageUseCase(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<Result<string>> ExecuteAsync(string userMessage, string? context = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return Result<string>.Fail("El mensaje no puede estar vacío");

            var prompt = string.IsNullOrWhiteSpace(context)
                ? userMessage
                : $"{context}\n\nUsuario: {userMessage}";

            var messages = new[] { new ChatMessage(ChatRole.User, prompt) };
            var response = await _chatClient.GetResponseAsync(messages);
            var responseText = response.Messages.FirstOrDefault()?.Text;

            if (string.IsNullOrWhiteSpace(responseText))
                return Result<string>.Fail("No se recibió respuesta del asistente");

            return Result<string>.Success(responseText);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Error en chat: {ex.Message}");
        }
    }
}
