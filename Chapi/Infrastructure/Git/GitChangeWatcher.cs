using System.Collections.Concurrent;
using System.IO;

namespace Chapi.Infrastructure.Git;

/// <summary>
/// Observa cambios en el sistema de archivos del repositorio Git.
/// Similar al FSMonitor de GitHub Desktop.
/// </summary>
public class GitChangeWatcher : IDisposable
{
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastChangeTime = new();
    private const int DEBOUNCE_MS = 500; // Esperar 500ms antes de notificar cambios

    public bool IsSilenced { get; set; }

    public event EventHandler<string>? RepositoryChanged;

    /// <summary>
    /// Crea un objeto que silencia el watcher mientras exista.
    /// </summary>
    public IDisposable Silence() => new WatcherSilencer(this);

    private class WatcherSilencer : IDisposable
    {
        private readonly GitChangeWatcher _watcher;
        public WatcherSilencer(GitChangeWatcher watcher) { _watcher = watcher; _watcher.IsSilenced = true; }
        public void Dispose() => _watcher.IsSilenced = false;
    }

    /// <summary>
    /// Inicia el monitoreo de un repositorio.
    /// </summary>
    public void WatchRepository(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
            return;

        // Si ya está siendo monitoreado, no hacer nada
        if (_watchers.ContainsKey(projectPath))
            return;

        try
        {
            var watcher = new FileSystemWatcher(projectPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                             | NotifyFilters.DirectoryName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size,
                // Ignorar carpetas .git para evitar ruido
                // Ver todos los archivos para detectar cambios en .git/refs y .git/logs
                Filter = "*"
            };

            // Eventos de cambios
            watcher.Changed += (s, e) => OnFileChanged(projectPath, e);
            watcher.Created += (s, e) => OnFileChanged(projectPath, e);
            watcher.Deleted += (s, e) => OnFileChanged(projectPath, e);
            watcher.Renamed += (s, e) => OnFileChanged(projectPath, e);

            watcher.EnableRaisingEvents = true;
            _watchers[projectPath] = watcher;

        }
        catch (Exception) { }
    }

    /// <summary>
    /// Detiene el monitoreo de un repositorio.
    /// </summary>
    public void UnwatchRepository(string projectPath)
    {
        if (_watchers.TryRemove(projectPath, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            _lastChangeTime.TryRemove(projectPath, out _);
        }
    }

    private void OnFileChanged(string projectPath, FileSystemEventArgs e)
    {
        if (IsSilenced) return;

        // Normalizar ruta para comparaciones
        string path = e.FullPath.Replace('\\', '/');
        bool isGitInternal = path.Contains("/.git/");
        
        if (isGitInternal)
        {
            // Detectar cambios en stashes, commits o el index
            // .git/index, .git/refs/stash, .git/logs/refs/stash, .git/HEAD
            bool isRelevant = path.EndsWith("/stash") || 
                             path.EndsWith("/HEAD") ||
                             path.Contains("/refs/heads/") ||
                             path.EndsWith("/index");

            if (!isRelevant) return;
        }

        // Ignorar archivos temporales comunes y bloqueos
        string fileName = Path.GetFileName(path);
        if (fileName.EndsWith(".tmp") || fileName.EndsWith("~") || 
            fileName.StartsWith("index.lock") || fileName.Contains(".lock"))
            return;

        // Debouncing: evitar múltiples notificaciones por el mismo cambio
        var now = DateTime.Now;
        if (_lastChangeTime.TryGetValue(projectPath, out var lastTime))
        {
            if ((now - lastTime).TotalMilliseconds < DEBOUNCE_MS)
                return;
        }

        _lastChangeTime[projectPath] = now;

        // Notificar cambio después del debounce (en un solo Task)
        Task.Run(async () =>
        {
            await Task.Delay(DEBOUNCE_MS);

            // Verificar que no haya habido otro cambio más reciente
            if (_lastChangeTime.TryGetValue(projectPath, out var checkTime) && checkTime == now)
            {
                RepositoryChanged?.Invoke(this, projectPath);
            }
        });
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
        _lastChangeTime.Clear();
    }
}
