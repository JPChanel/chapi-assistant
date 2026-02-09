using Chapi.Domain.Common;
using Chapi.Infrastructure.Persistence.Rollbacks;

namespace Chapi.Application.UseCases.Rollback;

public class CreateRollbackUseCase
{
    public Result<RollbackManager.RollbackEntry> Execute(string module, string methodName, string operation)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(module))
                return Result<RollbackManager.RollbackEntry>.Fail("El módulo no puede estar vacío");

            if (string.IsNullOrWhiteSpace(methodName))
                return Result<RollbackManager.RollbackEntry>.Fail("El nombre del método no puede estar vacío");

            if (string.IsNullOrWhiteSpace(operation))
                return Result<RollbackManager.RollbackEntry>.Fail("La operación no puede estar vacía");

            var entry = RollbackManager.StartTransaction(module, methodName, operation);
            return Result<RollbackManager.RollbackEntry>.Success(entry);
        }
        catch (Exception ex)
        {
            return Result<RollbackManager.RollbackEntry>.Fail($"Error creando rollback: {ex.Message}");
        }
    }
}
