using Chapi.Domain.Common;
using Chapi.Infrastructure.AI;

namespace Chapi.Application.UseCases.AI;

public class GenerateCommitMessageUseCase
{
    public async Task<Result<string>> ExecuteAsync(string diffContent)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(diffContent))
                return Result<string>.Fail("No hay cambios para generar mensaje");

            var prompt = GetPrompt.GitCommit(diffContent);
            var response = await AIClient.SendPromptAsync(prompt);

            if (string.IsNullOrWhiteSpace(response))
                return Result<string>.Fail("No se pudo generar el mensaje de commit");

            return Result<string>.Success(response.Trim());
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Error generando mensaje: {ex.Message}");
        }
    }
}
