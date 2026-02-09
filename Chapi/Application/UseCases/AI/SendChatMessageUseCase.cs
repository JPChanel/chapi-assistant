using Chapi.Domain.Common;
using Chapi.Infrastructure.AI;

namespace Chapi.Application.UseCases.AI;

public class SendChatMessageUseCase
{
    public async Task<Result<string>> ExecuteAsync(string userMessage, string? context = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return Result<string>.Fail("El mensaje no puede estar vacío");

            var prompt = string.IsNullOrWhiteSpace(context)
                ? userMessage
                : $"{context}\n\nUsuario: {userMessage}";

            var response = await AIClient.SendPromptAsync(prompt);

            if (string.IsNullOrWhiteSpace(response))
                return Result<string>.Fail("No se recibió respuesta del asistente");

            return Result<string>.Success(response);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Error en chat: {ex.Message}");
        }
    }
}
