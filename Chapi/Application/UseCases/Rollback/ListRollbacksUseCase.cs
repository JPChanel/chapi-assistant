using Chapi.Domain.Common;
using Chapi.Infrastructure.Persistence.Rollbacks;

namespace Chapi.Application.UseCases.Rollback;

public class ListRollbacksUseCase
{
    public Result<List<RollbackManager.RollbackEntry>> Execute()
    {
        try
        {
            var rollbacks = RollbackManager.GetAvailableRollbacks();
            return Result<List<RollbackManager.RollbackEntry>>.Success(rollbacks);
        }
        catch (Exception ex)
        {
            return Result<List<RollbackManager.RollbackEntry>>.Fail($"Error listando rollbacks: {ex.Message}");
        }
    }
}
