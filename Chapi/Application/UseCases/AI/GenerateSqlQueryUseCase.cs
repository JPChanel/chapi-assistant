using Chapi.Domain.Common;
using Chapi.Infrastructure.AI;
using Microsoft.Extensions.AI;

namespace Chapi.Application.UseCases.AI;

public class GenerateSqlQueryUseCase
{
    private readonly IServiceProvider _serviceProvider;

    public GenerateSqlQueryUseCase(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<Result<string>> ExecuteAsync(string description, string? schema = null, string? netParams = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(description))
                return Result<string>.Fail("La descripción de la consulta no puede estar vacía");

            var chatClient = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IChatClient>(_serviceProvider);
            var prompt = GetPrompt.GenerateSqlCall(description, schema ?? string.Empty, netParams ?? string.Empty);
            
            var messages = new[] { new ChatMessage(ChatRole.User, prompt) };
            var response = await chatClient.GetResponseAsync(messages);
            var responseText = response.Messages.FirstOrDefault()?.Text;

            if (string.IsNullOrWhiteSpace(responseText))
                return Result<string>.Fail("No se pudo generar la consulta SQL");

            return Result<string>.Success(responseText.Trim());
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Error generando SQL: {ex.Message}");
        }
    }
}
