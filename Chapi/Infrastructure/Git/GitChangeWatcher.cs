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

    public event EventHandler<string>? RepositoryChanged;

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
                Filter = "*.*"
            };

            // Eventos de cambios
            watcher.Changed += (s, e) => OnFileChanged(projectPath, e);
            watcher.Created += (s, e) => OnFileChanged(projectPath, e);
            watcher.Deleted += (s, e) => OnFileChanged(projectPath, e);
            watcher.Renamed += (s, e) => OnFileChanged(projectPath, e);

            watcher.EnableRaisingEvents = true;
            _watchers[projectPath] = watcher;

            System.Diagnostics.Debug.WriteLine($"🔍 GitChangeWatcher: Monitoreando {projectPath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error iniciando watcher: {ex.Message}");
        }
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
            System.Diagnostics.Debug.WriteLine($"🛑 GitChangeWatcher: Detenido monitoreo de {projectPath}");
        }
    }

    private void OnFileChanged(string projectPath, FileSystemEventArgs e)
    {
        // Ignorar cambios en .git/
        if (e.FullPath.Contains("\\.git\\") || e.FullPath.Contains("/.git/"))
            return;

        // Ignorar archivos temporales y de sistema
        var fileName = Path.GetFileName(e.FullPath);
        if (fileName.StartsWith(".") || fileName.EndsWith(".tmp") || fileName.EndsWith("~"))
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
                System.Diagnostics.Debug.WriteLine($"📝 GitChangeWatcher: Cambio detectado en {projectPath}");
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
