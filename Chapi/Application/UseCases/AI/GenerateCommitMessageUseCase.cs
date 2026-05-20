using Chapi.Domain.Common;
using Chapi.Infrastructure.AI;
using Microsoft.Extensions.AI;

namespace Chapi.Application.UseCases.AI;

public class GenerateCommitMessageUseCase
{
    private readonly IServiceProvider _serviceProvider;

    public GenerateCommitMessageUseCase(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<Result<string>> ExecuteAsync(string diffContent)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(diffContent))
                return Result<string>.Fail("No hay cambios para generar mensaje");

            var chatClient = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IChatClient>(_serviceProvider);
            var prompt = GetPrompt.GitCommit(diffContent);
            
            // Usar IChatClient de Microsoft.Extensions.AI
            var messages = new[] { new ChatMessage(ChatRole.User, prompt) };
            var response = await chatClient.GetResponseAsync(messages);
            
            // ChatResponse tiene Messages o Choices dependiendo de la version. 
            // En nuestra implementacion de GeminiChatClient usamos ChatResponse con Messages.
            var responseText = response.Messages.FirstOrDefault()?.Text;

            if (string.IsNullOrWhiteSpace(responseText))
                return Result<string>.Fail("No se pudo generar el mensaje de commit");

            return Result<string>.Success(responseText.Trim());
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Error generando mensaje: {ex.Message}");
        }
    }
}
