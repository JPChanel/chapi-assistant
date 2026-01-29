using Chapi.Domain.Common;
using Chapi.Infrastructure.Roslyn;

namespace Chapi.Application.UseCases.CodeGeneration;

public class AddDependencyInjectionUseCase
{
    public Result Execute(
        string projectPath,
        string moduleName,
        IEnumerable<string> operations)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return Result.Fail("La ruta del proyecto no puede estar vacía");

            if (string.IsNullOrWhiteSpace(moduleName))
                return Result.Fail("El nombre del módulo no puede estar vacío");

            AddDependencyInjection.Add(
                projectPath,
                moduleName,
                operations);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error agregando Dependency Injection: {ex.Message}");
        }
    }
}
