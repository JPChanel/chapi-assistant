using Chapi.Model;
using LibGit2Sharp;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Chapi.Helper.GitHelper;

public static class Git
{
    public record GitResult(bool Success, string Output);
    public record AheadBehindResult(int Ahead, int Behind);

    /// <summary>
    /// Comprueba si 'git.exe' está instalado y accesible en el PATH del sistema.
    /// </summary>
    public static bool IsGitInstalled()
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

    /// <summary>
    /// Ejecuta un comando git en un directorio y retorna la salida estándar (sincrónico interno pero awaitable).
    /// </summary>
    public static async Task<string> EjecutarGit(string command, string workingDirectory)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process == null) return string.Empty;

            var outputSb = new StringBuilder();
            var errorSb = new StringBuilder();

            // Leer salidas en paralelo
            var outTask = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = await process.StandardOutput.ReadLineAsync();
                    if (line != null) outputSb.AppendLine(line);
                }
            });

            var errTask = Task.Run(async () =>
            {
                while (!process.StandardError.EndOfStream)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    if (line != null) errorSb.AppendLine(line);
                }
            });

            await Task.WhenAll(outTask, errTask, process.WaitForExitAsync());

            var combined = outputSb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(combined) && errorSb.Length > 0)
                combined = errorSb.ToString().Trim();

            return combined;
        }
        catch (Exception ex)
        {
            return "error " + ex.Message;
        }
    }
    /// <summary>
    /// Ejecuta un comando Git y reporta el progreso en tiempo real.
    /// </summary>
    public static Task<string> EjecutarGitConProgreso(string command, string workingDirectory, Action<string> onProgress)
    {
        var outputBuilder = new StringBuilder();
        var tcs = new TaskCompletionSource<string>();

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = command,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true, 
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (sender, e) => {
            if (e.Data != null)
            {
                onProgress?.Invoke(e.Data); // Envía la línea de texto a la UI
                outputBuilder.AppendLine(e.Data);
            }
        };

        // Escuchamos en StandardError porque Git envía "Writing objects: 30%..." aquí
        process.ErrorDataReceived += (sender, e) => {
            if (e.Data != null)
            {
                onProgress?.Invoke(e.Data); // Envía la línea de progreso a la UI
                outputBuilder.AppendLine(e.Data); // También la guardamos en el log
            }
        };

        process.Exited += (sender, e) => {
            // Cuando el proceso termina, marcamos la Tarea como completada
            tcs.SetResult(outputBuilder.ToString());
            process.Dispose();
        };

        // Inicia el proceso y la escucha de eventos
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine(); // ¡Importante!

        return tcs.Task; 
    }
    public static async Task<GitResult> CloneRepo(string repoUrl, string destino)
    {
        try
        {
            // Ensure parent directory exists
            var parent = Path.GetDirectoryName(destino);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            string args = $"clone {repoUrl} \"{destino}\"";
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return new GitResult(false, "No se pudo iniciar proceso git.");

            var output = await process.StandardOutput.ReadToEndAsync();
            var err = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var combined = string.IsNullOrWhiteSpace(output) ? err : output;
            bool ok = process.ExitCode == 0;
            return new GitResult(ok, combined?.Trim() ?? string.Empty);
        }
        catch (Exception ex)
        {
            return new GitResult(false, ex.Message);
        }
    }

    public static void DeleteGitFolder(string rutaProyecto)
    {
        try
        {
            string gitFolder = Path.Combine(rutaProyecto, ".git");
            if (!Directory.Exists(gitFolder)) return;

            foreach (var file in Directory.GetFiles(gitFolder, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(gitFolder, true);
        }
        catch (Exception ex)
        {
            Msg.Assistant($"No se logró eliminar .git: {ex.Message}");
        }
    }

    public static async Task<GitResult> InitGit(string workingDirectory)
    {
        try
        {
            var res = await EjecutarGit("init", workingDirectory);
            // Si res viene vacío probablemente succeed; comprobar existencia de .git
            bool ok = Directory.Exists(Path.Combine(workingDirectory, ".git"));
            return new GitResult(ok, res);
        }
        catch (Exception ex)
        {
            return new GitResult(false, ex.Message);
        }
    }

    public static async Task<GitResult> AsociarRemoto(string url, string workingDirectory)
    {
        try
        {
            var res = await EjecutarGit($"remote add origin {url}", workingDirectory);
            // No necesariamente produce salida, asumimos éxito si no hay excepciones
            return new GitResult(true, res);
        }
        catch (Exception ex)
        {
            return new GitResult(false, ex.Message);
        }
    }
    public static List<string> GetBranches(string repositoryPath)
    {
        try
        {
            using (var repo = new Repository(repositoryPath))
            {
                // 1. Obtenemos un set con los nombres de todas las ramas locales
                var localBranchNames = repo.Branches
                                           .Where(b => !b.IsRemote)
                                           .Select(b => b.FriendlyName)
                                           .ToHashSet();

                // 2. Obtenemos las ramas remotas que CUMPLEN estas condiciones:
                var remoteBranchesToShow = repo.Branches
                    .Where(b => b.IsRemote &&
                                !b.FriendlyName.EndsWith("/HEAD") &&
                                !localBranchNames.Contains(b.FriendlyName.Replace(b.RemoteName + "/", "")))
                    .Select(b => b.FriendlyName)
                    .ToList();

                // 3. Juntamos las dos listas (locales primero)
                var allBranches = localBranchNames.ToList();
                allBranches.AddRange(remoteBranchesToShow);

                // 4. Devolvemos la lista ordenada
                return allBranches.OrderBy(name => name).ToList();
            }
        }
        catch (RepositoryNotFoundException)
        {
            // Handle the case where the path is not a valid repository.
            return new List<string>();
        }
    }

    public static List<string> FindGitRepositories(string basePath)
    {
        var repositories = new List<string>();
        var directories = Directory.GetDirectories(basePath);

        foreach (var dir in directories)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
            {
                repositories.Add(dir);
            }
        }
        return repositories;
    }

    /// <summary>
    /// Obtiene el contenido de un archivo en el commit HEAD (el último commit).
    /// </summary>
    public static async Task<string> GetFileContentAtHead(string filePath, string workingDirectory)
    {
        // Formatear la ruta para git (usar /)
        string gitPath = filePath.Replace("\\", "/");
        var result = await EjecutarGit($"show HEAD:\"{gitPath}\"", workingDirectory);

        // Si el archivo es nuevo (no en HEAD), 'git show' da un error.
        if (result.StartsWith("fatal:") || result.Contains("does not exist"))
        {
            return string.Empty; // Es un archivo nuevo
        }
        return result;
    }

    /// <summary>
    /// Obtiene la lista de todos los tags.
    /// </summary>
    public static async Task<List<GitTagItem>> GetTags(string workingDirectory)
    {
        var tags = new List<GitTagItem>();
        string args =
            "for-each-ref refs/tags --sort=-v:refname " +
            "--format=\"%(refname:short)|" +
            "%(if)%(*objectname)%(then)%(*objectname:short)%(else)%(objectname:short)%(end)|" +
            "%(if)%(*subject)%(then)%(*subject)%(else)%(subject)%(end)|" +
            "%(committerdate:relative)|" +
            "%(contents:subject)\"";

        var output = await EjecutarGit(args, workingDirectory);

        if (string.IsNullOrWhiteSpace(output) || output.Contains("fatal:"))
            return tags;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Trim().Split('|');
            if (parts.Length == 5)
            {
                tags.Add(new GitTagItem
                {
                    TagName = parts[0],
                    CommitHash = parts[1],
                    CommitMessage = parts[2],
                    RelativeDate = parts[3],
                    TagMessage = parts[4]
                });
            }
        }

        return tags;
    }

    /// <summary>
    /// Crea un nuevo tag anotado.
    /// </summary>
    public static async Task<GitResult> CreateTag(string tagName, string message, string workingDirectory, string commitHash = null)
    {
        // Validar que el mensaje no tenga comillas que rompan el comando
        message = message.Replace("\"", "'");
        string hashTarget = string.IsNullOrEmpty(commitHash) ? "" : $" {commitHash}";
        string args = $"tag -a \"{tagName}\" -m \"{message}\"{hashTarget}";

        var output = await EjecutarGit(args, workingDirectory);
        bool success = !output.Contains("fatal:") && !output.Contains("error:");

        return new GitResult(success, output);
    }

    /// <summary>
    /// Sube un tag específico al repositorio remoto (origin).
    /// </summary>
    public static async Task<GitResult> PushTag(string tagName, string workingDirectory)
    {
        string args = $"push origin \"{tagName}\"";
        var output = await EjecutarGit(args, workingDirectory);
        bool success = !output.Contains("fatal:") && !output.Contains("error:");
        return new GitResult(success, output);
    }

    /// <summary>
    /// Obtiene el contenido de un archivo en un commit específico.
    /// </summary>
    public static async Task<string> GetFileContentAtCommit(string filePath, string commitHash, string workingDirectory)
    {
        string gitPath = filePath.Replace("\\", "/");
        var result = await EjecutarGit($"show \"{commitHash}\":\"{gitPath}\"", workingDirectory);

        // Si el archivo no existía en ese commit (ej. fue añadido), 'git show' da error.
        if (result.StartsWith("fatal:") || result.Contains("does not exist"))
        {
            return string.Empty; // Es un archivo nuevo
        }
        return result;
    }
    /// <summary>
    /// Obtiene el contenido de un archivo en un "commit-ish" (commit, stash, head, etc).
    /// </summary>
    public static async Task<string> GetFileContentAtCommitish(string filePath, string commitish, string workingDirectory)
    {
        // Formatear la ruta para git (usar /)
        string gitPath = filePath.Replace("\\", "/");
        // commitish puede ser "HEAD", "stash@{0}", "stash@{0}^", "my-branch", "hash123"
        var result = await EjecutarGit($"show \"{commitish}\":\"{gitPath}\"", workingDirectory);

        // Si el archivo no existía en ese commit (ej. fue añadido), 'git show' da error.
        if (result.StartsWith("fatal:") || result.Contains("does not exist"))
        {
            return string.Empty; // Es un archivo nuevo
        }
        return result;
    }
    /// <summary>
    /// Obtiene el hash del primer padre de un commit.
    /// </summary>
    public static async Task<string> GetCommitParentHash(string commitHash, string workingDirectory)
    {
        // Obtenemos el hash del commit "padre" (el anterior)
        var output = await EjecutarGit($"rev-parse \"{commitHash}~1\"", workingDirectory);

        // Si es el primer commit, dará error.
        if (output.Contains("fatal"))
            return string.Empty;

        return output.Trim();
    }

    /// <summary>
    /// Obtiene la lista de archivos que cambiaron en un commit específico.
    /// </summary>
    public static async Task<List<string>> GetFilesChangedInCommit(string commitHash, string workingDirectory)
    {
        // --name-only solo nos da los nombres de los archivos
        string args = $"show --name-only --pretty=\"\" {commitHash} --";

        var output = await EjecutarGit(args, workingDirectory);

        if (string.IsNullOrWhiteSpace(output))
            return new List<string>();

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                     .Select(f => f.Trim().Replace('/', Path.DirectorySeparatorChar))
                     .ToList();
    }
    /// <summary>
    /// Obtiene una lista de hashes de commits que están en local pero no en origin.
    /// </summary>
    public static async Task<HashSet<string>> GetUnpushedCommitHashes(string branchName, string workingDirectory)
    {
        // ✅ --- AÑADIR TRY-CATCH ---
        try
        {
            string remoteBranch = $"origin/{branchName}";
            string args = $"log {remoteBranch}..{branchName} --pretty=format:\"%h\"";

            var output = await EjecutarGit(args, workingDirectory);

            if (string.IsNullOrWhiteSpace(output) || output.Contains("fatal: bad revision"))
            {
                return new HashSet<string>();
            }

            return new HashSet<string>(output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                             .Select(h => h.Trim()));
        }
        catch (Exception)
        {
            return new HashSet<string>();
        }
    }
    /// <summary>
    /// Obtiene un diccionario de [RutaDeArchivo, Estado (A, M, D, R)] para un commit.
    /// </summary>
    public static async Task<Dictionary<string, char>> GetFileStatusesForCommit(string commitHash, string workingDirectory)
    {
        var statuses = new Dictionary<string, char>();

        // diff-tree compara el commit con su primer padre
        string args = $"diff-tree --no-commit-id --name-status -r \"{commitHash}~1\" \"{commitHash}\"";

        var output = await EjecutarGit(args, workingDirectory);
        if (string.IsNullOrWhiteSpace(output) || output.Contains("fatal"))
        {
            string argsFirstCommit = $"diff-tree --no-commit-id --name-status -r --root \"{commitHash}\"";
            output = await EjecutarGit(argsFirstCommit, workingDirectory);
            if (string.IsNullOrWhiteSpace(output)) return statuses;
        }

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            // La salida es: [Status] \t [FilePath]
            var parts = line.Split('\t');
            if (parts.Length == 2)
            {
                char status = parts[0][0]; // A, M, D, R...
                string path = parts[1].Trim().Replace('/', Path.DirectorySeparatorChar);
                if (!statuses.ContainsKey(path))
                    statuses.Add(path, status);
            }
        }
        return statuses;
    }

    /// <summary>
    /// Representa una entrada en la lista de stashes.
    /// </summary>
    public record StashEntry(string Name, string Branch, string Message);

    /// <summary>
    /// Obtiene la lista de stashes disponibles.
    /// </summary>
    public static async Task<List<StashEntry>> ListStashes(string workingDirectory)
    {
        string args = "stash list --pretty=format:\"%gD|%gd|%gs\"";
        var output = await EjecutarGit(args, workingDirectory);
        var stashes = new List<StashEntry>();

        if (string.IsNullOrWhiteSpace(output))
            return stashes;

        // --- 💡 SOLUCIÓN: AÑADIR ESTA VALIDACIÓN ---
        if (output.Contains("fatal:") || output.Contains("error:"))
        {
            // Esto será capturado por el catch en LoadChangesAsync
            throw new Exception($"Error al listar stashes: {output}");
        }
        // --- FIN DE LA SOLUCIÓN ---

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Trim().Split('|');
            if (parts.Length == 3)
            {
                stashes.Add(new StashEntry(parts[0], parts[1].Replace("refs/heads/", ""), parts[2]));
            }
        }
        return stashes;
    }

    /// <summary>
    /// Aplica un stash específico (por defecto, el último: stash@{0}).
    /// </summary>
    /// <param name="stashName">Ej: "stash@{0}", "stash@{1}"</param>
    public static async Task<GitResult> ApplyStash(string stashName, string workingDirectory)
    {
        string args = $"stash apply {stashName}";
        var output = await EjecutarGit(args, workingDirectory);

        // 'git stash apply' puede tener conflictos o no hacer nada si no hay stash
        bool success = !output.Contains("fatal:") && !output.Contains("error:");
        // Nota: Podríamos parsear mejor la salida para detectar conflictos.

        return new GitResult(success, output);
    }

    /// <summary>
    /// Elimina un stash específico (por defecto, el último: stash@{0}).
    /// </summary>
    /// <param name="stashName">Ej: "stash@{0}", "stash@{1}"</param>
    public static async Task<GitResult> DropStash(string stashName, string workingDirectory)
    {
        string args = $"stash drop {stashName}";
        var output = await EjecutarGit(args, workingDirectory);
        bool success = output.Contains($"Dropped {stashName}");
        return new GitResult(success, output);
    }

    /// <summary>
    /// Obtiene un diccionario de [RutaDeArchivo, Estado (A, M, D)] para un stash.
    /// </summary>
    public static async Task<Dictionary<string, char>> GetFileStatusesForStash(string stashName, string workingDirectory)
    {
        var statuses = new Dictionary<string, char>();

        // Usamos 'stash show' para obtener la lista de archivos y su estado
        string args = $"stash show --name-status {stashName}";

        var output = await EjecutarGit(args, workingDirectory);
        if (string.IsNullOrWhiteSpace(output) || output.Contains("fatal"))
        {
            return statuses; // Devuelve vacío si hay error o no hay nada
        }

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            // La salida es: [Status] \t [FilePath]
            var parts = line.Split('\t');
            if (parts.Length == 2)
            {
                char status = parts[0][0]; // A, M, D...
                string path = parts[1].Trim().Replace('/', Path.DirectorySeparatorChar);
                if (!statuses.ContainsKey(path))
                    statuses.Add(path, status);
            }
        }
        return statuses;
    }
    /// <summary>
    /// Obtiene el recuento de commits pendientes (ahead/behind) 
    /// comparando HEAD con su rama upstream (@{u}).
    /// </summary>
    public static async Task<AheadBehindResult> GetAheadBehindCount(string workingDirectory)
    {
        // @{u} es el alias de git para la rama "upstream" configurada (ej: origin/main)
        string args = "rev-list --left-right --count HEAD...@{u}";
        var output = await EjecutarGit(args, workingDirectory);

        // Si no hay upstream configurado, 'output' contendrá "fatal:"
        if (string.IsNullOrWhiteSpace(output) || output.Contains("fatal:") || output.Contains("error:"))
        {
            return new AheadBehindResult(0, 0);
        }

        // El resultado es "ahead \t behind" (ej: "1\t2")
        var parts = output.Trim().Split('\t');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out int ahead) &&
            int.TryParse(parts[1], out int behind))
        {
            return new AheadBehindResult(ahead, behind);
        }

        return new AheadBehindResult(0, 0);
    }
    /// <summary>
    /// Obtiene el nombre de la rama actualmente activa.
    /// </summary>
    public static async Task<string> GetCurrentBranch(string workingDirectory)
    {
        // --show-current es la forma moderna y limpia
        var branchName = await EjecutarGit("branch --show-current", workingDirectory);

        // Validar que no sea un error o una cadena vacía
        if (!string.IsNullOrWhiteSpace(branchName) && !branchName.Contains("fatal:") && !branchName.Contains("error:"))
        {
            return branchName.Trim();
        }

        var branchOutput = await EjecutarGit("branch", workingDirectory);
        var line = branchOutput.Split('\n').FirstOrDefault(l => l.StartsWith("*"));

        if (line != null)
        {
            // El formato es "* (HEAD detached at ...)" o "* branch-name"
            if (line.Contains("HEAD detached at"))
            {
                return line.Substring(line.LastIndexOf(' ')).Trim(')'); // Devuelve el hash
            }
            return line.Substring(1).Trim(); // Devuelve el nombre de la rama
        }

        return "master"; // Fallback final
    }

    /// <summary>
    /// Elimina un tag localmente.
    /// </summary>
    public static async Task<GitResult> DeleteTagLocal(string tagName, string workingDirectory)
    {
        // -d es el flag para --delete
        string args = $"tag -d \"{tagName}\"";
        var output = await EjecutarGit(args, workingDirectory);
        // 'git tag -d' tiene éxito si el output contiene "Deleted tag..."
        bool success = !output.Contains("fatal:") && !output.Contains("error:");
        return new GitResult(success, output);
    }

    /// <summary>
    /// Elimina un tag del repositorio remoto (origin).
    /// </summary>
    public static async Task<GitResult> DeleteTagRemote(string tagName, string workingDirectory)
    {
        // Este es el comando moderno para eliminar un tag remoto
        string args = $"push origin --delete \"{tagName}\"";
        var output = await EjecutarGit(args, workingDirectory);
        // 'git push --delete' tiene éxito si el output contiene "[deleted]"
        bool success = !output.Contains("fatal:") && !output.Contains("error:");
        return new GitResult(success, output);
    }
    /// <summary>
    /// Devuelve un diccionario donde la clave es el HASH del commit
    /// y el valor es una lista de nombres de TAG que apuntan a él.
    /// </summary>
    public static async Task<Dictionary<string, List<string>>> GetTagCommitMap(string workingDirectory)
    {
        var map = new Dictionary<string, List<string>>();
        string args = "for-each-ref refs/tags --format=\"%(*objectname:short)%(objectname:short)%20%(refname:short)\"";


        var output = await EjecutarGit(args, workingDirectory);

        if (string.IsNullOrWhiteSpace(output) || output.Contains("fatal:"))
            return map;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Trim().Split(' ');
            if (parts.Length == 2)
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
    /// <summary>
    /// Elimina una rama local.
    /// </summary>
    public static async Task<GitResult> DeleteBranchLocal(string branchName, string workingDirectory)
    {

        string args = $"branch -d \"{branchName}\"";
        var output = await EjecutarGit(args, workingDirectory);
        bool success = !output.Contains("fatal:") && !output.Contains("error:");
        return new GitResult(success, output);
    }

    /// <summary>
    /// Elimina una rama del repositorio remoto (origin).
    /// </summary>
    public static async Task<GitResult> DeleteBranchRemote(string branchName, string workingDirectory)
    {
        // Comando para eliminar una rama remota
        string args = $"push origin --delete \"{branchName}\"";
        var output = await EjecutarGit(args, workingDirectory);
        bool success = !output.Contains("fatal:") && !output.Contains("error:");
        return new GitResult(success, output);
    }
}
