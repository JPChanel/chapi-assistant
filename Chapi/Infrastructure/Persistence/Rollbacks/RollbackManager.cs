using System.IO;
using System.Text.Json;

namespace Chapi.Infrastructure.Persistence.Rollbacks;

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
        public string ChangeType { get; set; }
        public string BackupContent { get; set; }
        public int? LineNumber { get; set; }
        public string AddedLine { get; set; }
    }

    private static string GetRollbackFilePath(string module, string methodName, string operation)
    {
        Directory.CreateDirectory(RollbackDirectory);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var safeModule = module.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
        var safeMethod = methodName.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
        var fileName = $"rollback_{safeModule}_{safeMethod}_{operation}_{timestamp}.json";
        return Path.Combine(RollbackDirectory, fileName);
    }

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

    public static void SaveRollback(RollbackEntry entry)
    {
        var filePath = GetRollbackFilePath(entry.Module, entry.MethodName, entry.Operation);
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }

    public static void RecordFileCreation(RollbackEntry entry, string filePath)
    {
        entry.Changes.Add(new FileChange
        {
            FilePath = filePath,
            ChangeType = "Created",
            BackupContent = null
        });
    }

    public static void RecordFileModification(RollbackEntry entry, string filePath, string originalContent)
    {
        entry.Changes.Add(new FileChange
        {
            FilePath = filePath,
            ChangeType = "Modified",
            BackupContent = originalContent
        });
    }

    public static void CommitTransaction(RollbackEntry entry)
    {
        SaveRollback(entry);
    }

    public static List<RollbackEntry> GetAvailableRollbacks()
    {
        if (!Directory.Exists(RollbackDirectory))
            return new List<RollbackEntry>();

        var rollbacks = new List<RollbackEntry>();
        foreach (var file in Directory.GetFiles(RollbackDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var entry = JsonSerializer.Deserialize<RollbackEntry>(json);
                if (entry != null)
                    rollbacks.Add(entry);
            }
            catch { }
        }
        return rollbacks.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public static string GetRollbackFilePathForEntry(RollbackEntry entry)
    {
        return GetRollbackFilePath(entry.Module, entry.MethodName, entry.Operation);
    }

    public static void ExecuteRollback(RollbackEntry entry)
    {
        foreach (var change in entry.Changes)
        {
            if (change.ChangeType == "Created" && File.Exists(change.FilePath))
            {
                File.Delete(change.FilePath);
            }
            else if (change.ChangeType == "Modified" && !string.IsNullOrEmpty(change.BackupContent))
            {
                File.WriteAllText(change.FilePath, change.BackupContent);
            }
        }
    }

    public static void CleanOldRollbacks(int daysToKeep = 7)
    {
        if (!Directory.Exists(RollbackDirectory))
            return;

        var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
        foreach (var file in Directory.GetFiles(RollbackDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var entry = JsonSerializer.Deserialize<RollbackEntry>(json);
                if (entry != null && entry.CreatedAt < cutoffDate)
                {
                    File.Delete(file);
                }
            }
            catch { }
        }
    }

    public static void ClearAllRollbacks()
    {
        if (!Directory.Exists(RollbackDirectory))
            return;

        foreach (var file in Directory.GetFiles(RollbackDirectory))
        {
            try { File.Delete(file); } catch { }
        }

        foreach (var dir in Directory.GetDirectories(RollbackDirectory))
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
