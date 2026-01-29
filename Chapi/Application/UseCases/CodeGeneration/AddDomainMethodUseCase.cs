using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Infrastructure.Roslyn;
using Chapi.Infrastructure.Persistence.Rollbacks;

namespace Chapi.Application.UseCases.CodeGeneration;

public class AddDomainMethodUseCase
{
    private readonly AddDomainMethod _addDomainMethod;

    public AddDomainMethodUseCase(AddDomainMethod addDomainMethod)
    {
        _addDomainMethod = addDomainMethod;
    }

    public async Task<Result<string>> ExecuteAsync(
        string projectPath,
        string moduleName,
        string methodName,
        MethodType methodType,
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

            var result = await _addDomainMethod.AddMethodAsync(
                projectPath,
                moduleName,
                methodName,
                methodType,
                rollbackEntry);

            if (string.IsNullOrWhiteSpace(result))
                return Result<string>.Fail("No se pudo agregar el método al dominio");

            return Result<string>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Error agregando método a Domain: {ex.Message}");
        }
    }
}
