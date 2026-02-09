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
            // 1. Obtener indicadores locales primero (Rápido)
            var initialCounts = await _gitRepository.GetAheadBehindCountAsync(projectPath);
            onUpdated?.Invoke(initialCounts.Ahead, initialCounts.Behind);

            // 2. Fetch silencioso en segundo plano (Lento)
            await _gitRepository.FetchAsync(projectPath);
            
            // 3. Volver a obtener indicadores tras el fetch
            var finalCounts = await _gitRepository.GetAheadBehindCountAsync(projectPath);
            onUpdated?.Invoke(finalCounts.Ahead, finalCounts.Behind);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error actualizando indicadores para {projectPath}: {ex.Message}");
        }
    }
}
