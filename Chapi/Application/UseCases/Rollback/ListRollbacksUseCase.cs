using Chapi.Domain.Common;
using Chapi.Infrastructure.Persistence.Rollbacks;

using System.IO;
namespace Chapi.Application.UseCases.Rollback;

public class ListRollbacksUseCase
{
    public Result<List<RollbackManager.RollbackEntry>> Execute(string projectPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return Result<List<RollbackManager.RollbackEntry>>.Fail("La ruta del proyecto no puede estar vacÃ­a");

            if (!Directory.Exists(projectPath))
                return Result<List<RollbackManager.RollbackEntry>>.Fail("El directorio del proyecto no existe");

            var rollbacks = RollbackManager.GetAvailableRollbacks(projectPath);
            return Result<List<RollbackManager.RollbackEntry>>.Success(rollbacks);
        }
        catch (Exception ex)
        {
            return Result<List<RollbackManager.RollbackEntry>>.Fail($"Error listando rollbacks: {ex.Message}");
        }
    }
}

