using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Domain.Enums;
using Chapi.Domain.Interfaces;
using Chapi.Domain.Models;
using System.IO;

namespace Chapi.Infrastructure.Git;

/// <summary>
/// Implementación de IGitRepository basada 100% en el CLI de Git.
/// Mismo modelo que GitHub Desktop (dugite): usa git.exe nativo de Windows directamente.
/// Funciona en rutas UNC (\\wsl$\) sin la sobrecarga de metadatos de libgit2.
/// </summary>
public class GitCliRepository : IGitRepository
{
    private readonly IGitAuthProviderFactory _authFactory;
    private readonly ICredentialStorageService _credentialStorage;
    private string? _cachedRepoRoot;

    public GitCliRepository(
        IGitAuthProviderFactory authFactory,
        ICredentialStorageService credentialStorage)
    {
        _authFactory = authFactory;
        _credentialStorage = credentialStorage;
    }

    private async Task<string> GetRepoRootAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) 
            return path;

        if (_cachedRepoRoot != null && path.StartsWith(_cachedRepoRoot, StringComparison.OrdinalIgnoreCase))
            return _cachedRepoRoot;

        var result = await GitProcessExecutor.RunAsync(path, "rev-parse", "--show-toplevel");
        if (result.IsSuccess)
        {
            var detected = result.Data.Trim();
            // Git rev-parse puede devolver rutas tipo Linux (/d/ruta) en algunos entornos.
            // Las normalizamos a formato Windows real.
            if (detected.Length >= 2 && detected[0] == '/' && char.IsLetter(detected[1]) && (detected.Length == 2 || detected[2] == '/'))
            {
                detected = detected[1] + ":" + detected.Substring(2);
            }
            
            _cachedRepoRoot = Path.GetFullPath(detected.Replace('/', Path.DirectorySeparatorChar));
            return _cachedRepoRoot;
        }
        return Path.GetFullPath(path);
    }

    // Atajo para correr git siempre en la raíz del repositorio
    private async Task<Result<string>> Git(string workingDir, params string[] args)
    {
        if (string.IsNullOrWhiteSpace(workingDir)) 
            return Result<string>.Fail("Ruta del proyecto no válida (vacía).");

        var root = await GetRepoRootAsync(workingDir);
        // Ejecutamos desde la raíz pero pasamos todos los argumentos relativos a ella
        return await GitProcessExecutor.RunAsync(root, args);
    }

    private string GetRelativePath(string repoRoot, string projectPath, string filePath)
    {
        if (string.IsNullOrWhiteSpace(repoRoot) || string.IsNullOrWhiteSpace(filePath)) 
            return filePath ?? string.Empty;

        // Forzamos GetFullPath en ambos para asegurar que la unidad (C:\ vs c:\) y separadores coincidan
        string normalizedRoot = Path.GetFullPath(repoRoot);
        string absolutePath = Path.IsPathRooted(filePath)
            ? Path.GetFullPath(filePath)
            : Path.GetFullPath(Path.Combine(projectPath, filePath));

        var relative = Path.GetRelativePath(normalizedRoot, absolutePath);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private async Task<string?> GetAccessTokenAsync(string remoteUrl)
    {
        var provider = _authFactory.DetectProviderFromUrl(remoteUrl);
        if (provider == GitProvider.Unknown) return null;

        var cred = await _credentialStorage.GetCredentialAsync(provider.ToString());
        if (!cred.HasValue) return null;

        return cred.Value.token;
    }

    private async Task<Result<T?>> ExecuteAuthenticatedAsync<T>(string projectPath, string remoteUrl, Func<string, Dictionary<string, string>, Task<Result<T?>>> action)
    {
        var token = await GetAccessTokenAsync(remoteUrl);
        var env = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(token))
        {
            env["CHAPI_GIT_TOKEN"] = token;
        }

        // Pasamos el remoteUrl original. GitProcessExecutor configurará ASK_PASS 
        // para que cuando Git pida credenciales para esta URL, el script responda con el token.
        var result = await action(remoteUrl, env);

        // Reintento con Refresh Token si falla por permisos
        if (!result.IsSuccess && (result.Error.Contains("403") || result.Error.Contains("401") || result.Error.Contains("authentication")))
        {
            var providerType = _authFactory.DetectProviderFromUrl(remoteUrl);
            var authProvider = _authFactory.GetProvider(providerType);

            var refreshResult = await authProvider.RefreshTokenAsync();
            if (refreshResult.IsSuccess)
            {
                var newToken = refreshResult.Data.AccessToken;
                env["CHAPI_GIT_TOKEN"] = newToken;
                return await action(remoteUrl, env);
            }
        }

        return result;
    }

    // ─── Cambios ─────────────────────────────────────────────────────────────

    public async Task<IEnumerable<FileChange>> GetChangesAsync(string projectPath)
    {
        var root = await GetRepoRootAsync(projectPath);
        // Usamos -z para evitar problemas con rutas con espacios o caracteres especiales
        var result = await Git(projectPath, "status", "--porcelain=v1", "-z");
        if (!result.IsSuccess) return Enumerable.Empty<FileChange>();

        var changes = new List<FileChange>();
        var data = result.Data;
        int i = 0;
        
        while (i < data.Length)
        {
            if (i + 3 >= data.Length) break;
            
            var xy = data.Substring(i, 2);
            i += 3; // Saltar XY y el espacio/NUL
            
            int nulIdx = data.IndexOf('\0', i);
            if (nulIdx < 0) break;
            
            var path = data.Substring(i, nulIdx - i);
            i = nulIdx + 1;
            
            // Si es un renombramiento (R) o copia (C), -z devuelve dos rutas: origin\0target\0
            if (xy.StartsWith('R') || xy.StartsWith('C'))
            {
                int nextNul = data.IndexOf('\0', i);
                if (nextNul < 0) break;
                // La ruta que nos interesa para mostrar es el destino (target)
                path = data.Substring(i, nextNul - i);
                i = nextNul + 1;
            }
            
            // Calculamos la ruta absoluta y luego la relativa al projectPath
            // para que la UI vea rutas coherentes con la carpeta abierta.
            var absolutePath = Path.GetFullPath(Path.Combine(root, path));
            var relativeToProject = Path.GetRelativePath(projectPath, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

            changes.Add(new FileChange
            {
                FilePath = relativeToProject,
                Status = DetermineStatus(xy),
            });
        }
        return changes;
    }

    private static ChangeStatus DetermineStatus(string xy)
    {
        if (xy.Contains('?')) return ChangeStatus.Untracked;
        if (xy.Contains('A')) return ChangeStatus.Added;
        if (xy.Contains('D')) return ChangeStatus.Deleted;
        if (xy.Contains('R')) return ChangeStatus.Renamed;
        if (xy.Contains('U') || xy == "AA" || xy == "DD" || xy == "UU") return ChangeStatus.Conflict;
        return ChangeStatus.Modified;
    }

    public async Task<Result> StageFilesAsync(string projectPath, IEnumerable<string> files)
    {
        var root = await GetRepoRootAsync(projectPath);
        
        var existingFiles = new List<string>();
        var missingFiles = new List<string>();

        foreach (var file in files)
        {
            var absolutePath = Path.IsPathRooted(file) 
                ? Path.GetFullPath(file) 
                : Path.GetFullPath(Path.Combine(projectPath, file));

            var relativePath = GetRelativePath(root, projectPath, file);

            if (File.Exists(absolutePath) || Directory.Exists(absolutePath))
            {
                existingFiles.Add(relativePath);
            }
            else
            {
                missingFiles.Add(relativePath);
            }
        }

        // 1. Añadir archivos que existen
        if (existingFiles.Any())
        {
            var args = new List<string> { "add", "--" };
            args.AddRange(existingFiles);
            var rAdd = await Git(projectPath, args.ToArray());
            if (!rAdd.IsSuccess) return Result.Fail(rAdd.Error);
        }

        // 2. Remover del índice los que ya no existen
        if (missingFiles.Any())
        {
            var args = new List<string> { "rm", "--cached", "--ignore-unmatch", "--quiet", "--" };
            args.AddRange(missingFiles);
            var rRm = await Git(projectPath, args.ToArray());
            if (!rRm.IsSuccess) return Result.Fail(rRm.Error);
        }

        return Result.Success();
    }

    public async Task<Result> UnstageFilesAsync(string projectPath, IEnumerable<string> files)
    {
        var root = await GetRepoRootAsync(projectPath);
        var args = new List<string> { "restore", "--staged", "--" };
        args.AddRange(files.Select(f => GetRelativePath(root, projectPath, f)));
        var result = await Git(projectPath, args.ToArray());
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> DiscardChangesAsync(string projectPath, IEnumerable<string>? files = null)
    {
        var root = await GetRepoRootAsync(projectPath);

        if (files == null || !files.Any())
        {
            // Reset index (unstage everything)
            await Git(projectPath, "reset", ".");
            // Revert all tracked files to match index
            var r1 = await Git(projectPath, "checkout", "--", ".");
            // Remove all untracked files and directories
            var r2 = await Git(projectPath, "clean", "-df");
            
            return r1.IsSuccess && r2.IsSuccess ? Result.Success() : Result.Fail(r1.IsSuccess ? r2.Error : r1.Error);
        }

        foreach (var file in files)
        {
            var relativePath = GetRelativePath(root, projectPath, file);

            // 1. Deshacer el Stage (si existe) para volverlo unstaged
            await Git(projectPath, "reset", "--", relativePath);
            
            // 2. Intentar checkout (para archivos modificados rastreados)
            var r = await Git(projectPath, "checkout", "--", relativePath);
            if (!r.IsSuccess)
            {
                // 3. Si falla checkout (porque es un archivo nuevo), forzamos su eliminación
                await Git(projectPath, "clean", "-df", "--", relativePath);
            }
        }

        return Result.Success();
    }

    // ─── Commit ──────────────────────────────────────────────────────────────

    public async Task<Result<GitCommit>> CommitAsync(string projectPath, string message, IEnumerable<string> files)
    {
        var stageResult = await StageFilesAsync(projectPath, files);
        if (!stageResult.IsSuccess) return Result<GitCommit>.Fail(stageResult.Error);

        var commitResult = await Git(projectPath, "commit", "-m", message);
        if (!commitResult.IsSuccess) return Result<GitCommit>.Fail(commitResult.Error);

        var hashResult = await Git(projectPath, "rev-parse", "HEAD");
        if (!hashResult.IsSuccess) return Result<GitCommit>.Fail("Commit creado pero no se pudo obtener el hash.");

        return Result<GitCommit>.Success(new GitCommit
        {
            Hash = hashResult.Data.Trim(),
            Message = message,
            Date = DateTime.Now,
            Author = string.Empty,
        });
    }

    public async Task<IEnumerable<GitCommit>> GetCommitsAsync(string projectPath, int limit)
    {
        // Usamos delimitadores nulos (%x00) para campos y %x01 para registros completos.
        // Esto permite que el cuerpo del mensaje tenga saltos de línea sin romper el parseo.
        var format = "%H%x00%an%x00%at%x00%s%x00%b%x00%ar%x01";
        var result = await Git(projectPath, "log", $"-n{limit}", $"--format={format}");
        if (!result.IsSuccess) return Enumerable.Empty<GitCommit>();

        var commits = new List<GitCommit>();
        // Dividimos por el separador de registro (%x01)
        var records = result.Data.Split('\x01', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var record in records)
        {
            var parts = record.TrimStart('\n', '\r').Split('\0');
            if (parts.Length < 4) continue;

            if (long.TryParse(parts[2], out var ts))
            {
                commits.Add(new GitCommit
                {
                    Hash = parts[0],
                    Author = parts[1],
                    Date = DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime,
                    Message = parts[3],
                    Description = parts.Length > 4 ? parts[4].Trim() : string.Empty,
                    RelativeDate = parts.Length > 5 ? parts[5] : string.Empty,
                    Tags = new List<string>()
                });
            }
        }
        return commits;
    }

    public async Task<HashSet<string>> GetUnpushedCommitsAsync(string projectPath, string branch)
    {
        var result = await Git(projectPath, "log", $"origin/{branch}..{branch}", "--format=%H");
        if (!result.IsSuccess) return new HashSet<string>();
        return result.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
    }

    // ─── Branches ────────────────────────────────────────────────────────────

    public async Task<IEnumerable<string>> GetBranchesAsync(string projectPath)
    {
        var result = await Git(projectPath, "branch", "--list", "--format=%(refname:short)");
        if (!result.IsSuccess) return Enumerable.Empty<string>();

        return result.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(b => b.Trim())
            .Select(b => b.StartsWith("* ") ? b.Substring(2) : b)
            .Where(b => !string.IsNullOrEmpty(b) && !b.StartsWith("warning:", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<string> GetCurrentBranchAsync(string projectPath)
    {
        var result = await Git(projectPath, "rev-parse", "--abbrev-ref", "HEAD");
        return result.IsSuccess ? result.Data.Trim() : string.Empty;
    }

    public async Task<Result> SwitchBranchAsync(string projectPath, string branchName)
    {
        var result = await Git(projectPath, "checkout", branchName);
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> CreateBranchAsync(string projectPath, string branchName, string? fromCommitOrBranch = null)
    {
        var args = fromCommitOrBranch != null
            ? new[] { "checkout", "-b", branchName, fromCommitOrBranch }
            : new[] { "checkout", "-b", branchName };
        var result = await GitProcessExecutor.RunAsync(projectPath, args);
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> DeleteBranchAsync(string projectPath, string branchName, bool force = false, bool deleteRemote = false)
    {
        var flag = force ? "-D" : "-d";
        var result = await Git(projectPath, "branch", flag, branchName);
        if (!result.IsSuccess) return Result.Fail(result.Error);
        if (deleteRemote)
        {
            var remoteUrl = await GetRemoteUrlAsync(projectPath);
            var pushResult = await ExecuteAuthenticatedAsync<string>(projectPath, remoteUrl, async (_, env) =>
            {
                var r = await GitProcessExecutor.RunAsync(projectPath, 120_000, env, "push", "origin", "--delete", branchName);
                return r.IsSuccess ? Result<string?>.Success(r.Data) : Result<string?>.Fail(r.Error);
            });
            if (!pushResult.IsSuccess) return Result.Fail(pushResult.Error);
        }
        return Result.Success();
    }

    public async Task<Result> MergeBranchAsync(string projectPath, string sourceBranch, bool fastForward = true)
    {
        var ffFlag = fastForward ? "--ff" : "--no-ff";
        var result = await Git(projectPath, "merge", ffFlag, sourceBranch);
        if (!result.IsSuccess && result.Error.Contains("CONFLICT"))
            return Result.Fail("CONFLICTO_DETECTADO");
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> SquashMergeBranchAsync(string projectPath, string sourceBranch, string? commitMessage = null)
    {
        var merge = await Git(projectPath, "merge", "--squash", sourceBranch);
        if (!merge.IsSuccess)
        {
            await Git(projectPath, "reset", "--hard", "HEAD");
            return Result.Fail(merge.Error);
        }

        var msg = commitMessage ?? $"Squash merge from {sourceBranch}";
        var commit = await Git(projectPath, "commit", "-m", msg);
        return commit.IsSuccess ? Result.Success() : Result.Fail(commit.Error);
    }

    public async Task<Result> RebaseBranchAsync(string projectPath, string targetBranch)
    {
        var result = await Git(projectPath, "rebase", targetBranch);
        if (!result.IsSuccess)
        {
            await Git(projectPath, "rebase", "--abort");
            return Result.Fail(result.Error);
        }
        return Result.Success();
    }

    public async Task<(bool hasConflicts, string message)> CheckMergeConflictsAsync(string projectPath, string sourceBranch)
    {
        var result = await Git(projectPath, "merge", "--no-commit", "--no-ff", sourceBranch);
        var hasConflicts = !result.IsSuccess && (result.Error?.Contains("CONFLICT") == true || result.Data?.Contains("CONFLICT") == true);

        await Git(projectPath, "merge", "--abort");

        return (hasConflicts, hasConflicts ? "Conflictos detectados" : string.Empty);
    }

    public async Task<Result> ResetAsync(string projectPath, string target, ResetMode mode)
    {
        var modeFlag = mode switch
        {
            ResetMode.Soft => "--soft",
            ResetMode.Mixed => "--mixed",
            ResetMode.Hard => "--hard",
            _ => "--mixed"
        };
        var result = await Git(projectPath, "reset", modeFlag, target);
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> RestoreFileFromStashAsync(string projectPath, string stashName, string filePath)
    {
        var linuxPath = filePath.Replace(Path.DirectorySeparatorChar, '/');
        var result = await Git(projectPath, "checkout", stashName, "--", linuxPath);
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> StashChangesAsync(string projectPath, string message, IEnumerable<string>? files = null)
    {
        var normalizedFiles = files?.Select(f => f.Replace(Path.DirectorySeparatorChar, '/')).ToList();
        var args = new List<string> { "stash", "push", "-u", "-m", message };
        if (normalizedFiles?.Any() == true)
        {
            args.Add("--");
            args.AddRange(normalizedFiles);
        }
        var result = await GitProcessExecutor.RunAsync(projectPath, args.ToArray());

        if (!result.IsSuccess)
        {
            if (result.Error?.Contains("No local changes to save") == true ||
                result.Data?.Contains("No local changes to save") == true)
                return Result.Fail("No hay cambios locales para guardar.");
            return Result.Fail(result.Error ?? "Error al hacer stash.");
        }
        return Result.Success();
    }

    public async Task<IEnumerable<GitStash>> ListStashesAsync(string projectPath)
    {
        var result = await Git(projectPath, "stash", "list", "--format=%gd|%s");
        if (!result.IsSuccess) return Enumerable.Empty<GitStash>();

        var stashes = new List<GitStash>();
        var lines = result.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var idx = line.IndexOf('|');
            if (idx < 0) continue;
            var name = line.Substring(0, idx).Trim();
            var msgPart = line.Substring(idx + 1).Trim();

            string branch = "unknown";
            string msg = msgPart;
            if (msgPart.StartsWith("WIP on ") || msgPart.StartsWith("On "))
            {
                var colonIdx = msgPart.IndexOf(':');
                if (colonIdx > 0)
                {
                    branch = msgPart.Substring(msgPart.IndexOf(' ') + 1, colonIdx - msgPart.IndexOf(' ') - 1).Trim();
                    msg = msgPart.Substring(colonIdx + 1).Trim();
                    var spaceIdx = msg.IndexOf(' ');
                    if (spaceIdx > 0 && msg.Substring(0, spaceIdx).Length == 7)
                        msg = msg.Substring(spaceIdx + 1);
                }
            }

            int fileCount = 0;
            var showResult = await Git(projectPath, "stash", "show", name);
            if (showResult.IsSuccess)
            {
                fileCount = showResult.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1;
                if (fileCount < 0) fileCount = 0;
            }

            stashes.Add(new GitStash(name, branch, msg, fileCount));
        }
        return stashes;
    }

    public async Task<Dictionary<string, char>> GetFileStatusesForStashAsync(string projectPath, string stashName)
    {
        var result = await Git(projectPath, "stash", "show", stashName, "--name-status");
        var statuses = new Dictionary<string, char>();
        if (!result.IsSuccess) return statuses;
        foreach (var line in result.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 3) continue;
            char status = line[0];
            var filePath = line.Substring(1).Trim().Replace('/', Path.DirectorySeparatorChar);
            statuses[filePath] = status;
        }
        return statuses;
    }

    public async Task<Result> StashPopAsync(string projectPath, int? index = null)
    {
        var stashRef = $"stash@{{{index ?? 0}}}";
        var result = await Git(projectPath, "stash", "pop", stashRef);
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> StashDropAsync(string projectPath, int index)
    {
        var stashRef = $"stash@{{{index}}}";
        var result = await Git(projectPath, "stash", "drop", stashRef);
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> StashClearAsync(string projectPath)
    {
        var result = await Git(projectPath, "stash", "clear");
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    // ─── Remote / Sync ───────────────────────────────────────────────────────

    public async Task<Result> PushAsync(string projectPath, string branch, bool force = false)
    {
        var remoteUrl = await GetRemoteUrlAsync(projectPath);
        // Verificar si la rama tiene upstream para decidir si usar -u (set-upstream)
        bool existsOnRemote = await HasUpstreamAsync(projectPath, branch);

        var result = await ExecuteAuthenticatedAsync<string>(projectPath, remoteUrl, async (target, env) =>
        {
            var argsList = new List<string> { "push" };
            if (force) argsList.Add("--force-with-lease");
            if (!existsOnRemote) argsList.Add("-u"); // Publicar rama

            // Usamos "origin" en lugar de la URL directa para que Git actualice origin/branch
            argsList.Add("origin");
            argsList.Add(branch);

            var r = await GitProcessExecutor.RunAsync(projectPath, 120_000, env, argsList.ToArray());
            return r.IsSuccess ? Result<string?>.Success(r.Data) : Result<string?>.Fail(r.Error);
        });

        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> PullAsync(string projectPath, string branch)
    {
        var remoteUrl = await GetRemoteUrlAsync(projectPath);
        var result = await ExecuteAuthenticatedAsync<string>(projectPath, remoteUrl, async (target, env) =>
        {
            // Usamos "origin" para que actualice las ramas de rastreo locales
            var r = await GitProcessExecutor.RunAsync(projectPath, 120_000, env, "pull", "origin", branch);
            return r.IsSuccess ? Result<string?>.Success(r.Data) : Result<string?>.Fail(r.Error);
        });

        if (!result.IsSuccess && result.Error.Contains("CONFLICT"))
            return Result.Fail("Conflictos al hacer pull");

        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> FetchAsync(string projectPath)
    {
        var remoteUrl = await GetRemoteUrlAsync(projectPath);
        var result = await ExecuteAuthenticatedAsync<string>(projectPath, remoteUrl, async (target, env) =>
        {
            // Fetch origin actualiza todos los punteros origin/*
            var r = await GitProcessExecutor.RunAsync(projectPath, 120_000, env, "fetch", "origin", "--prune");
            return r.IsSuccess ? Result<string?>.Success(r.Data) : Result<string?>.Fail(r.Error);
        });
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<(int Ahead, int Behind)> GetAheadBehindCountAsync(string projectPath)
    {
        var result = await Git(projectPath, "rev-list", "--left-right", "--count", "HEAD...@{upstream}");
        if (!result.IsSuccess) return (0, 0);
        var parts = result.Data.Trim().Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return (0, 0);
        int.TryParse(parts[0], out var ahead);
        int.TryParse(parts[1], out var behind);
        return (ahead, behind);
    }

    public async Task<string> GetRemoteUrlAsync(string projectPath, string remoteName = "origin")
    {
        var result = await Git(projectPath, "remote", "get-url", remoteName);
        return result.IsSuccess ? result.Data.Trim() : string.Empty;
    }

    public async Task<Result> SetRemoteUrlAsync(string projectPath, string remoteName, string url)
    {
        var result = await Git(projectPath, "remote", "set-url", remoteName, url);
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<bool> HasUpstreamAsync(string projectPath, string branchName)
    {
        var result = await Git(projectPath, "rev-parse", "--abbrev-ref", $"{branchName}@{{upstream}}");
        return result.IsSuccess;
    }

    // ─── Commits / Historial ─────────────────────────────────────────────────

    public async Task<IEnumerable<string>> GetFilesChangedInCommitAsync(string projectPath, string hash)
    {
        var result = await Git(projectPath, "diff-tree", "--no-commit-id", "--name-only", "-r", hash);
        if (!result.IsSuccess) return Enumerable.Empty<string>();
        return result.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                          .Select(f => f.Replace('/', Path.DirectorySeparatorChar));
    }

    public async Task<string> GetFileContentAtCommitAsync(string projectPath, string file, string hash)
    {
        var linuxPath = file.Replace(Path.DirectorySeparatorChar, '/');
        var result = await Git(projectPath, "show", $"{hash}:{linuxPath}");
        return result.IsSuccess ? result.Data : string.Empty;
    }

    public async Task<string> GetCommitParentHashAsync(string projectPath, string hash)
    {
        var result = await Git(projectPath, "rev-parse", $"{hash}^");
        return result.IsSuccess ? result.Data.Trim() : string.Empty;
    }

    public async Task<Dictionary<string, (int Additions, int Deletions)>> GetCommitNumStatAsync(string projectPath, string hash)
    {
        var result = await Git(projectPath, "show", "--numstat", "--format=", hash);
        var dict = new Dictionary<string, (int, int)>();
        if (!result.IsSuccess) return dict;
        foreach (var line in result.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 3) continue;
            if (int.TryParse(parts[0], out var add) && int.TryParse(parts[1], out var del))
                dict[parts[2].Replace('/', Path.DirectorySeparatorChar)] = (add, del);
        }
        return dict;
    }

    // ─── Archivos ────────────────────────────────────────────────────────────

    public async Task<string> GetFileContentAsync(string projectPath, string revision, string filePath)
    {
        var linuxPath = filePath.Replace(Path.DirectorySeparatorChar, '/');
        var result = await Git(projectPath, "show", $"{revision}:{linuxPath}");
        return result.IsSuccess ? result.Data : string.Empty;
    }

    public async Task<(int additions, int deletions)> GetFileStatsAsync(string projectPath, string filePath)
    {
        var linuxPath = filePath.Replace(Path.DirectorySeparatorChar, '/');
        var result = await Git(projectPath, "diff", "HEAD", "--numstat", "--", linuxPath);
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Data)) return (0, 0);
        var parts = result.Data.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && int.TryParse(parts[0], out int add) && int.TryParse(parts[1], out int del))
            return (add, del);
        return (0, 0);
    }

    public async Task<string> GetDiffAsync(string projectPath, string file, string? revision = null)
    {
        var linuxPath = file.Replace(Path.DirectorySeparatorChar, '/');
        var result = revision != null
            ? await Git(projectPath, "diff", revision, "--", linuxPath)
            : await Git(projectPath, "diff", "HEAD", "--", linuxPath);
        return result.IsSuccess ? result.Data : string.Empty;
    }

    public async Task<string> GetBranchDiffAsync(string projectPath, string sourceBranch, string targetBranch)
    {
        var result = await Git(projectPath, "diff", $"{targetBranch}...{sourceBranch}");
        return result.IsSuccess ? result.Data : string.Empty;
    }

    // ─── Config ──────────────────────────────────────────────────────────────

    public async Task<string> GetConfigAsync(string projectPath, string key, bool isGlobal = false)
    {
        var args = isGlobal
            ? new[] { "config", "--global", "--get", key }
            : new[] { "config", "--local", "--get", key };
        var result = await GitProcessExecutor.RunAsync(projectPath, args);
        return result.IsSuccess ? result.Data.Trim() : string.Empty;
    }

    public async Task<Result> SetConfigAsync(string projectPath, string key, string value, bool isGlobal = false)
    {
        var args = isGlobal
            ? new[] { "config", "--global", key, value }
            : new[] { "config", "--local", key, value };
        var result = await GitProcessExecutor.RunAsync(projectPath, args);
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> UnsetConfigAsync(string projectPath, string key, bool isGlobal = false)
    {
        var args = isGlobal
            ? new[] { "config", "--global", "--unset", key }
            : new[] { "config", "--local", "--unset", key };
        var result = await GitProcessExecutor.RunAsync(projectPath, args);
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    // ─── Metadata ─────────────────────────────────────────────────────────────

    public async Task<Result<GitRepositoryMetadata>> GetMetadataAsync(string projectPath)
    {
        try
        {
            var branchTask = GetCurrentBranchAsync(projectPath);
            var remoteUrlTask = GetRemoteUrlAsync(projectPath);
            var aheadBehindTask = GetAheadBehindCountAsync(projectPath);

            var isDetachedResult = await Git(projectPath, "symbolic-ref", "-q", "HEAD");
            bool isDetached = !isDetachedResult.IsSuccess;

            await Task.WhenAll(branchTask, remoteUrlTask, aheadBehindTask);

            var userName = await GetConfigAsync(projectPath, "user.name");
            var userEmail = await GetConfigAsync(projectPath, "user.email");
            var (ahead, behind) = await aheadBehindTask;
            var currentBranch = await branchTask;

            return Result<GitRepositoryMetadata>.Success(new GitRepositoryMetadata
            {
                CurrentBranch = currentBranch,
                RemoteUrl = await remoteUrlTask,
                Ahead = ahead,
                Behind = behind,
                UserName = userName,
                UserEmail = userEmail,
                IsDetached = isDetached,
                DetachedHeadSha = isDetached ? currentBranch : null,
                HasUpstream = ahead > 0 || behind > 0 || await HasUpstreamAsync(projectPath, currentBranch)
            });
        }
        catch (Exception ex)
        {
            return Result<GitRepositoryMetadata>.Fail(ex.Message);
        }
    }

    // ─── Ciclo de vida ───────────────────────────────────────────────────────

    public async Task<Result> CloneAsync(string url, string destinationPath)
    {
        var result = await GitProcessExecutor.RunAsync(Directory.GetCurrentDirectory(), 300_000, "clone", url, destinationPath);
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> InitAsync(string projectPath)
    {
        var result = await Git(projectPath, "init");
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> AddRemoteAsync(string projectPath, string name, string url)
    {
        var result = await Git(projectPath, "remote", "add", name, url);
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    // ─── Tags ────────────────────────────────────────────────────────────────

    public async Task<Result> CreateTagAsync(string projectPath, string tagName, string message, string commitHash = null)
    {
        var args = commitHash != null
            ? new[] { "tag", "-a", tagName, commitHash, "-m", message }
            : new[] { "tag", "-a", tagName, "-m", message };
        var result = await GitProcessExecutor.RunAsync(projectPath, args);
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> DeleteTagLocalAsync(string projectPath, string tagName)
    {
        var result = await Git(projectPath, "tag", "-d", tagName);
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> DeleteTagRemoteAsync(string projectPath, string tagName)
    {
        var remoteUrl = await GetRemoteUrlAsync(projectPath);
        var result = await ExecuteAuthenticatedAsync<string>(projectPath, remoteUrl, async (_, env) =>
        {
            var r = await GitProcessExecutor.RunAsync(projectPath, 120_000, env, "push", "origin", "--delete", tagName);
            return r.IsSuccess ? Result<string?>.Success(r.Data) : Result<string?>.Fail(r.Error);
        });
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<Result> PushTagAsync(string projectPath, string tagName)
    {
        var remoteUrl = await GetRemoteUrlAsync(projectPath);
        var result = await ExecuteAuthenticatedAsync<string>(projectPath, remoteUrl, async (_, env) =>
        {
            var r = await GitProcessExecutor.RunAsync(projectPath, 120_000, env, "push", "origin", tagName);
            return r.IsSuccess ? Result<string?>.Success(r.Data) : Result<string?>.Fail(r.Error);
        });
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    public async Task<IEnumerable<GitTagItem>> GetTagsAsync(string projectPath)
    {
        var result = await Git(projectPath, "tag", "--list", "--sort=-creatordate",
            "--format=%(refname:short)|%(objectname:short)|%(creatordate:unix)|%(subject)");
        if (!result.IsSuccess) return Enumerable.Empty<GitTagItem>();

        var tags = new List<GitTagItem>();
        foreach (var line in result.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            if (parts.Length < 1) continue;
            tags.Add(new GitTagItem
            {
                TagName = parts[0],
                CommitHash = parts.Length > 1 ? parts[1] : string.Empty,
                TagMessage = parts.Length > 3 ? parts[3] : string.Empty,
                RelativeDate = parts.Length > 2 && long.TryParse(parts[2], out var ts)
                    ? DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime.ToShortDateString()
                    : "Unknown"
            });
        }
        if (tags.Any()) tags.First().IsLatest = true;
        return tags;
    }

    public async Task<Dictionary<string, List<string>>> GetTagCommitMapAsync(string projectPath)
    {
        var result = await Git(projectPath, "tag", "--list", "--format=%(objectname)|%(refname:short)");
        var map = new Dictionary<string, List<string>>();
        if (!result.IsSuccess) return map;
        foreach (var line in result.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            if (parts.Length < 2) continue;
            var hash = parts[0].Trim();
            if (!map.ContainsKey(hash)) map[hash] = new List<string>();
            map[hash].Add(parts[1].Trim());
        }
        return map;
    }

    // ─── Conflictos ──────────────────────────────────────────────────────────

    public async Task<IEnumerable<GitConflict>> GetMergeConflictsAsync(string projectPath)
    {
        var result = await Git(projectPath, "diff", "--name-only", "--diff-filter=U");
        if (!result.IsSuccess) return Enumerable.Empty<GitConflict>();

        var conflicts = new List<GitConflict>();
        foreach (var f in result.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var filePath = f.Trim();
            var absolutePath = Path.Combine(projectPath, filePath);
            var gc = new GitConflict { FilePath = filePath.Replace('/', Path.DirectorySeparatorChar) };

            if (File.Exists(absolutePath))
            {
                var lines = await File.ReadAllLinesAsync(absolutePath);
                gc.Blocks = ParseConflictBlocks(lines);
            }
            conflicts.Add(gc);
        }
        return conflicts;
    }

    private List<ConflictBlock> ParseConflictBlocks(string[] lines)
    {
        var blocks = new List<ConflictBlock>();
        ConflictBlock? currentBlock = null;
        bool inLocal = false;
        bool inIncoming = false;
        var localSb = new System.Text.StringBuilder();
        var incomingSb = new System.Text.StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.StartsWith("<<<<<<<"))
            {
                currentBlock = new ConflictBlock { StartLine = i + 1 };
                inLocal = true;
                inIncoming = false;
                localSb.Clear();
                incomingSb.Clear();
            }
            else if (line.StartsWith("=======") && currentBlock != null)
            {
                inLocal = false;
                inIncoming = true;
            }
            else if (line.StartsWith(">>>>>>>") && currentBlock != null)
            {
                currentBlock.EndLine = i + 1;
                currentBlock.LocalContent = localSb.ToString().TrimEnd('\r', '\n');
                currentBlock.IncomingContent = incomingSb.ToString().TrimEnd('\r', '\n');
                blocks.Add(currentBlock);
                currentBlock = null;
                inLocal = false;
                inIncoming = false;
            }
            else if (currentBlock != null)
            {
                if (inLocal) localSb.AppendLine(line);
                else if (inIncoming) incomingSb.AppendLine(line);
            }
        }
        return blocks;
    }

    public async Task<Result> ResolveConflictAsync(string projectPath, string filePath, string resolvedContent)
    {
        await File.WriteAllTextAsync(Path.Combine(projectPath, filePath), resolvedContent);
        var result = await Git(projectPath, "add", "--", filePath.Replace(Path.DirectorySeparatorChar, '/'));
        return result.IsSuccess ? Result.Success() : Result.Fail(result.Error);
    }

    // ─── Misc ────────────────────────────────────────────────────────────────

    public bool IsGitInstalled() => GitBinaryLocator.IsGitAvailable();
}
