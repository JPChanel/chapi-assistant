using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Domain.Enums;
using Chapi.Domain.Interfaces;
using Chapi.Domain.Models;
using LibGit2Sharp;
using System.Linq;

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
            Username = cred.Value.username,
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

                foreach (var item in repo.RetrieveStatus(new StatusOptions { IncludeIgnored = false }))
                {
                    ChangeStatus status = ChangeStatus.Modified; // Default
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
                        changes.Add(new FileChange
                        {
                            FilePath = item.FilePath,
                            Status = status,
                        });
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

    public async Task<string> ExecuteGitCommandAsync(string projectPath, string command)
    {
        // No podemos ejecutar comandos arbitrarios de texto con LibGit2Sharp
        // Esta función debe ser removida o adaptada para usar métodos específicos
        // Por ahora lanzamos error indicando que se requieren métodos nativos
        return await Task.FromResult("Error: LibGit2Sharp no soporta ejecución de comandos de texto arbitrarios. Usa métodos específicos.");
    }

    // Stashes y Tags requieren implementación específica con LibGit2
    public Task<IEnumerable<GitStash>> ListStashesAsync(string projectPath) => Task.FromResult(Enumerable.Empty<GitStash>());
    public Task<Dictionary<string, char>> GetFileStatusesForStashAsync(string projectPath, string stashName) => Task.FromResult(new Dictionary<string, char>());
    public Task<Result> CreateTagAsync(string projectPath, string tagName, string message, string commitHash = null) => Task.FromResult(Result.Success());
    public Task<Result> DeleteTagLocalAsync(string projectPath, string tagName) => Task.FromResult(Result.Success());
    public Task<IEnumerable<GitTagItem>> GetTagsAsync(string projectPath) => Task.FromResult(Enumerable.Empty<GitTagItem>());
    public Task<Dictionary<string, List<string>>> GetTagCommitMapAsync(string projectPath) => Task.FromResult(new Dictionary<string, List<string>>());
    
    // History details
    public Task<IEnumerable<string>> GetFilesChangedInCommitAsync(string projectPath, string hash) => Task.FromResult(Enumerable.Empty<string>());
    public Task<string> GetFileContentAtCommitAsync(string projectPath, string file, string hash) => Task.FromResult(string.Empty);
    public Task<string> GetCommitParentHashAsync(string projectPath, string hash) => Task.FromResult(string.Empty);
    public Task<Dictionary<string, (int Additions, int Deletions)>> GetCommitNumStatAsync(string projectPath, string hash) => Task.FromResult(new Dictionary<string, (int, int)>());
    public Task<string> GetFileContentAsync(string projectPath, string revision, string filePath) => Task.FromResult(string.Empty);

    #endregion
}
