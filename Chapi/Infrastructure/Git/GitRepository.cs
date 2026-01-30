using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Domain.Interfaces;
using System.IO;

namespace Chapi.Infrastructure.Git;

/// <summary>
/// Implementacion del repositorio Git.
/// Encapsula todas las operaciones Git usando GitCommandExecutor y GitOutputParser.
/// </summary>
public class GitRepository : IGitRepository
{
    private readonly GitCommandExecutor _executor;
    private readonly GitOutputParser _parser;

    public GitRepository(GitCommandExecutor executor, GitOutputParser parser)
    {
        _executor = executor;
        _parser = parser;
    }

    #region Commits

    public async Task<Result<GitCommit>> CommitAsync(string projectPath, string message, IEnumerable<string> files)
    {
        try
        {
            // 1. Stage files
            var stageResult = await StageFilesAsync(projectPath, files);
            if (!stageResult.IsSuccess)
                return Result<GitCommit>.Fail(stageResult.Error);

            // 2. Commit
            var escapedMessage = message.Replace("\"", "\\\"");
            var result = await _executor.ExecuteAsync($"commit -m \"{escapedMessage}\"", projectPath);

            if (!result.IsSuccess)
                return Result<GitCommit>.Fail(result.Error);

            if (result.Output.Contains("nothing to commit"))
                return Result<GitCommit>.Fail("No hay cambios para commitear");

            // 3. Obtener hash del commit recien creado
            var hashResult = await _executor.ExecuteAsync("rev-parse HEAD", projectPath);
            var hash = hashResult.Output.Trim();

            var commit = new GitCommit
            {
                Hash = hash,
                Message = message,
                Author = Environment.UserName,
                Date = DateTime.Now
            };

            return Result<GitCommit>.Success(commit);
        }
        catch (Exception ex)
        {
            return Result<GitCommit>.Fail($"Error al hacer commit: {ex.Message}");
        }
    }

    public async Task<IEnumerable<GitCommit>> GetCommitsAsync(string projectPath, int limit)
    {
        try
        {
            const string fieldSeparator = "\x1f";
            const string recordSeparator = "\x1e";

            string logFormat = $"%H{fieldSeparator}%an{fieldSeparator}%ar{fieldSeparator}%s{fieldSeparator}%b{recordSeparator}";
            var result = await _executor.ExecuteAsync($"log --pretty=format:\"{logFormat}\" -n {limit}", projectPath);

            if (!result.IsSuccess)
                return Enumerable.Empty<GitCommit>();

            return _parser.ParseLogOutput(result.Output);
        }
        catch
        {
            return Enumerable.Empty<GitCommit>();
        }
    }

    public async Task<HashSet<string>> GetUnpushedCommitsAsync(string projectPath, string branch)
    {
        try
        {
            // Intentar usar el upstream configurado para la rama especifica: branch@{u}
            var cmd = $"log \"{branch}@{{u}}..{branch}\" --pretty=format:%H";
            var result = await _executor.ExecuteAsync(cmd, projectPath);

            if (!result.IsSuccess)
            {
                // Fallback: intentar con origin/branch clÃ¡sico si no hay upstream configurado
                result = await _executor.ExecuteAsync($"log origin/{branch}..{branch} --pretty=format:%H", projectPath);
            }

            if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Output))
                return new HashSet<string>();

            return result.Output
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(h => h.Trim())
                .ToHashSet();
        }
        catch
        {
            return new HashSet<string>();
        }
    }

    #endregion

    #region Changes

    public async Task<IEnumerable<FileChange>> GetChangesAsync(string projectPath)
    {
        try
        {
            var statusTask = _executor.ExecuteAsync("status --porcelain -uall", projectPath);
            var statsTask = _executor.ExecuteAsync("diff --numstat", projectPath);

            await Task.WhenAll(statusTask, statsTask);

            var statusResult = statusTask.Result;
            var statsResult = statsTask.Result;

            if (!statusResult.IsSuccess)
                return Enumerable.Empty<FileChange>();

            var changes = _parser.ParseStatusOutput(statusResult.Output).ToList();
            
            if (statsResult.IsSuccess && !string.IsNullOrWhiteSpace(statsResult.Output))
            {
                var stats = _parser.ParseNumStatOutput(statsResult.Output);
                foreach (var change in changes)
                {
                    // Intentar normalizar paths para el match
                    var normalizedPath = change.FilePath.Replace(Path.DirectorySeparatorChar, '/');
                    
                    // Buscar en el diccionario (que use claves normalizadas o probar ambas)
                    // El parser de numstat ya normaliza a DirectorySeparatorChar, asi que usamos change.FilePath
                    if (stats.TryGetValue(change.FilePath, out var stat))
                    {
                        change.Additions = stat.Additions;
                        change.Deletions = stat.Deletions;
                    }
                    else if (stats.TryGetValue(normalizedPath, out var stat2))
                    {
                        change.Additions = stat2.Additions;
                        change.Deletions = stat2.Deletions;
                    }
                }
            }

            return changes;
        }
        catch
        {
            return Enumerable.Empty<FileChange>();
        }
    }

    public async Task<Result> StageFilesAsync(string projectPath, IEnumerable<string> files)
    {
        try
        {
            foreach (var file in files)
            {
                var result = await _executor.ExecuteAsync($"add \"{file}\"", projectPath);
                if (!result.IsSuccess)
                    return Result.Fail($"Error staging {file}: {result.Error}");
            }
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error al agregar archivos: {ex.Message}");
        }
    }

    public async Task<Result> UnstageFilesAsync(string projectPath, IEnumerable<string> files)
    {
        try
        {
            foreach (var file in files)
            {
                var result = await _executor.ExecuteAsync($"reset HEAD \"{file}\"", projectPath);
                if (!result.IsSuccess)
                    return Result.Fail($"Error unstaging {file}: {result.Error}");
            }
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error al quitar archivos del stage: {ex.Message}");
        }
    }

    #endregion

    #region Branches

    public async Task<IEnumerable<string>> GetBranchesAsync(string projectPath)
    {
        try
        {
            var result = await _executor.ExecuteAsync("branch", projectPath);

            if (!result.IsSuccess)
                return Enumerable.Empty<string>();

            return _parser.ParseBranchOutput(result.Output);
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    public async Task<string> GetCurrentBranchAsync(string projectPath)
    {
        try
        {
            var result = await _executor.ExecuteAsync("branch --show-current", projectPath);
            return result.IsSuccess ? result.Output.Trim() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task<Result> SwitchBranchAsync(string projectPath, string branchName)
    {
        try
        {
            var result = await _executor.ExecuteAsync($"checkout {branchName}", projectPath);

            if (!result.IsSuccess)
                return Result.Fail(result.Error);

            if (result.Output.Contains("error:") || result.Output.Contains("fatal:"))
                return Result.Fail(result.Output);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error al cambiar de rama: {ex.Message}");
        }
    }

    #endregion

    #region Remote

    public async Task<Result> PushAsync(string projectPath, string branch)
    {
        try
        {
            var result = await _executor.ExecuteAsync($"push origin {branch}", projectPath);

            if (!result.IsSuccess)
                return Result.Fail(result.Error);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error al hacer push: {ex.Message}");
        }
    }

    public async Task<Result> PullAsync(string projectPath, string branch)
    {
        try
        {
            var result = await _executor.ExecuteAsync($"pull origin {branch}", projectPath);

            if (!result.IsSuccess)
                return Result.Fail(result.Error);

            if (result.Output.Contains("CONFLICT"))
                return Result.Fail("Hay conflictos que deben resolverse manualmente");

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error al hacer pull: {ex.Message}");
        }
    }

    public async Task<Result> FetchAsync(string projectPath)
    {
        try
        {
            var result = await _executor.ExecuteAsync("fetch", projectPath);

            if (!result.IsSuccess)
                return Result.Fail(result.Error);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error al hacer fetch: {ex.Message}");
        }
    }

    public async Task<(int Ahead, int Behind)> GetAheadBehindCountAsync(string projectPath)
    {
        try
        {
            // Usamos '@{u}' entre comillas para evitar problemas de interpretacion en shells como powershell
            // @{u} referencia al upstream configurado de la rama actual.
            var result = await _executor.ExecuteAsync("rev-list --left-right --count \"@{u}...HEAD\"", projectPath);

            if (!result.IsSuccess)
            {
                // Si falla (ej: no hay upstream), intentamos fallback a origin
                var currentBranch = await GetCurrentBranchAsync(projectPath);
                if (!string.IsNullOrEmpty(currentBranch))
                {
                    result = await _executor.ExecuteAsync($"rev-list --left-right --count origin/{currentBranch}...{currentBranch}", projectPath);
                }
            }

            if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Output))
                return (0, 0);

            var parts = result.Output.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[0], out int behind) && int.TryParse(parts[1], out int ahead))
            {
                return (ahead, behind);
            }

            return (0, 0);
        }
        catch
        {
            return (0, 0);
        }
    }

    #endregion

    #region History Details

    public async Task<IEnumerable<string>> GetFilesChangedInCommitAsync(string projectPath, string hash)
    {
        try
        {
            var result = await _executor.ExecuteAsync($"show --name-only --pretty=format: {hash}", projectPath);
            if (!result.IsSuccess)
                return Enumerable.Empty<string>();

            return result.Output
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f));
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    public async Task<string> GetFileContentAtCommitAsync(string projectPath, string file, string hash)
    {
        try
        {
            // Normalizar separators para Git
            var normalizedFile = file.Replace("\\", "/");
            var result = await _executor.ExecuteAsync($"show \"{hash}:{normalizedFile}\"", projectPath);
            return result.IsSuccess ? result.Output : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task<string> GetCommitParentHashAsync(string projectPath, string hash)
    {
        try
        {
            var result = await _executor.ExecuteAsync($"rev-parse {hash}^", projectPath);
            return result.IsSuccess ? result.Output.Trim() : string.Empty;
        }
        catch
        {
            // Si es el primer commit, no tiene padre
            return string.Empty;
        }
    }

    #endregion

    #region Lifecycle

    public async Task<Result> CloneAsync(string url, string destinationPath)
    {
        try
        {
            // Para clonar, el projectPath es el directorio padre
            var parentDir = Path.GetDirectoryName(destinationPath);
            if (!Directory.Exists(parentDir)) Directory.CreateDirectory(parentDir);

            var result = await _executor.ExecuteAsync($"clone \"{url}\" \"{destinationPath}\"", parentDir);
            return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error al clonar repositorio: {ex.Message}");
        }
    }

    public async Task<Result> InitAsync(string projectPath)
    {
        try
        {
            var result = await _executor.ExecuteAsync("init", projectPath);
            return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error al inicializar repositorio: {ex.Message}");
        }
    }

    public async Task<Result> AddRemoteAsync(string projectPath, string name, string url)
    {
        try
        {
            var result = await _executor.ExecuteAsync($"remote add {name} \"{url}\"", projectPath);
            return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error al agregar remoto: {ex.Message}");
        }
    }

    #endregion

    #region Generic Command Execution

    public async Task<string> ExecuteGitCommandAsync(string projectPath, string command)
    {
        try
        {
            var result = await _executor.ExecuteAsync(command, projectPath);
            return result.IsSuccess ? result.Output : throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error ejecutando comando Git: {ex.Message}");
        }
    }

    #endregion
}

