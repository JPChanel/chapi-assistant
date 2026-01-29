using Chapi.Domain.Common;
using Chapi.Infrastructure.Roslyn;
using Chapi.Infrastructure.Persistence.Rollbacks;

namespace Chapi.Application.UseCases.CodeGeneration;

public class AddApiEndpointUseCase
{
    public async Task<Result<string>> ExecuteAsync(
        string projectPath,
        string moduleName,
        string methodName,
        string httpMethod,
        RollbackManager.RollbackEntry? rollbackEntry = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return Result<string>.Fail("La ruta del proyecto no puede estar vacía");

            if (string.IsNullOrWhiteSpace(moduleName))
                return Result<string>.Fail("El nombre del módulo no puede estar vacío");

            if (string.IsNullOrWhiteSpace(methodName))
                return Result<string>.Fail("El nombre del método no puede estar vacío");

            if (string.IsNullOrWhiteSpace(httpMethod))
                return Result<string>.Fail("El método HTTP no puede estar vacío");

            var result = await AddApiEndpointMethod.AddEndpointAsync(
                projectPath,
                moduleName,
                methodName,
                httpMethod,
                rollbackEntry);

            if (string.IsNullOrWhiteSpace(result))
                return Result<string>.Fail("No se pudo agregar el endpoint API");

            return Result<string>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Error agregando API Endpoint: {ex.Message}");
        }
    }
}

