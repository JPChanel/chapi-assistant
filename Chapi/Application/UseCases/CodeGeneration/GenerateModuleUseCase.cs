using Chapi.Domain.Common;
using Chapi.Infrastructure.Services;

namespace Chapi.Application.UseCases.CodeGeneration;

public class GenerateModuleUseCase
{
    private readonly IModuleGeneratorService _moduleGeneratorService;

    public GenerateModuleUseCase(IModuleGeneratorService moduleGeneratorService)
    {
        _moduleGeneratorService = moduleGeneratorService;
    }

    public async Task<Result> ExecuteAsync(string projectDirectory, string moduleNames, string dbChoice)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
                return Result.Fail("No hay proyecto seleccionado");

            if (string.IsNullOrWhiteSpace(moduleNames))
                return Result.Fail("Debe ingresar al menos un nombre de módulo");

            string dbName = dbChoice.ToUpper() == "S" ? "Sybase" : "Postgres";
            var modules = moduleNames.Split(';').Select(m => m.Trim()).Where(m => m.Length > 0).ToArray();

            foreach (var module in modules)
            {
                await _moduleGeneratorService.GenerateModuleAsync(projectDirectory, module, dbName);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error al generar módulos: {ex.Message}");
        }
    }
}
