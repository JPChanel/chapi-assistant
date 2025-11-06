
using System.IO;
using System.Text.Json;

namespace Chapi.Helper;

public class RollbackManager
{
    private static readonly string RollbackDirectory = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Rollbacks"
    );

    public class RollbackEntry
    {
        public string Module { get; set; }
        public string MethodName { get; set; }
        public string Operation { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<FileChange> Changes { get; set; } = new();
    }

    public class FileChange
    {
        public string FilePath { get; set; }
        public string ChangeType { get; set; } // "Created", "Modified", "LineAdded"
        public string BackupContent { get; set; } // Contenido antes del cambio
        public int? LineNumber { get; set; } // Para cambios en DependencyInjection
        public string AddedLine { get; set; } // Línea agregada
    }

    private static string GetRollbackFilePath(string module, string methodName, string operation)
    {
        Directory.CreateDirectory(RollbackDirectory);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = $"rollback_{module}_{methodName}_{operation}_{timestamp}.json";
        return Path.Combine(RollbackDirectory, fileName);
    }

    // ✅ REGISTRAR INICIO DE OPERACIÓN
    public static RollbackEntry StartTransaction(string module, string methodName, string operation)
    {
        return new RollbackEntry
        {
            Module = module,
            MethodName = methodName,
            Operation = operation,
            CreatedAt = DateTime.Now
        };
    }

    // ✅ REGISTRAR CREACIÓN DE ARCHIVO
    public static void RecordFileCreation(RollbackEntry entry, string filePath)
    {
        entry.Changes.Add(new FileChange
        {
            FilePath = filePath,
            ChangeType = "Created",
            BackupContent = null // No hay backup porque es nuevo
        });
    }

    // ✅ REGISTRAR MODIFICACIÓN DE ARCHIVO
    public static void RecordFileModification(RollbackEntry entry, string filePath, string originalContent)
    {
        entry.Changes.Add(new FileChange
        {
            FilePath = filePath,
            ChangeType = "Modified",
            BackupContent = originalContent
        });
    }

    // ✅ REGISTRAR LÍNEA AGREGADA EN DependencyInjection
    public static void RecordLineAdded(RollbackEntry entry, string filePath, string addedLine, int lineNumber)
    {
        entry.Changes.Add(new FileChange
        {
            FilePath = filePath,
            ChangeType = "LineAdded",
            AddedLine = addedLine,
            LineNumber = lineNumber
        });
    }

    // ✅ GUARDAR TRANSACCIÓN COMPLETA
    public static void CommitTransaction(RollbackEntry entry)
    {
        var filePath = GetRollbackFilePath(entry.Module, entry.MethodName, entry.Operation);
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(filePath, json);
        Msg.Assistant($"💾 Rollback registrado: {Path.GetFileName(filePath)}");
    }

    // ✅ EJECUTAR ROLLBACK
    public static void ExecuteRollback(string rollbackFilePath)
    {
        if (!File.Exists(rollbackFilePath))
        {
            Msg.Assistant("⚠️ Archivo de rollback no encontrado.");
            return;
        }

        var json = File.ReadAllText(rollbackFilePath);
        var entry = JsonSerializer.Deserialize<RollbackEntry>(json);

        if (entry == null)
        {
            Msg.Assistant("⚠️ Archivo de rollback corrupto.");
            return;
        }

        Msg.Assistant($"🔄 Ejecutando rollback de '{entry.MethodName}' en módulo '{entry.Module}'...");

        // Procesar cambios en orden inverso
        foreach (var change in entry.Changes.AsEnumerable().Reverse())
        {
            try
            {
                switch (change.ChangeType)
                {
                    case "Created":
                        if (File.Exists(change.FilePath))
                        {
                            File.Delete(change.FilePath);
                            Msg.Assistant($"  ✓ Eliminado: {Path.GetFileName(change.FilePath)}");
                        }
                        break;

                    case "Modified":
                        if (File.Exists(change.FilePath) && change.BackupContent != null)
                        {
                            File.WriteAllText(change.FilePath, change.BackupContent);
                            Msg.Assistant($"  ✓ Restaurado: {Path.GetFileName(change.FilePath)}");
                        }
                        break;

                    case "LineAdded":
                        if (File.Exists(change.FilePath))
                        {
                            RemoveLineFromFile(change.FilePath, change.AddedLine);
                            Msg.Assistant($"  ✓ Línea removida de: {Path.GetFileName(change.FilePath)}");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Msg.Assistant($"  ⚠️ Error en {Path.GetFileName(change.FilePath)}: {ex.Message}");
            }
        }

        // Eliminar archivo de rollback después de ejecutarlo
        File.Delete(rollbackFilePath);
        Msg.Assistant($"✅ Rollback completado y archivo eliminado.");
    }

    // ✅ REMOVER LÍNEA ESPECÍFICA DE UN ARCHIVO
    private static void RemoveLineFromFile(string filePath, string lineToRemove)
    {
        var lines = File.ReadAllLines(filePath).ToList();
        var normalizedLineToRemove = lineToRemove.Trim().Replace(" ", "");

        // Buscar y remover la línea
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            var normalizedLine = lines[i].Trim().Replace(" ", "");
            if (normalizedLine.Contains(normalizedLineToRemove) ||
                normalizedLineToRemove.Contains(normalizedLine))
            {
                lines.RemoveAt(i);
                break;
            }
        }

        File.WriteAllLines(filePath, lines);
    }

    // ✅ LISTAR ROLLBACKS DISPONIBLES
    public static List<RollbackEntry> GetAvailableRollbacks()
    {
        if (!Directory.Exists(RollbackDirectory))
            return new List<RollbackEntry>();

        var rollbacks = new List<RollbackEntry>();
        var files = Directory.GetFiles(RollbackDirectory, "rollback_*.json");

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var entry = JsonSerializer.Deserialize<RollbackEntry>(json);
                if (entry != null)
                {
                    rollbacks.Add(entry);
                }
            }
            catch
            {
                // Ignorar archivos corruptos
            }
        }

        return rollbacks.OrderByDescending(r => r.CreatedAt).ToList();
    }

    // ✅ OBTENER RUTA DEL ARCHIVO DE ROLLBACK
    public static string GetRollbackFilePathForEntry(RollbackEntry entry)
    {
        var timestamp = entry.CreatedAt.ToString("yyyyMMdd_HHmmss");
        var fileName = $"rollback_{entry.Module}_{entry.MethodName}_{entry.Operation}_{timestamp}.json";
        return Path.Combine(RollbackDirectory, fileName);
    }

    // ✅ LIMPIAR ROLLBACKS ANTIGUOS (más de 30 días)
    public static void CleanOldRollbacks(int daysOld = 30)
    {
        if (!Directory.Exists(RollbackDirectory))
            return;

        var cutoffDate = DateTime.Now.AddDays(-daysOld);
        var files = Directory.GetFiles(RollbackDirectory, "rollback_*.json");

        foreach (var file in files)
        {
            if (File.GetCreationTime(file) < cutoffDate)
            {
                File.Delete(file);
                Msg.Assistant($"🗑️ Rollback antiguo eliminado: {Path.GetFileName(file)}");
            }
        }
    }
}