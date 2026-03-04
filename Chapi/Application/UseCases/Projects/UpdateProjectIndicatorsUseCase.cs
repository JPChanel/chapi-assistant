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
            // Solo obtener indicadores locales (Rápido)
            // No hacemos Fetch aquí para evitar bucles con el FileSystemWatcher
            var counts = await _gitRepository.GetAheadBehindCountAsync(projectPath);
            onUpdated?.Invoke(counts.Ahead, counts.Behind);
        }
        catch (Exception ex)
        {
        }
    }
}
