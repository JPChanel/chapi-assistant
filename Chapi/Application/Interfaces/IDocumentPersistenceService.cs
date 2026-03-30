using Chapi.Domain.Documentation;

namespace Chapi.Application.Interfaces;

public interface IDocumentPersistenceService
{
    Task<bool> SaveAsync(DocumentSession session);
    Task<DocumentSession?> LoadAsync(string sessionId);
    Task<IReadOnlyList<DocumentSession>> GetAllAsync(string projectName);
    Task<bool> DeleteAsync(string sessionId);
    string GetStoragePath(string projectName);
}
