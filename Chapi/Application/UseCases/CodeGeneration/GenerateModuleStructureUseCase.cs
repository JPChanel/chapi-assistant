using Chapi.Domain.Common;
using Chapi.Infrastructure.Services;
using System.IO;

namespace Chapi.Application.UseCases.CodeGeneration;

public class GenerateModuleStructureUseCase
{
    private readonly IModuleGeneratorService _moduleGenerator;

    public GenerateModuleStructureUseCase(IModuleGeneratorService moduleGenerator)
    {
        _moduleGenerator = moduleGenerator;
    }

    public async Task<Result> ExecuteAsync(string projectPath, string moduleName, string dbName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return Result.Fail("La ruta del proyecto no puede estar vacía");

            if (string.IsNullOrWhiteSpace(moduleName))
                return Result.Fail("El nombre del módulo no puede estar vacío");

            if (string.IsNullOrWhiteSpace(dbName))
                return Result.Fail("El nombre de la base de datos no puede estar vacío");

            if (!Directory.Exists(projectPath))
                return Result.Fail("El directorio del proyecto no existe");

            await _moduleGenerator.GenerateModuleAsync(projectPath, moduleName, dbName);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error generando módulo: {ex.Message}");
        }
    }
}
