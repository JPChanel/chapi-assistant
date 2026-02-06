using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Domain.Enums;
using Chapi.Domain.Interfaces;
using Chapi.Domain.Models;
using LibGit2Sharp;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Chapi.Infrastructure.Git;

/// <summary>
/// Implementación de repositorio Git usando LibGit2Sharp (autónomo, no requiere git instalado).
/// </summary>
public class LibGit2SharpRepository : IGitRepository
{
    private readonly IGitAuthProviderFactory _authFactory;
    private readonly ICredentialStorageService _credentialStorage;

    public LibGit2SharpRepository(
        IGitAuthProviderFactory authFactory,
        ICredentialStorageService credentialStorage)
    {
        _authFactory = authFactory;
        _credentialStorage = credentialStorage;
    }

    // Método auxiliar para obtener credenciales
    private async Task<Credentials?> GetCredentialsAsync(string remoteUrl)
    {
        var provider = _authFactory.DetectProviderFromUrl(remoteUrl);
        if (provider == GitProvider.Unknown) return null;

        var cred = await _credentialStorage.GetCredentialAsync(provider.ToString());
        if (!cred.HasValue) return null;

        return new UsernamePasswordCredentials
        {
            Username = provider == GitProvider.GitLab ? "oauth2" : cred.Value.username,
            Password = cred.Value.token
        };
    }

    #region Commits

    public async Task<Result<GitCommit>> CommitAsync(string projectPath, string message, IEnumerable<string> files)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                
                // Stage files
                Commands.Stage(repo, files);

                // Create signature
                var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
                if (signature == null)
                    return Result<GitCommit>.Fail("No se ha configurado usuario ni correo en git config");

                // Commit
                var commit = repo.Commit(message, signature, signature);

                return Result<GitCommit>.Success(new GitCommit
                {
                    Hash = commit.Sha,
                    Message = commit.MessageShort,
                    Author = commit.Author.Name,
                    Date = commit.Author.When.DateTime
                });
            }
            catch (Exception ex)
            {
                return Result<GitCommit>.Fail($"Error al hacer commit: {ex.Message}");
            }
        });
    }

    public async Task<IEnumerable<GitCommit>> GetCommitsAsync(string projectPath, int limit)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var commits = repo.Commits.Take(limit);
                var result = new List<GitCommit>();

                foreach (var c in commits)
                {
                    var commit = new GitCommit
                    {
                        Hash = c.Sha,
                        Message = c.MessageShort,
                        Author = c.Author.Name,
                        Date = c.Author.When.DateTime,
                        Tags = new List<string>()
                    };

                    // Agregar tags si existen para este commit
                    foreach (var tag in repo.Tags)
                    {
                        if (tag.Target.Sha == c.Sha)
                        {
                            commit.Tags.Add(tag.FriendlyName);
                        }
                    }

                    result.Add(commit);
                }

                return result;
            }
            catch
            {
                return Enumerable.Empty<GitCommit>();
            }
        });
    }

    public async Task<HashSet<string>> GetUnpushedCommitsAsync(string projectPath, string branch)
    {
        return await Task.Run(async () =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var localBranch = repo.Branches[branch];
                var trackingBranch = localBranch.TrackedBranch;

                if (trackingBranch == null)
                    return new HashSet<string>();
                
                // Obtener commits locales que no están en el remoto
                var filter = new CommitFilter
                {
                    SortBy = CommitSortStrategies.Topological,
                    IncludeReachableFrom = localBranch.Tip,
                    ExcludeReachableFrom = trackingBranch.Tip
                };

                var unpushed = repo.Commits.QueryBy(filter).Select(c => c.Sha).ToHashSet();
                return unpushed;
            }
            catch
            {
                return new HashSet<string>();
            }
        });
    }

    #endregion

    #region Changes

    public async Task<IEnumerable<FileChange>> GetChangesAsync(string projectPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var changes = new List<FileChange>();

                // 1. Obtener estados básicos (untracked, etc.)
                var statusOptions = new StatusOptions { IncludeIgnored = false };
                var repoStatus = repo.RetrieveStatus(statusOptions);

                // 2. Obtener diff para additions/deletions (HEAD vs WorkingDirectory)
                Patch diff = null;
                if (!repo.Info.IsHeadUnborn)
                {
                    diff = repo.Diff.Compare<Patch>(repo.Head.Tip.Tree, DiffTargets.WorkingDirectory);
                }

                foreach (var item in repoStatus)
                {
                    ChangeStatus status = ChangeStatus.Modified;
                    bool isKnown = false;

                    // Mapeo manual de estados de LibGit2Sharp a nuestro dominio
                    if (item.State.HasFlag(LibGit2Sharp.FileStatus.NewInIndex) || item.State.HasFlag(LibGit2Sharp.FileStatus.NewInWorkdir))
                    {
                        status = ChangeStatus.Added;
                        isKnown = true;
                    }
                    else if (item.State.HasFlag(LibGit2Sharp.FileStatus.ModifiedInIndex) || item.State.HasFlag(LibGit2Sharp.FileStatus.ModifiedInWorkdir))
                    {
                        status = ChangeStatus.Modified;
                        isKnown = true;
                    }
                    else if (item.State.HasFlag(LibGit2Sharp.FileStatus.DeletedFromIndex) || item.State.HasFlag(LibGit2Sharp.FileStatus.DeletedFromWorkdir))
                    {
                        status = ChangeStatus.Deleted;
                        isKnown = true;
                    }
                    else if (item.State.HasFlag(LibGit2Sharp.FileStatus.RenamedInIndex) || item.State.HasFlag(LibGit2Sharp.FileStatus.RenamedInWorkdir))
                    {
                        status = ChangeStatus.Renamed;
                        isKnown = true;
                    }

                    if (isKnown && !item.State.HasFlag(LibGit2Sharp.FileStatus.Ignored))
                    {
                        var change = new FileChange
                        {
                            FilePath = item.FilePath.Replace('/', Path.DirectorySeparatorChar),
                            Status = status,
                        };

                        // Buscar estadísticas en el diff
                        if (diff != null)
                        {
                            var patchEntry = diff[item.FilePath];
                            if (patchEntry != null)
                            {
                                change.Additions = patchEntry.LinesAdded;
                                change.Deletions = patchEntry.LinesDeleted;
                            }
                        }

                        changes.Add(change);
                    }
                }

                return changes;
            }
            catch
            {
                return Enumerable.Empty<FileChange>();
            }
        });
    }

    public async Task<Result> StageFilesAsync(string projectPath, IEnumerable<string> files)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                Commands.Stage(repo, files);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Fail($"Error staging: {ex.Message}");
            }
        });
    }

    public async Task<Result> UnstageFilesAsync(string projectPath, IEnumerable<string> files)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                Commands.Unstage(repo, files);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Fail($"Error unstaging: {ex.Message}");
            }
        });
    }

    public async Task<Result> DiscardChangesAsync(string projectPath, IEnumerable<string>? files = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var options = new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force };

                if (files == null || !files.Any())
                {
                    // Descartar cambios en archivos rastreados
                    repo.CheckoutPaths("HEAD", new[] { "*" }, options);

                    // Limpiar archivos no rastreados (Manualmente ya que LibGit2Sharp no tiene Clean)
                    var status = repo.RetrieveStatus(new StatusOptions { IncludeIgnored = false });
                    foreach (var entry in status.Where(s => s.State == LibGit2Sharp.FileStatus.NewInWorkdir))
                    {
                        var fullPath = Path.Combine(projectPath, entry.FilePath);
                        if (File.Exists(fullPath)) File.Delete(fullPath);
                        else if (Directory.Exists(fullPath)) Directory.Delete(fullPath, true);
                    }
                }
                else
                {
                    // Descartar cambios en archivos específicos
                    repo.CheckoutPaths("HEAD", files, options);

                    // Eliminar archivos específicos si son nuevos (untracked)
                    foreach (var file in files)
                    {
                        var state = repo.RetrieveStatus(file);
                        if (state == LibGit2Sharp.FileStatus.NewInWorkdir)
                        {
                            var fullPath = Path.Combine(projectPath, file);
                            if (File.Exists(fullPath)) File.Delete(fullPath);
                            else if (Directory.Exists(fullPath)) Directory.Delete(fullPath, true);
                        }
                    }
                }
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Fail($"Error al descartar cambios: {ex.Message}");
            }
        });
    }

    #endregion

    #region Branches

    public async Task<IEnumerable<string>> GetBranchesAsync(string projectPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                return repo.Branches.Where(b => !b.IsRemote).Select(b => b.FriendlyName).ToList();
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        });
    }

    public async Task<string> GetCurrentBranchAsync(string projectPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                return repo.Head.FriendlyName;
            }
            catch
            {
                return string.Empty;
            }
        });
    }

    public async Task<Result> SwitchBranchAsync(string projectPath, string branchName)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var branch = repo.Branches[branchName];
                if (branch == null)
                    return Result.Fail($"Rama {branchName} no existe");

                Commands.Checkout(repo, branch);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Fail($"Error cambiando rama: {ex.Message}");
            }
        });
    }

    public async Task<Result> CreateBranchAsync(string projectPath, string branchName, string? fromCommitOrBranch = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var target = string.IsNullOrEmpty(fromCommitOrBranch) 
                    ? repo.Head.Tip 
                    : (repo.Branches[fromCommitOrBranch]?.Tip ?? repo.Lookup<Commit>(fromCommitOrBranch));
                
                if (target == null) return Result.Fail("Origen de rama no encontrado");

                repo.Branches.Add(branchName, target);
                return Result.Success();
            }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        });
    }

    public async Task<Result> DeleteBranchAsync(string projectPath, string branchName, bool force = false)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var branch = repo.Branches[branchName];
                if (branch == null) return Result.Fail("Rama no encontrada");
                
                repo.Branches.Remove(branch);
                return Result.Success();
            }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        });
    }

    public async Task<Result> ResetAsync(string projectPath, string target, Chapi.Domain.Enums.ResetMode mode)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var commit = repo.Lookup<Commit>(target);
                
                // Si el target termina en "^", buscamos el padre
                if (target.EndsWith("^") && commit == null)
                {
                    var baseHash = target.TrimEnd('^');
                    var baseCommit = repo.Lookup<Commit>(baseHash);
                    commit = baseCommit?.Parents.FirstOrDefault();
                }

                if (commit == null) return Result.Fail("Commit no encontrado: " + target);

                LibGit2Sharp.ResetMode libMode = mode switch {
                    Chapi.Domain.Enums.ResetMode.Soft => LibGit2Sharp.ResetMode.Soft,
                    Chapi.Domain.Enums.ResetMode.Mixed => LibGit2Sharp.ResetMode.Mixed,
                    Chapi.Domain.Enums.ResetMode.Hard => LibGit2Sharp.ResetMode.Hard,
                    _ => LibGit2Sharp.ResetMode.Soft
                };

                repo.Reset(libMode, commit);
                return Result.Success();
            }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        });
    }

    public async Task<Result> RestoreFileFromStashAsync(string projectPath, string stashName, string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var match = System.Text.RegularExpressions.Regex.Match(stashName, @"\{(\d+)\}");
                if (!match.Success) return Result.Fail("Nombre de stash inválido");
                int index = int.Parse(match.Groups[1].Value);

                var stash = repo.Stashes.ElementAtOrDefault(index);
                if (stash == null) return Result.Fail("Stash no encontrado");

                var options = new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force };
                repo.CheckoutPaths(stash.WorkTree.Sha, new[] { filePath.Replace(Path.DirectorySeparatorChar, '/') }, options);
                return Result.Success();
            }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        });
    }

    public async Task<string> GetDiffAsync(string projectPath, string file, string? revision = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var path = file.Replace(Path.DirectorySeparatorChar, '/');
                Patch diff;
                
                if (string.IsNullOrEmpty(revision) || revision == "HEAD")
                {
                    diff = repo.Diff.Compare<Patch>(repo.Head.Tip.Tree, DiffTargets.WorkingDirectory, new[] { path });
                }
                else
                {
                    var commit = repo.Lookup<Commit>(revision);
                    var parent = commit?.Parents.FirstOrDefault();
                    if (parent == null) return string.Empty;
                    diff = repo.Diff.Compare<Patch>(parent.Tree, commit.Tree, new[] { path });
                }
                
                return diff.Content;
            }
            catch { return string.Empty; }
        });
    }

    public async Task<string> GetConfigAsync(string key, bool global = false)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (global)
                {
                    // Configuración global desde .gitconfig
                    string globalConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gitconfig");
                    if (!File.Exists(globalConfigPath)) return string.Empty;
                    
                    using var config = global::LibGit2Sharp.Configuration.BuildFrom(globalConfigPath);
                    return config.Get<string>(key)?.Value ?? string.Empty;
                }
                else
                {
                    // Configuración local del repositorio (necesita projectPath)
                    // Como no tenemos projectPath aquí, intentamos global
                    string globalConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gitconfig");
                    if (!File.Exists(globalConfigPath)) return string.Empty;
                    
                    using var config = global::LibGit2Sharp.Configuration.BuildFrom(globalConfigPath);
                    return config.Get<string>(key)?.Value ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en GetConfigAsync: {ex.Message}");
                return string.Empty;
            }
        });
    }

    public async Task<Result> SetConfigAsync(string key, string value, bool global = false)
    {
        return await Task.Run(() =>
        {
            try
            {
                string globalConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gitconfig");
                using var config = global ? global::LibGit2Sharp.Configuration.BuildFrom(globalConfigPath) : null;
                if (config == null) return Result.Fail("Configuración no disponible");
                config.Set(key, value);
                return Result.Success();
            }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        });
    }

    public async Task<Result> UnsetConfigAsync(string key, bool global = false)
    {
        return await Task.Run(() =>
        {
            try
            {
                string globalConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gitconfig");
                using var config = global ? global::LibGit2Sharp.Configuration.BuildFrom(globalConfigPath) : null;
                if (config == null) return Result.Fail("Configuración no disponible");
                config.Unset(key);
                return Result.Success();
            }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        });
    }

    #endregion

    #region Remote

    public async Task<Result> PushAsync(string projectPath, string branch)
    {
        try
        {
            // Detectar remote URL sin abrir repo aún (para evitar bloqueo)
            string remoteUrl = "";
            using (var repoCheck = new Repository(projectPath))
            {
                remoteUrl = repoCheck.Network.Remotes["origin"]?.Url ?? "";
            }

            var credentials = await GetCredentialsAsync(remoteUrl);
            if (credentials == null)
                return Result.Fail("No hay credenciales autenticadas. Por favor inicia sesión.");

            return await Task.Run(() =>
            {
                try
                {
                    using var repo = new Repository(projectPath);
                    var localBranch = repo.Branches[branch];
                    
                    var options = new PushOptions
                    {
                        CredentialsProvider = (_url, _user, _type) => credentials
                    };

                    repo.Network.Push(localBranch, options);
                    return Result.Success();
                }
                catch (Exception ex)
                {
                    return Result.Fail($"Error push: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error autenticación: {ex.Message}");
        }
    }

    public async Task<Result> PullAsync(string projectPath, string branch)
    {
        try
        {
            string remoteUrl = "";
            using (var repoCheck = new Repository(projectPath))
            {
                remoteUrl = repoCheck.Network.Remotes["origin"]?.Url ?? "";
            }

            var credentials = await GetCredentialsAsync(remoteUrl);
            if (credentials == null)
                return Result.Fail("No hay credenciales autenticadas.");

            return await Task.Run(() =>
            {
                try
                {
                    using var repo = new Repository(projectPath);
                    var signature = repo.Config.BuildSignature(DateTimeOffset.Now);

                    var options = new PullOptions
                    {
                        FetchOptions = new FetchOptions
                        {
                            CredentialsProvider = (_url, _user, _type) => credentials
                        }
                    };

                    var result = Commands.Pull(repo, signature, options);
                    
                    if (result.Status == MergeStatus.Conflicts)
                        return Result.Fail("Conflictos al hacer pull");

                    return Result.Success();
                }
                catch (Exception ex)
                {
                    return Result.Fail($"Error pull: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error pull: {ex.Message}");
        }
    }

    public async Task<Result> FetchAsync(string projectPath)
    {
        try
        {
            string remoteUrl = "";
            using (var repoCheck = new Repository(projectPath))
            {
                remoteUrl = repoCheck.Network.Remotes["origin"]?.Url ?? "";
            }

            var credentials = await GetCredentialsAsync(remoteUrl);
            if (credentials == null)
                return Result.Fail("No hay credenciales autenticadas.");

            return await Task.Run(() =>
            {
                try
                {
                    using var repo = new Repository(projectPath);
                    var remote = repo.Network.Remotes["origin"];
                    
                    var options = new FetchOptions
                    {
                        CredentialsProvider = (_url, _user, _type) => credentials
                    };

                    Commands.Fetch(repo, remote.Name, remote.FetchRefSpecs.Select(x => x.Specification), options, "");
                    return Result.Success();
                }
                catch (Exception ex)
                {
                    return Result.Fail($"Error fetch: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error fetch: {ex.Message}");
        }
    }

    public async Task<(int Ahead, int Behind)> GetAheadBehindCountAsync(string projectPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var branch = repo.Head;
                var tracking = branch.TrackedBranch;

                if (tracking == null) return (0, 0);

                var div = repo.ObjectDatabase.CalculateHistoryDivergence(branch.Tip, tracking.Tip);
                return (div.AheadBy ?? 0, div.BehindBy ?? 0);
            }
            catch
            {
                return (0, 0);
            }
        });
    }

    public async Task<string> GetRemoteUrlAsync(string projectPath, string remoteName = "origin")
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var remote = repo.Network.Remotes[remoteName];
                return remote?.Url ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        });
    }

    public async Task<Result> SetRemoteUrlAsync(string projectPath, string remoteName, string url)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                repo.Network.Remotes.Update(remoteName, r => r.Url = url);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Fail($"Error al actualizar remoto: {ex.Message}");
            }
        });
    }

    #endregion

    #region Lifecycle

    public async Task<Result> CloneAsync(string url, string destinationPath)
    {
        try
        {
            var credentials = await GetCredentialsAsync(url);
            
            return await Task.Run(() =>
            {
                try
                {
                    var options = new CloneOptions();
                    if (credentials != null)
                    {
                        options.FetchOptions.CredentialsProvider = (_url, _user, _type) => credentials;
                    }

                    Repository.Clone(url, destinationPath, options);
                    return Result.Success();
                }
                catch (Exception ex)
                {
                    return Result.Fail($"Error cloning: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error cloning: {ex.Message}");
        }
    }

    public async Task<Result> InitAsync(string projectPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                Repository.Init(projectPath);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Fail($"Error init: {ex.Message}");
            }
        });
    }

    public async Task<Result> AddRemoteAsync(string projectPath, string name, string url)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                repo.Network.Remotes.Add(name, url);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Fail($"Error adding remote: {ex.Message}");
            }
        });
    }

    #endregion

    #region Misc

    public bool IsGitInstalled()
    {
        // Con LibGit2Sharp, "git" está embebido en la app.
        // Siempre retornamos true porque nosotros somos el git provider.
        return true; 
    }

    public async Task<bool> HasUpstreamAsync(string projectPath, string branchName)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                return repo.Branches[branchName]?.TrackedBranch != null;
            }
            catch
            {
                return false;
            }
        });
    }



    // Stash
    public async Task<Result> StashChangesAsync(string projectPath, string message, IEnumerable<string>? files = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
                if (signature == null)
                    return Result.Fail("No se ha configurado usuario ni correo en git config");

                // Nota: LibGit2Sharp no soporta nativamente stashear archivos individuales en Stashes.Add
                // Se stashea todo el directorio de trabajo (comportamiento estándar de GUI).
                repo.Stashes.Add(signature, message, StashModifiers.IncludeUntracked);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Fail($"Error stash: {ex.Message}");
            }
        });
    }

    public async Task<IEnumerable<GitStash>> ListStashesAsync(string projectPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var result = new List<GitStash>();
                int index = 0;
                foreach (var stash in repo.Stashes)
                {
                    // Intentar extraer la rama del mensaje (ej: "WIP on main: ...")
                    string branch = "Unknown";
                    var match = System.Text.RegularExpressions.Regex.Match(stash.Message, @"on ([^:]+):");
                    if (match.Success) 
                        branch = match.Groups[1].Value;

                    // Calcular cantidad de archivos (Diff entre stash commit y su primer padre)
                    int fileCount = 0;
                    try
                    {
                        var stashCommit = stash.WorkTree;
                        var parent = stashCommit.Parents.FirstOrDefault();
                        if (parent != null)
                        {
                            var diff = repo.Diff.Compare<TreeChanges>(parent.Tree, stashCommit.Tree);
                            fileCount = diff.Count();
                        }
                    }
                    catch { }

                    result.Add(new GitStash(
                        Name: $"stash@{{{index}}}",
                        Branch: branch,
                        Message: stash.Message,
                        FileCount: fileCount
                    ));
                    index++;
                }
                return result;
            }
            catch
            {
                return Enumerable.Empty<GitStash>();
            }
        });
    }

    public async Task<Dictionary<string, char>> GetFileStatusesForStashAsync(string projectPath, string stashName)
    {
        return await Task.Run(() =>
        {
            var statuses = new Dictionary<string, char>();
            try
            {
                using var repo = new Repository(projectPath);
                var match = System.Text.RegularExpressions.Regex.Match(stashName, @"\{(\d+)\}");
                if (!match.Success) return statuses;
                int index = int.Parse(match.Groups[1].Value);
                
                var stash = repo.Stashes.ElementAtOrDefault(index);
                if (stash == null) return statuses;

                var stashCommit = stash.WorkTree;
                var parent = stashCommit.Parents.FirstOrDefault();
                if (parent != null)
                {
                    var diff = repo.Diff.Compare<TreeChanges>(parent.Tree, stashCommit.Tree);
                    foreach (var change in diff)
                    {
                        char status = 'M'; // Default modified
                        switch (change.Status)
                        {
                            case ChangeKind.Added: status = 'A'; break;
                            case ChangeKind.Deleted: status = 'D'; break;
                            case ChangeKind.Renamed: status = 'R'; break;
                        }
                        statuses[change.Path.Replace('/', Path.DirectorySeparatorChar)] = status;
                    }
                }
            }
            catch { }
            return statuses;
        });
    }

    public async Task<Result> StashPopAsync(string projectPath, int? index = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                int idx = index ?? 0;
                
                var options = new StashApplyOptions
                {
                    ApplyModifiers = StashApplyModifiers.ReinstateIndex
                };

                repo.Stashes.Apply(idx, options);
                repo.Stashes.Remove(idx);
                
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Fail($"Error al aplicar stash: {ex.Message}");
            }
        });
    }

    public async Task<Result> StashDropAsync(string projectPath, int index)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                repo.Stashes.Remove(index);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Fail($"Error al eliminar stash: {ex.Message}");
            }
        });
    }

    public async Task<Result> StashClearAsync(string projectPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                // No hay un Clear nativo en LibGit2Sharp, eliminamos uno a uno
                int count = repo.Stashes.Count();
                for (int i = 0; i < count; i++)
                {
                    repo.Stashes.Remove(0); // Siempre el 0 mientras haya stashes
                }
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Fail($"Error al limpiar stashes: {ex.Message}");
            }
        });
    }
    public async Task<Result> CreateTagAsync(string projectPath, string tagName, string message, string commitHash = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var target = string.IsNullOrEmpty(commitHash) ? repo.Head.Tip : repo.Lookup<Commit>(commitHash);
                if (target == null) return Result.Fail("Commit no encontrado");

                var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
                if (signature == null) return Result.Fail("Git config: User/Email no configurado");

                repo.Tags.Add(tagName, target, signature, message);
                return Result.Success();
            }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        });
    }

    public async Task<Result> DeleteTagLocalAsync(string projectPath, string tagName)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                repo.Tags.Remove(tagName);
                return Result.Success();
            }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        });
    }

    public async Task<IEnumerable<GitTagItem>> GetTagsAsync(string projectPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var result = new List<GitTagItem>();
                
                foreach (var tag in repo.Tags.OrderByDescending(t => (t.Annotation?.Tagger.When ?? (t.Target as Commit)?.Author.When)?.DateTime ?? DateTime.MinValue))
                {
                    var commit = tag.PeeledTarget as Commit;
                    var item = new GitTagItem
                    {
                        TagName = tag.FriendlyName,
                        CommitHash = tag.Target.Sha,
                        AuthorName = (tag.Annotation?.Tagger.Name) ?? commit?.Author.Name ?? "Unknown",
                        RelativeDate = (tag.Annotation?.Tagger.When ?? commit?.Author.When)?.DateTime.ToShortDateString() ?? "Unknown",
                        CommitMessage = commit?.MessageShort ?? "",
                        TagMessage = tag.Annotation?.Message ?? ""
                    };
                    result.Add(item);
                }

                if (result.Any()) result.First().IsLatest = true;
                return result;
            }
            catch { return Enumerable.Empty<GitTagItem>(); }
        });
    }

    public async Task<Dictionary<string, List<string>>> GetTagCommitMapAsync(string projectPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var map = new Dictionary<string, List<string>>();
                foreach (var tag in repo.Tags)
                {
                    var sha = tag.Target.Sha;
                    if (!map.ContainsKey(sha)) map[sha] = new List<string>();
                    if (!map[sha].Contains(tag.FriendlyName))
                        map[sha].Add(tag.FriendlyName);
                    
                    if (tag.Annotation != null)
                    {
                        var peeledSha = tag.PeeledTarget.Sha;
                        if (!map.ContainsKey(peeledSha)) map[peeledSha] = new List<string>();
                        if (!map[peeledSha].Contains(tag.FriendlyName))
                            map[peeledSha].Add(tag.FriendlyName);
                    }
                }
                return map;
            }
            catch { return new Dictionary<string, List<string>>(); }
        });
    }
    
    // History details
    public async Task<IEnumerable<string>> GetFilesChangedInCommitAsync(string projectPath, string hash)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var commit = repo.Lookup<Commit>(hash);
                if (commit == null) return Enumerable.Empty<string>();
                
                var parent = commit.Parents.FirstOrDefault();
                if (parent == null) 
                {
                    // Primer commit - listar todos los archivos
                    return commit.Tree.Select(e => e.Path).ToList();
                }

                var changes = repo.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree);
                return changes.Select(c => c.Path).ToList();
            }
            catch { return Enumerable.Empty<string>(); }
        });
    }

    public async Task<string> GetFileContentAtCommitAsync(string projectPath, string file, string hash)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var commit = repo.Lookup<Commit>(hash);
                if (commit == null) return string.Empty;

                var entry = commit[file.Replace('\\', '/')];
                if (entry?.Target is Blob blob)
                {
                    return blob.GetContentText();
                }
                return string.Empty;
            }
            catch { return string.Empty; }
        });
    }

    public async Task<string> GetCommitParentHashAsync(string projectPath, string hash)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                return repo.Lookup<Commit>(hash)?.Parents.FirstOrDefault()?.Sha ?? string.Empty;
            }
            catch { return string.Empty; }
        });
    }

    public async Task<Dictionary<string, (int Additions, int Deletions)>> GetCommitNumStatAsync(string projectPath, string hash)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                var commit = repo.Lookup<Commit>(hash);
                if (commit == null) return new Dictionary<string, (int, int)>();

                var parent = commit.Parents.FirstOrDefault();
                var result = new Dictionary<string, (int, int)>();

                if (parent == null) return result;

                var patch = repo.Diff.Compare<Patch>(parent.Tree, commit.Tree);
                foreach (var entry in patch)
                {
                    result[entry.Path.Replace('/', Path.DirectorySeparatorChar)] = (entry.LinesAdded, entry.LinesDeleted);
                }
                return result;
            }
            catch { return new Dictionary<string, (int, int)>(); }
        });
    }

    public async Task<string> GetFileContentAsync(string projectPath, string revision, string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                // Lookup<Commit> en LibGit2Sharp realiza el 'peeling' automáticamente desde tags o referencias
                var commit = repo.Lookup<Commit>(revision);
                if (commit == null) return string.Empty;

                var entry = commit[filePath.Replace('\\', '/')];
                if (entry?.Target is Blob blob)
                {
                    return blob.GetContentText();
                }
                return string.Empty;
            }
            catch { return string.Empty; }
        });
    }

    #endregion
}
