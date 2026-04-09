using Chapi.Domain.Common;
using Chapi.Domain.Entities.Workspace;

namespace Chapi.Application.Interfaces.Workspace;

public interface IWorkspaceService
{
    Task<Result<WorkspaceData>> LoadWorkspaceAsync(string projectPath);
    Task<Result> SaveWorkspaceAsync(WorkspaceData data);
    Task<Result<IReadOnlyList<WorkspaceActivityRecord>>> LoadActivityRecordsAsync();
    Task<Result<string>> GetRandomQuoteAsync();
    Result OpenFileInExplorer(string filePath);
}
