using Chapi.Domain.Common;
using Chapi.Infrastructure.Roslyn;

namespace Chapi.Application.UseCases.CodeGeneration;

public class AddDependencyInjectionUseCase
{
    private readonly AddDependencyInjection _addDependencyInjection;

    public AddDependencyInjectionUseCase(AddDependencyInjection addDependencyInjection)
    {
        _addDependencyInjection = addDependencyInjection;
    }

    public async Task<Result<string>> ExecuteAsync(
        string projectPath,
        string interfaceName,
        string implementationName,
        string lifetime = "Scoped")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return Result<string>.Fail("La ruta del proyecto no puede estar vacía");

            if (string.IsNullOrWhiteSpace(interfaceName))
                return Result<string>.Fail("El nombre de la interfaz no puede estar vacío");

            if (string.IsNullOrWhiteSpace(implementationName))
                return Result<string>.Fail("El nombre de la implementación no puede estar vacío");

            var result = await _addDependencyInjection.AddServiceAsync(
                projectPath,
                interfaceName,
                implementationName,
                lifetime);

            if (string.IsNullOrWhiteSpace(result))
                return Result<string>.Fail("No se pudo agregar la configuración de DI");

            return Result<string>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Error agregando Dependency Injection: {ex.Message}");
        }
    }
}
