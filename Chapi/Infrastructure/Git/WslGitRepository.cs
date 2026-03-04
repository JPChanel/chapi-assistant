using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Domain.Enums;
using Chapi.Domain.Interfaces;
using Chapi.Domain.Models;
using System.IO;

namespace Chapi.Infrastructure.Git;

/// <summary>
/// Repositorio Git optimizado para WSL que delega operaciones pesadas al binario nativo de Linux.
/// </summary>
public class WslGitRepository : IGitRepository
{
    private readonly IGitAuthProviderFactory _authFactory;
    private readonly ICredentialStorageService _credentialStorage;

    public WslGitRepository(
        IGitAuthProviderFactory authFactory,
        ICredentialStorageService credentialStorage)
    {
        _authFactory = authFactory;
        _credentialStorage = credentialStorage;
    }

    private async Task<string?> GetAccessTokenAsync(string remoteUrl)
    {
        var provider = _authFactory.DetectProviderFromUrl(remoteUrl);
        if (provider == GitProvider.Unknown) return null;

        var cred = await _credentialStorage.GetCredentialAsync(provider.ToString());
        if (!cred.HasValue) return null;

        return cred.Value.token;
    }

    private string InjectTokenIntoUrl(string url, string token)
    {
        if (string.IsNullOrEmpty(token)) return url;
        
        // Formato esperado: https://gitlab.com/user/repo.git -> https://oauth2:TOKEN@gitlab.com/user/repo.git
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "https://oauth2:" + token + "@" + url.Substring(8);
        }
        return url;
    }
    public async Task<Result> StashChangesAsync(string projectPath, string message, IEnumerable<string>? files = null)
    {
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} stash push -u -m \"{message}\"");
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> StashPopAsync(string projectPath, int? index = null)
    {
        string idx = index?.ToString() ?? "0";
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} stash pop {idx}");
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> StashDropAsync(string projectPath, int index)
    {
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} stash drop {index}");
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> StashClearAsync(string projectPath)
    {
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, "git -C {path} stash clear");
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<IEnumerable<FileChange>> GetChangesAsync(string projectPath)
    {
        var combinedResult = await WslCommandExecutor.ExecuteAsync(projectPath, "git -C {path} status --porcelain && echo \"---STATS--- sprinkles ---\" && git -C {path} diff HEAD --numstat");
        if (!combinedResult.IsSuccess) return Enumerable.Empty<FileChange>();

        var changesDict = new Dictionary<string, FileChange>();
        var parts = combinedResult.Data.Split(new[] { "---STATS--- sprinkles ---" }, StringSplitOptions.None);
        
        var statusLines = parts[0].Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in statusLines)
        {
            if (line.Length < 4) continue;
            string statusPart = line.Substring(0, 2);
            // Tomamos todo a partir del índice 3, que es donde empieza el archivo tras 'XY '
            string filePath = line.Substring(3).Trim('"').Trim().Replace('/', Path.DirectorySeparatorChar);
            ChangeStatus status = ChangeStatus.Modified;
            if (statusPart.Contains('A') || statusPart.Contains('?')) status = ChangeStatus.Added;
            else if (statusPart.Contains('D')) status = ChangeStatus.Deleted;
            else if (statusPart.Contains('R')) status = ChangeStatus.Renamed;

            changesDict[filePath] = new FileChange { FilePath = filePath, Status = status, Additions = 0, Deletions = 0 };
        }

        if (parts.Length > 1)
        {
            var statsLines = parts[1].Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in statsLines)
            {
                var statParts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (statParts.Length >= 3)
                {
                    string filePath = statParts[2].Replace('/', Path.DirectorySeparatorChar);
                    if (changesDict.TryGetValue(filePath, out var change))
                    {
                        if (int.TryParse(statParts[0], out int add)) change.Additions = add;
                        if (int.TryParse(statParts[1], out int del)) change.Deletions = del;
                    }
                }
            }
        }
        return changesDict.Values;
    }

    public async Task<bool> HasUpstreamAsync(string projectPath, string branchName)
    {
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} rev-parse --abbrev-ref {branchName}@{{u}}");
        return result.IsSuccess;
    }

    public async Task<(int Ahead, int Behind)> GetAheadBehindCountAsync(string projectPath)
    {
        var branchResult = await WslCommandExecutor.ExecuteAsync(projectPath, "git -C {path} rev-parse --abbrev-ref HEAD");
        if (!branchResult.IsSuccess) return (0, 0);
        string branch = branchResult.Data.Trim();

        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} rev-list --left-right --count {branch}...{branch}@{{u}}");
        if (!result.IsSuccess) return (0, 0);

        var parts = result.Data.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && int.TryParse(parts[0], out int ahead) && int.TryParse(parts[1], out int behind))
            return (ahead, behind);

        return (0, 0);
    }

    public async Task<string> GetCurrentBranchAsync(string projectPath)
    {
        // git symbolic-ref -q HEAD devuelve el nombre de la rama si existe, de lo contrario falla (detached)
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, "git -C {path} symbolic-ref -q --short HEAD || git -C {path} rev-parse --short HEAD");
        return result.IsSuccess ? result.Data.Trim() : string.Empty;
    }

    public async Task<string> GetFileContentAsync(string projectPath, string revision, string filePath)
    {
        var linuxPath = filePath.Replace(Path.DirectorySeparatorChar, '/');
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} show {revision}:\"{linuxPath}\"");
        return result.IsSuccess ? result.Data : string.Empty;
    }

    public async Task<(int additions, int deletions)> GetFileStatsAsync(string projectPath, string filePath)
    {
        var linuxPath = filePath.Replace(Path.DirectorySeparatorChar, '/');
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} diff HEAD --numstat -- \"{linuxPath}\"");
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Data)) return (0, 0);
        var parts = result.Data.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && int.TryParse(parts[0], out int add) && int.TryParse(parts[1], out int del))
            return (add, del);
        return (0, 0);
    }

    public async Task<IEnumerable<GitStash>> ListStashesAsync(string projectPath)
    {
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, "git -C {path} stash list --format=\"%gd|%ar|%s\"");
        if (!result.IsSuccess) return Enumerable.Empty<GitStash>();
        
        var stashes = new List<GitStash>();
        var lines = result.Data.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|', 3);
            if (parts.Length < 3) continue;
            // En WSL no podemos calcular fácilmente el conteo de archivos sin más comandos, 
            // pero el nombre y mensaje son lo más importante.
            stashes.Add(new GitStash(Name: parts[0], Branch: "WSL", Message: parts[2], FileCount: 0));
        }
        return stashes;
    }

    public async Task<string> GetConfigAsync(string projectPath, string key, bool isGlobal = false)
    {
        string scope = isGlobal ? "--global" : "";
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git config {scope} {key}");
        return result.IsSuccess ? result.Data.Trim() : string.Empty;
    }

    public async Task<Result> SetConfigAsync(string projectPath, string key, string value, bool isGlobal = false)
    {
        string scope = isGlobal ? "--global" : "";
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git config {scope} {key} \"{value}\"");
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> UnsetConfigAsync(string projectPath, string key, bool isGlobal = false)
    {
        string scope = isGlobal ? "--global" : "";
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git config {scope} --unset {key}");
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> FetchAsync(string projectPath)
    {
        var remoteUrl = await GetRemoteUrlAsync(projectPath);
        var token = await GetAccessTokenAsync(remoteUrl);
        
        string target = "origin";
        if (!string.IsNullOrEmpty(token))
        {
            target = InjectTokenIntoUrl(remoteUrl, token);
        }

        // Deshabilitar credential helper temporalmente para evitar cuelgues, y asignar 2 min de timeout
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} -c credential.helper= fetch {target} --prune", 120000);
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<IEnumerable<GitCommit>> GetCommitsAsync(string projectPath, int limit)
    {
        // Formato: hash|shortHash|author|timestamp|message|relativeDate
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} log -n {limit} --format=\"%H|%h|%an|%at|%s|%ar\"");
        if (!result.IsSuccess) return Enumerable.Empty<GitCommit>();

        var commits = new List<GitCommit>();
        var lines = result.Data.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length < 5) continue;
            if (long.TryParse(parts[3], out long ts))
            {
                commits.Add(new GitCommit 
                { 
                    Hash = parts[0], 
                    Author = parts[2], 
                    Date = DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime, 
                    Message = parts[4], 
                    RelativeDate = parts.Length > 5 ? parts[5] : string.Empty,
                    Tags = new List<string>() 
                });
            }
        }
        return commits;
    }

    public async Task<HashSet<string>> GetUnpushedCommitsAsync(string projectPath, string branch)
    {
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} log {branch}@{{u}}..{branch} --format=\"%H\"");
        if (!result.IsSuccess) return new HashSet<string>();
        return result.Data.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
    }

    public async Task<IEnumerable<string>> GetFilesChangedInCommitAsync(string projectPath, string hash)
    {
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} diff-tree --no-commit-id --name-only -r {hash}");
        if (!result.IsSuccess) return Enumerable.Empty<string>();
        return result.Data.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(f => f.Replace('/', Path.DirectorySeparatorChar));
    }

    public async Task<string> GetFileContentAtCommitAsync(string projectPath, string file, string hash)
    {
        var linuxPath = file.Replace(Path.DirectorySeparatorChar, '/');
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} show {hash}:\"{linuxPath}\"");
        return result.IsSuccess ? result.Data : string.Empty;
    }

    public async Task<string> GetRemoteUrlAsync(string projectPath, string remoteName = "origin")
    {
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} remote get-url {remoteName}");
        return result.IsSuccess ? result.Data.Trim() : string.Empty;
    }

    public async System.Threading.Tasks.Task<Chapi.Domain.Common.Result<Chapi.Domain.Models.GitRepositoryMetadata>> GetMetadataAsync(string projectPath)
    {
        // Comando consolidado para obtener todo de un golpe:
        // config name | config email | remote url | current branch | ahead/behind
        var command = "git -C {path} config user.name || echo \"\"; echo \"---\"; " +
                      "git -C {path} config user.email || echo \"\"; echo \"---\"; " +
                      "git -C {path} remote get-url origin 2>/dev/null || echo \"\"; echo \"---\"; " +
                      "git -C {path} symbolic-ref -q --short HEAD || git -C {path} rev-parse --short HEAD; echo \"---\"; " +
                      "git -C {path} symbolic-ref -q HEAD >/dev/null && echo \"false\" || echo \"true\"; echo \"---\"; " +
                      "git -C {path} rev-list --left-right --count HEAD...HEAD@{u} 2>/dev/null || echo \"0\t0\"";

        var result = await WslCommandExecutor.ExecuteAsync(projectPath, command);
        if (!result.IsSuccess) return Result<GitRepositoryMetadata>.Fail(result.Error);

        var m = new GitRepositoryMetadata();
        var parts = result.Data.Split(new[] { "---\n", "---\r\n", "---" }, StringSplitOptions.None)
                          .Select(p => p.Trim()).ToList();

        if (parts.Count >= 6)
        {
            m.UserName = parts[0];
            m.UserEmail = parts[1];
            m.RemoteUrl = parts[2];
            m.CurrentBranch = parts[3];
            m.IsDetached = parts[4] == "true";
            if (m.IsDetached) m.DetachedHeadSha = parts[3];

            var abParts = parts[5].Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (abParts.Length >= 2 && int.TryParse(abParts[0], out int ahead) && int.TryParse(abParts[1], out int behind))
            {
                m.Ahead = ahead;
                m.Behind = behind;
                m.HasUpstream = true;
            }
        }

        return Result<GitRepositoryMetadata>.Success(m);
    }

    public bool IsGitInstalled() => true;

    #region Delegados (No Optimizados en esta clase)
    public Task<Result<GitCommit>> CommitAsync(string projectPath, string message, IEnumerable<string> files) => throw new NotImplementedException();
    public Task<Result> StageFilesAsync(string projectPath, IEnumerable<string> files) => throw new NotImplementedException();
    public Task<Result> UnstageFilesAsync(string projectPath, IEnumerable<string> files) => throw new NotImplementedException();
    public Task<Result> DiscardChangesAsync(string projectPath, IEnumerable<string>? files = null) => throw new NotImplementedException();
    public async Task<IEnumerable<string>> GetBranchesAsync(string projectPath)
    {
        // Obtener ramas locales y remotas (origin/)
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, "git -C {path} branch -a --format=\"%(refname:short)\"");
        if (!result.IsSuccess) return Enumerable.Empty<string>();

        return result.Data.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(b => b.Trim())
                          .Where(b => !b.EndsWith("/HEAD"))
                          .ToList();
    }

    public async Task<Result> SwitchBranchAsync(string projectPath, string branchName)
    {
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} checkout \"{branchName}\"");
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> CreateBranchAsync(string projectPath, string branchName, string? fromCommitOrBranch = null)
    {
        string source = string.IsNullOrEmpty(fromCommitOrBranch) ? "" : $" \"{fromCommitOrBranch}\"";
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} checkout -b \"{branchName}\"{source}");
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> DeleteBranchAsync(string projectPath, string branchName, bool force = false, bool deleteRemote = false)
    {
        string flag = force ? "-D" : "-d";
        var localResult = await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} branch {flag} \"{branchName}\"");
        
        if (localResult.IsSuccess && deleteRemote)
        {
            await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} push origin --delete \"{branchName}\"");
        }

        return localResult.IsSuccess ? Result.Success() : Result.Fail(localResult.Error);
    }
    public Task<Result> MergeBranchAsync(string projectPath, string sourceBranch, bool fastForward = true) => throw new NotImplementedException();
    public Task<Result> SquashMergeBranchAsync(string projectPath, string sourceBranch, string? commitMessage = null) => throw new NotImplementedException();
    public Task<Result> RebaseBranchAsync(string projectPath, string targetBranch) => throw new NotImplementedException();
    public Task<(bool hasConflicts, string message)> CheckMergeConflictsAsync(string projectPath, string sourceBranch) => throw new NotImplementedException();
    public Task<Result> ResetAsync(string projectPath, string target, ResetMode mode) => throw new NotImplementedException();
    public Task<Result> RestoreFileFromStashAsync(string projectPath, string stashName, string filePath) => throw new NotImplementedException();
    public async Task<Result> PushAsync(string projectPath, string branch, bool force = false)
    {
        var remoteUrl = await GetRemoteUrlAsync(projectPath);
        var token = await GetAccessTokenAsync(remoteUrl);
        
        string target = "origin";
        if (!string.IsNullOrEmpty(token))
        {
            target = InjectTokenIntoUrl(remoteUrl, token);
        }

        // Deshabilitar credential helper temporalmente para evitar cuelgues, y asignar 2 min de timeout
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} -c credential.helper= push {target} {branch} {(force ? "-f" : "")}", 120000);
        
        if (result.IsSuccess)
        {
            // Sincronizar ramas de seguimiento para que la UI de Git (y git status) reflejen el éxito
            await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} -c credential.helper= fetch {target} {branch}", 120000);
        }

        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> PullAsync(string projectPath, string branch)
    {
        var remoteUrl = await GetRemoteUrlAsync(projectPath);
        var token = await GetAccessTokenAsync(remoteUrl);

        string target = "origin";
        if (!string.IsNullOrEmpty(token))
        {
            target = InjectTokenIntoUrl(remoteUrl, token);
        }

        // Deshabilitar credential helper temporalmente para evitar cuelgues, y asignar 2 min de timeout
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} -c credential.helper= pull {target} {branch}", 120000);
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }
    public Task<Result> SetRemoteUrlAsync(string projectPath, string remoteName, string url) => throw new NotImplementedException();
    public Task<string> GetCommitParentHashAsync(string projectPath, string hash) => throw new NotImplementedException();
    public Task<Dictionary<string, (int Additions, int Deletions)>> GetCommitNumStatAsync(string projectPath, string hash) => throw new NotImplementedException();
    public Task<Result> CloneAsync(string url, string destinationPath) => throw new NotImplementedException();
    public Task<Result> InitAsync(string projectPath) => throw new NotImplementedException();
    public Task<Result> AddRemoteAsync(string projectPath, string name, string url) => throw new NotImplementedException();
    public async Task<Dictionary<string, char>> GetFileStatusesForStashAsync(string projectPath, string stashName)
    {
        var result = await WslCommandExecutor.ExecuteAsync(projectPath, $"git -C {{path}} stash show {stashName} --name-status");
        var statuses = new Dictionary<string, char>();
        if (!result.IsSuccess) return statuses;
        var lines = result.Data.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 3) continue;
            char status = line[0];
            string filePath = line.Substring(1).Trim().Replace('/', Path.DirectorySeparatorChar);
            statuses[filePath] = status;
        }
        return statuses;
    }
    public Task<Result> CreateTagAsync(string projectPath, string tagName, string message, string commitHash = null) => throw new NotImplementedException();
    public Task<Result> DeleteTagLocalAsync(string projectPath, string tagName) => throw new NotImplementedException();
    public Task<Result> DeleteTagRemoteAsync(string projectPath, string tagName) => throw new NotImplementedException();
    public Task<Result> PushTagAsync(string projectPath, string tagName) => throw new NotImplementedException();
    public Task<IEnumerable<GitTagItem>> GetTagsAsync(string projectPath) => throw new NotImplementedException();
    public Task<Dictionary<string, List<string>>> GetTagCommitMapAsync(string projectPath) => throw new NotImplementedException();
    public Task<string> GetDiffAsync(string projectPath, string file, string? revision = null) => throw new NotImplementedException();
    public Task<string> GetBranchDiffAsync(string projectPath, string sourceBranch, string targetBranch) => throw new NotImplementedException();
    public Task<IEnumerable<GitConflict>> GetMergeConflictsAsync(string projectPath) => throw new NotImplementedException();
    public Task<Result> ResolveConflictAsync(string projectPath, string filePath, string resolvedContent) => throw new NotImplementedException();
    #endregion
}
