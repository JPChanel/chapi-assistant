using Chapi.Domain.Common;
using Chapi.Infrastructure.Roslyn;
using Chapi.Infrastructure.Persistence.Rollbacks;
using System.IO;

namespace Chapi.Application.UseCases.CodeGeneration;

public class AddApiEndpointUseCase
{
    public Result<RollbackManager.RollbackEntry> Execute(
        string projectPath,
        string moduleName,
        string operation,
        string methodName,
        RollbackManager.RollbackEntry? rollbackEntry = null,
        bool includeAppLayer = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return Result<RollbackManager.RollbackEntry>.Fail("La ruta del proyecto no puede estar vacía");

            if (string.IsNullOrWhiteSpace(moduleName))
                return Result<RollbackManager.RollbackEntry>.Fail("El nombre del módulo no puede estar vacío");

            if (string.IsNullOrWhiteSpace(methodName))
                return Result<RollbackManager.RollbackEntry>.Fail("El nombre del método no puede estar vacío");

            var result = AddApiEndpointMethod.Add(
                projectPath,
                moduleName,
                operation,
                methodName,
                rollbackEntry,
                includeAppLayer);

            if (result == null)
                return Result<RollbackManager.RollbackEntry>.Fail("No se pudo agregar el endpoint API");

            return Result<RollbackManager.RollbackEntry>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<RollbackManager.RollbackEntry>.Fail($"Error agregando API Endpoint: {ex.Message}");
        }
    }
}
