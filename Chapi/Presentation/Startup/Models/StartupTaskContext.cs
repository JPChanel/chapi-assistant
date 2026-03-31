using System.Windows;

namespace Chapi.Presentation.Startup.Models;

public sealed class StartupTaskContext
{
    public Window Owner { get; init; } = null!;
    public Action MarkWindowInitialized { get; init; } = null!;
    public Action<bool> SetGitInstalled { get; init; } = null!;
    public Action LoadProjects { get; init; } = null!;
    public Func<Task> UpdateProjectStatusesAsync { get; init; } = null!;
    public Func<Task> LoadHistoryAsync { get; init; } = null!;
    public Func<Task> RefreshChangesAfterResetAsync { get; init; } = null!;

    public Chapi.Presentation.Features.Changes.ViewModels.ChangesViewModel? ChangesViewModel { get; init; }
    public Chapi.Presentation.Features.History.ViewModels.HistoryViewModel? HistoryViewModel { get; init; }
    public Chapi.Presentation.Features.Releases.ViewModels.ReleasesViewModel? ReleasesViewModel { get; init; }
}
