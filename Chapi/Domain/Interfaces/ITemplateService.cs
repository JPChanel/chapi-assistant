using Chapi.Domain.Common;

namespace Chapi.Domain.Interfaces;

public interface ITemplateService
{
    Task<Result> RenameTemplateAsync(string path, string oldName, string newName, Action<string> onProgress = null);
}
