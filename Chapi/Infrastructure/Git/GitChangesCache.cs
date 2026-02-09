using System.Collections.Concurrent;
using Chapi.Domain.Entities;

namespace Chapi.Infrastructure.Git;

/// <summary>
/// Caché de cambios Git para evitar recalcular todo el repositorio.
/// Similar al sistema de caché de GitHub Desktop.
/// </summary>
public class GitChangesCache
{
    private readonly ConcurrentDictionary<string, CachedChanges> _cache = new();
    private const int CACHE_VALIDITY_SECONDS = 30; // Caché válido por 30 segundos

    private class CachedChanges
    {
        public DateTime Timestamp { get; set; }
        public List<FileChange> Changes { get; set; } = new();
        public int TotalAdditions { get; set; }
        public int TotalDeletions { get; set; }
    }

    /// <summary>
    /// Intenta obtener cambios del caché.
    /// </summary>
    public bool TryGetChanges(string projectPath, out IEnumerable<FileChange> changes, out int totalAdditions, out int totalDeletions)
    {
        changes = Enumerable.Empty<FileChange>();
        totalAdditions = 0;
        totalDeletions = 0;

        if (!_cache.TryGetValue(projectPath, out var cached))
            return false;

        // Verificar si el caché sigue siendo válido
        var age = (DateTime.Now - cached.Timestamp).TotalSeconds;
        if (age > CACHE_VALIDITY_SECONDS)
        {
            // Caché expirado
            _cache.TryRemove(projectPath, out _);
            return false;
        }

        // Caché válido
        changes = cached.Changes.Select(c => new FileChange
        {
            FilePath = c.FilePath,
            Status = c.Status,
            Additions = c.Additions,
            Deletions = c.Deletions
        }).ToList();

        totalAdditions = cached.TotalAdditions;
        totalDeletions = cached.TotalDeletions;

        System.Diagnostics.Debug.WriteLine($"✅ GitChangesCache: Hit para {projectPath} (edad: {age:F1}s)");
        return true;
    }

    /// <summary>
    /// Guarda cambios en el caché.
    /// </summary>
    public void SetChanges(string projectPath, IEnumerable<FileChange> changes, int totalAdditions, int totalDeletions)
    {
        var cached = new CachedChanges
        {
            Timestamp = DateTime.Now,
            Changes = changes.Select(c => new FileChange
            {
                FilePath = c.FilePath,
                Status = c.Status,
                Additions = c.Additions,
                Deletions = c.Deletions
            }).ToList(),
            TotalAdditions = totalAdditions,
            TotalDeletions = totalDeletions
        };

        _cache[projectPath] = cached;
        System.Diagnostics.Debug.WriteLine($"💾 GitChangesCache: Guardado para {projectPath} ({cached.Changes.Count} archivos)");
    }

    /// <summary>
    /// Invalida el caché de un repositorio (cuando se detectan cambios).
    /// </summary>
    public void Invalidate(string projectPath)
    {
        if (_cache.TryRemove(projectPath, out _))
        {
            System.Diagnostics.Debug.WriteLine($"🗑️ GitChangesCache: Invalidado para {projectPath}");
        }
    }

    /// <summary>
    /// Limpia todo el caché.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        System.Diagnostics.Debug.WriteLine("🧹 GitChangesCache: Limpiado completamente");
    }
}
