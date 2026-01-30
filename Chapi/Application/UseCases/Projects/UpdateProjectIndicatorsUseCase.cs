using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Projects;

public class UpdateProjectIndicatorsUseCase
{
    private readonly IGitRepository _gitRepository;

    public UpdateProjectIndicatorsUseCase(IGitRepository gitRepository)
    {
        _gitRepository = gitRepository;
    }

    public async Task ExecuteAsync(string projectPath, Action<int, int> onUpdated)
    {
        try
        {
            // Fetch silencioso
            await _gitRepository.FetchAsync(projectPath);
            
            // Obtener indicadores
            var counts = await _gitRepository.GetAheadBehindCountAsync(projectPath);
            
            onUpdated?.Invoke(counts.Ahead, counts.Behind);
        }
        catch (Exception ex)
        {
            // Log silencioso para no interrumpir el flujo principal
            System.Diagnostics.Debug.WriteLine($"Error actualizando indicadores para {projectPath}: {ex.Message}");
        }
    }
}
