using Chapi.Domain.Interfaces;
using Chapi.Domain.Models;

namespace Chapi.Application.UseCases.Git;

public class LoadReleasesUseCase
{
    private readonly IGitRepository _gitRepository;

    public LoadReleasesUseCase(IGitRepository gitRepository)
    {
        _gitRepository = gitRepository;
    }

    public async Task<IEnumerable<GitTagItem>> ExecuteAsync(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return Enumerable.Empty<GitTagItem>();

        return await _gitRepository.GetTagsAsync(projectPath);
    }
}
