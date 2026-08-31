using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Projects;

public class UpdateProjectIndicatorsUseCase
{
    private readonly IGitRepository _gitRepository;

    public UpdateProjectIndicatorsUseCase(IGitRepository gitRepository)
    {
        _gitRepository = gitRepository;
    }

    public async Task ExecuteAsync(string projectPath, Action<int, int, bool> onUpdated)
    {
        try
        {
            var countsTask = _gitRepository.GetAheadBehindCountAsync(projectPath);
            var remoteUrlTask = _gitRepository.GetRemoteUrlAsync(projectPath);
            await Task.WhenAll(countsTask, remoteUrlTask);

            var counts = await countsTask;
            var remoteUrl = await remoteUrlTask;
            bool hasRemote = !string.IsNullOrWhiteSpace(remoteUrl);

            onUpdated?.Invoke(counts.Ahead, counts.Behind, hasRemote);
        }
        catch (Exception ex)
        {
        }
    }
}
