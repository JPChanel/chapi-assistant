using Chapi.Domain.Common;
using Chapi.Infrastructure.Services;

namespace Chapi.Application.UseCases.CodeGeneration;

public class GenerateModuleStructureUseCase
{
    private readonly ModuleGeneratorService _moduleGenerator;

    public GenerateModuleStructureUseCase(ModuleGeneratorService moduleGenerator)
    {
        _moduleGenerator = moduleGenerator;
    }

    public async Task<Result> ExecuteAsync(string projectPath, string moduleName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return Result.Fail("La ruta del proyecto no puede estar vacía");

            if (string.IsNullOrWhiteSpace(moduleName))
                return Result.Fail("El nombre del módulo no puede estar vacío");

            if (!Directory.Exists(projectPath))
                return Result.Fail("El directorio del proyecto no existe");

            await _moduleGenerator.GenerateModuleAsync(projectPath, moduleName);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error generando módulo: {ex.Message}");
        }
    }
}
