using System.IO;
using System.Text.Json;
using Chapi.Application.Interfaces;
using Chapi.Domain.Documentation;

namespace Chapi.Infrastructure.Documentation;

/// <summary>
/// Persiste sesiones de documentación en %AppData%\ChapiAssistant\doc\{ProjectName}\
/// </summary>
public class AppDataDocPersistenceService : IDocumentPersistenceService
{
    private static readonly string _baseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ChapiAssistant", "doc");

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public string GetStoragePath(string projectName) =>
        Path.Combine(_baseDir, SanitizeName(projectName));

    public async Task<bool> SaveAsync(DocumentSession session)
    {
        try
        {
            var dir = GetStoragePath(session.ProjectName);
            Directory.CreateDirectory(dir);

            session.LastModifiedAt = DateTime.Now;
            var filePath = Path.Combine(dir, $"{session.Id}.json");
            var json = JsonSerializer.Serialize(session, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<DocumentSession?> LoadAsync(string sessionId)
    {
        try
        {
            var files = Directory.GetFiles(_baseDir, $"{sessionId}.json", SearchOption.AllDirectories);
            if (files.Length == 0) return null;

            var json = await File.ReadAllTextAsync(files[0]);
            return JsonSerializer.Deserialize<DocumentSession>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<DocumentSession>> GetAllAsync(string projectName)
    {
        try
        {
            var dir = GetStoragePath(projectName);
            if (!Directory.Exists(dir)) return Array.Empty<DocumentSession>();

            var sessions = new List<DocumentSession>();
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                var json = await File.ReadAllTextAsync(file);
                var session = JsonSerializer.Deserialize<DocumentSession>(json, _jsonOptions);
                if (session != null) sessions.Add(session);
            }
            return sessions.OrderByDescending(s => s.LastModifiedAt).ToList();
        }
        catch
        {
            return Array.Empty<DocumentSession>();
        }
    }

    public Task<bool> DeleteAsync(string sessionId)
    {
        try
        {
            var files = Directory.GetFiles(_baseDir, $"{sessionId}.json", SearchOption.AllDirectories);
            foreach (var file in files) File.Delete(file);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private static string SanitizeName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
