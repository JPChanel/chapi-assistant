using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Domain.Interfaces;
using Chapi.Domain.Models;
using System.IO;
using System.Diagnostics;
using System.ComponentModel;
using System.Text.RegularExpressions;

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
            var tagMap = await GetTagCommitMapAsync(projectPath);

            const string fieldSeparator = "\x1f";
            const string recordSeparator = "\x1e";

            // %D = Ref names (HEAD -> master, v1.0, etc) - lo mantenemos por si acaso, pero usaremos el mapa para tags seguros
            string logFormat = $"%H{fieldSeparator}%an{fieldSeparator}%ar{fieldSeparator}%s{fieldSeparator}%b{fieldSeparator}%D{recordSeparator}";
            var result = await _executor.ExecuteAsync($"log --pretty=format:\"{logFormat}\" -n {limit}", projectPath);

            if (!result.IsSuccess)
                return Enumerable.Empty<GitCommit>();

            var commits = _parser.ParseLogOutput(result.Output).ToList();
            
            // Enrich with tags from the map (more reliable than log %D for peeled tags)
            foreach (var commit in commits)
            {
                // Try full hash or short hash (7 chars)
                if (tagMap.TryGetValue(commit.Hash, out var tags) || 
                   (commit.Hash.Length >= 7 && tagMap.TryGetValue(commit.Hash.Substring(0, 7), out tags)))
                {
                    foreach(var tag in tags)
                    {
                       if (!commit.Tags.Contains(tag)) commit.Tags.Add(tag);
                    }
                }
            }
            return commits;
        }
        catch (Exception ex)
        {
            // Loggin error if needed
            return Enumerable.Empty<GitCommit>();
        }
    }

    public async Task<HashSet<string>> GetUnpushedCommitsAsync(string projectPath, string branch)
    {
        try
        {
            var cmd = $"log \"{branch}@{{u}}..{branch}\" --pretty=format:%H";
            var result = await _executor.ExecuteAsync(cmd, projectPath);

            if (!result.IsSuccess)
            {
                result = await _executor.ExecuteAsync($"log origin/{branch}..{branch} --pretty=format:%H", projectPath);
            }

            if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Output))
            {
                 if (result.Error.Contains("not a valid object name") || result.Error.Contains("ambiguous argument") || result.Error.Contains("no upstream"))
                {
                     var allCommitsResult = await _executor.ExecuteAsync($"log {branch} --pretty=format:%H", projectPath);
                     if (allCommitsResult.IsSuccess && !string.IsNullOrWhiteSpace(allCommitsResult.Output))
                     {
                         return allCommitsResult.Output
                             .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                             .Select(h => h.Trim())
                             .ToHashSet();
                     }
                }
                return new HashSet<string>();
            }

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
                    var normalizedPath = change.FilePath.Replace(Path.DirectorySeparatorChar, '/');
                    
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
            var localResult = await _executor.ExecuteAsync("branch --format=\"%(refname:short)\"", projectPath);
            var locals = localResult.IsSuccess
                ? localResult.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToHashSet()
                : new HashSet<string>();
            var remoteResult = await _executor.ExecuteAsync("branch -r --format=\"%(refname:short)\"", projectPath);
            
            if (remoteResult.IsSuccess && !string.IsNullOrWhiteSpace(remoteResult.Output))
            {
                var lines = remoteResult.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var remoteBranch = line.Trim();
                    if (remoteBranch.EndsWith("/HEAD")) continue;
                    if (!remoteBranch.Contains('/')) continue; 
                    var parts = remoteBranch.Split(new[] { '/' }, 2);
                    var simpleName = parts.Length > 1 ? parts[1] : remoteBranch;

                    if (!locals.Contains(simpleName))
                    {
                        locals.Add(simpleName);
                    }
                }
            }

            return locals.OrderBy(x => x).ToList();
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    public async Task<Dictionary<string, List<string>>> GetTagCommitMapAsync(string projectPath)
    {
        var map = new Dictionary<string, List<string>>();
        // Ensure we get exactly: [CommitHash] [TagName]
        // If annotated (*objectname exists), print peeled hash. Else print objectname.
        string args = "for-each-ref refs/tags --format=\"%(if)%(*objectname)%(then)%(*objectname)%(else)%(objectname)%(end) %(refname:short)\"";

        var result = await _executor.ExecuteAsync(args, projectPath);

        if (!result.IsSuccess)
            return map;

        var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); 
            
            if (parts.Length >= 2)
            {
                string hash = parts[0];
                string tagName = parts[1];

                if (!map.ContainsKey(hash))
                    map[hash] = new List<string>();

                map[hash].Add(tagName);
            }
        }
        return map;
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

            var result = await _executor.ExecuteAsync("rev-list --left-right --count \"@{u}...HEAD\"", projectPath);

            if (!result.IsSuccess)
            {
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

    public async Task<string> GetRemoteUrlAsync(string projectPath, string remoteName = "origin")
    {
         var result = await _executor.ExecuteAsync($"remote get-url {remoteName}", projectPath);
         return result.IsSuccess ? result.Output.Trim() : string.Empty;
    }

    #endregion

    #region History Details

    public async Task<IEnumerable<string>> GetFilesChangedInCommitAsync(string projectPath, string hash)
    {
        try
        {
            var result = await _executor.ExecuteAsync($"show --name-only --pretty=format: \"{hash}^{{}}\"", projectPath);
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

    public async Task<Dictionary<string, (int Additions, int Deletions)>> GetCommitNumStatAsync(string projectPath, string hash)
    {
        var result = await _executor.ExecuteAsync($"show --numstat --pretty=format:\"\" \"{hash}^{{}}\"", projectPath);
        
        if (!result.IsSuccess) return new Dictionary<string, (int Additions, int Deletions)>();

        return _parser.ParseNumStatOutput(result.Output);
    }
    

    public async Task<string> GetFileContentAtCommitAsync(string projectPath, string file, string hash)
    {
        try
        {
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
            return string.Empty;
        }
    }

    #endregion

    #region Lifecycle

    public async Task<Result> CloneAsync(string url, string destinationPath)
    {
        try
        {
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

    #region Stash

    public async Task<IEnumerable<GitStash>> ListStashesAsync(string projectPath)
    {
        try
        {
            var result = await _executor.ExecuteAsync("stash list --pretty=format:\"%gD|%gd|%gs\"", projectPath);
            
            if (!result.IsSuccess)
            {
                 return Enumerable.Empty<GitStash>();
            }

            var stashes = _parser.ParseStashListOutput(result.Output).ToList();

            var populatedStashes = new List<GitStash>();
            foreach (var stash in stashes)
            {
                int count = 0;
                try 
                {
                    var filesResult = await _executor.ExecuteAsync($"stash show --name-only \"{stash.Name}\"", projectPath);
                    if (filesResult.IsSuccess && !string.IsNullOrWhiteSpace(filesResult.Output))
                    {
                        count = filesResult.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    }
                }
                catch { }
                populatedStashes.Add(stash with { FileCount = count });
            }

            return populatedStashes;
        }
        catch
        {
             return Enumerable.Empty<GitStash>();
        }
    }

    public async Task<Dictionary<string, char>> GetFileStatusesForStashAsync(string projectPath, string stashName)
    {
        var statuses = new Dictionary<string, char>();
        try
        {
            var result = await _executor.ExecuteAsync($"stash show --name-status {stashName}", projectPath);
             if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Output))
                return statuses;

            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('\t');
                if (parts.Length == 2)
                {
                    char status = parts[0][0]; 
                    string path = parts[1].Trim().Replace('/', Path.DirectorySeparatorChar);
                    if (!statuses.ContainsKey(path))
                        statuses.Add(path, status);
                }
            }
        }
        catch { }
        return statuses;
    }
    #endregion

    #region Tags
    public async Task<Result> CreateTagAsync(string projectPath, string tagName, string message, string commitHash = null)
    {
        message = message.Replace("\"", "'");
        string hashTarget = string.IsNullOrEmpty(commitHash) ? "" : $" {commitHash}";
        string args = $"tag -a \"{tagName}\" -m \"{message}\"{hashTarget}";

        var result = await _executor.ExecuteAsync(args, projectPath);
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> DeleteTagLocalAsync(string projectPath, string tagName)
    {
        var result = await _executor.ExecuteAsync($"tag -d \"{tagName}\"", projectPath);
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<IEnumerable<GitTagItem>> GetTagsAsync(string projectPath)
    {
        try
        {
            const string fieldSeparator = "\x1f";
            const string recordSeparator = "\x1e";

            // Formato: tag name, object hash, creator name, creator relative date, subject, body
            // Formato: tag name | commit hash (peeled) | author | date | commit subject | tag message | commit body
            string format = $"%(refname:short){fieldSeparator}" +
                            $"%(if)%(*objectname)%(then)%(*objectname)%(else)%(objectname)%(end){fieldSeparator}" +
                            $"%(if)%(*authorname)%(then)%(*authorname)%(else)%(authorname)%(end){fieldSeparator}" +
                            $"%(committerdate:relative){fieldSeparator}" +
                            $"%(if)%(*subject)%(then)%(*subject)%(else)%(subject)%(end){fieldSeparator}" +
                            $"%(contents:subject){fieldSeparator}" +
                            $"%(if)%(*body)%(then)%(*body)%(else)%(body)%(end){recordSeparator}";

            var result = await _executor.ExecuteAsync($"for-each-ref --format=\"{format}\" --sort=-taggerdate refs/tags", projectPath);

            if (!result.IsSuccess)
            {
                string simpleFormat = $"%(refname:short){fieldSeparator}%(objectname){fieldSeparator}%(authorname){fieldSeparator}%(authordate:relative){fieldSeparator}%(subject){fieldSeparator}%(contents:body){recordSeparator}";
                result = await _executor.ExecuteAsync($"for-each-ref --format=\"{simpleFormat}\" --sort=-creatordate refs/tags", projectPath);
            }

            if (!result.IsSuccess)
                return Enumerable.Empty<GitTagItem>();

            var tags = _parser.ParseTagsOutput(result.Output).ToList();

            if (tags.Any())
            {
                tags.First().IsLatest = true;
            }

            return tags;
        }
        catch
        {
            return Enumerable.Empty<GitTagItem>();
        }
    }
    #endregion

    #region Misc
    public bool IsGitInstalled()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(psi))
            {
                if (process == null) return false;
                process.WaitForExit(2000); 
                return process.ExitCode == 0;
            }
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> HasUpstreamAsync(string projectPath, string branchName)
    {
        var result = await _executor.ExecuteAsync($"rev-parse --abbrev-ref \"{branchName}@{{u}}\"", projectPath);
        return result.IsSuccess && !string.IsNullOrWhiteSpace(result.Output);
    }

    public async Task<string> GetFileContentAsync(string projectPath, string revision, string filePath)
    {
        string normalizedPath = filePath.Replace("\\", "/");
        var result = await _executor.ExecuteAsync($"show {revision}:\"{normalizedPath}\"", projectPath);
        return result.IsSuccess ? result.Output : string.Empty;
    }
    #endregion

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

