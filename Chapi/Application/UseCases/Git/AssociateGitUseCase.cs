using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;

namespace Chapi.Application.UseCases.Git;

public class AssociateGitUseCase
{
    private readonly IGitRepository _gitRepository;

    public AssociateGitUseCase(IGitRepository gitRepository)
    {
        _gitRepository = gitRepository;
    }

    public async Task<Result> ExecuteAsync(string projectPath, string remoteUrl)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return Result.Fail("La ruta del proyecto no puede estar vacía");

            if (string.IsNullOrWhiteSpace(remoteUrl))
                return Result.Fail("La URL del repositorio remoto no puede estar vacía");

            // Verificar si ya tiene un remoto origin
            var currentRemoteResult = await _gitRepository.ExecuteGitCommandAsync(projectPath, "remote get-url origin");
            
            if (!string.IsNullOrEmpty(currentRemoteResult) && !currentRemoteResult.Contains("fatal:"))
            {
                // Ya existe, lo actualizamos
                await _gitRepository.ExecuteGitCommandAsync(projectPath, $"remote set-url origin {remoteUrl}");
            }
            else
            {
                // No existe, lo agregamos
                var result = await _gitRepository.AddRemoteAsync(projectPath, "origin", remoteUrl);
                if (!result.IsSuccess) return result;
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error al asociar Git: {ex.Message}");
        }
    }
}
