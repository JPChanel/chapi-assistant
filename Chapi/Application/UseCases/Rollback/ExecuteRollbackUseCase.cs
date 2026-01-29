using Chapi.Domain.Common;
using Chapi.Infrastructure.Persistence.Rollbacks;

namespace Chapi.Application.UseCases.Rollback;

public class ExecuteRollbackUseCase
{
    public async Task<Result> ExecuteAsync(RollbackManager.RollbackEntry entry)
    {
        try
        {
            if (entry == null)
                return Result.Fail("La entrada de rollback no puede ser nula");

            await RollbackManager.ExecuteRollback(entry);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error ejecutando rollback: {ex.Message}");
        }
    }
}
