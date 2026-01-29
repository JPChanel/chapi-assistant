using Chapi.Domain.Common;
using Chapi.Infrastructure.AI;

namespace Chapi.Application.UseCases.AI;

public class GenerateSqlQueryUseCase
{
    public async Task<Result<string>> ExecuteAsync(string description, string? schema = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(description))
                return Result<string>.Fail("La descripción de la consulta no puede estar vacía");

            var prompt = GetPrompt.GenerateSqlCall(description, schema ?? string.Empty);
            var response = await AIClient.SendPromptAsync(prompt);

            if (string.IsNullOrWhiteSpace(response))
                return Result<string>.Fail("No se pudo generar la consulta SQL");

            return Result<string>.Success(response.Trim());
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Error generando SQL: {ex.Message}");
        }
    }
}
