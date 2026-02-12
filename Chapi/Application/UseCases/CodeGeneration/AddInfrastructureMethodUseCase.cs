using Chapi.Domain.Common;
using Chapi.Infrastructure.Persistence.Rollbacks;
using Chapi.Infrastructure.Roslyn;

namespace Chapi.Application.UseCases.CodeGeneration;

public class AddInfrastructureMethodUseCase
{
    public async Task<Result<RollbackManager.RollbackEntry>> ExecuteAsync(
        string projectPath,
        string moduleName,
        string dbName,
        string operation,
        string methodName,
        RollbackManager.RollbackEntry? rollbackEntry = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return Result<RollbackManager.RollbackEntry>.Fail("La ruta del proyecto no puede estar vacía");

            if (string.IsNullOrWhiteSpace(moduleName))
                return Result<RollbackManager.RollbackEntry>.Fail("El nombre del módulo no puede estar vacío");

            if (string.IsNullOrWhiteSpace(methodName))
                return Result<RollbackManager.RollbackEntry>.Fail("El nombre del método no puede estar vacío");

            var result = await AddInfrastructureMethod.Add(
                projectPath,
                moduleName,
                dbName,
                operation,
                methodName,
                rollbackEntry);

            if (result == null)
                return Result<RollbackManager.RollbackEntry>.Fail("No se pudo agregar el método a Infrastructure");

            return Result<RollbackManager.RollbackEntry>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<RollbackManager.RollbackEntry>.Fail($"Error agregando método a Infrastructure: {ex.Message}");
        }
    }
}
