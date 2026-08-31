using Chapi.Presentation.Shared.Dialogs.Views;

namespace Chapi.Infrastructure.Services;

public static class DialogService
{
    public static Task<(bool, string)> ShowInputDialog(string title, string message, string? defaultText = null)
        => Chapi.Presentation.Shared.Dialogs.DialogService.ShowInputDialog(title, message, defaultText);

    public static Task<bool> ShowConfirmDialog(
        string title,
        string message,
        DialogVariant variant = DialogVariant.Info,
        DialogType type = DialogType.Confirm,
        string confirmButtonText = "SI",
        string cancelButtonText = "NO")
        => Chapi.Presentation.Shared.Dialogs.DialogService.ShowConfirmDialog(
            title,
            message,
            variant,
            type,
            confirmButtonText,
            cancelButtonText);

    public static Task<object> ShowDialog(object dialogContent)
        => Chapi.Presentation.Shared.Dialogs.DialogService.ShowDialog(dialogContent);

    public static void ShowTrayNotification(string title, string message)
        => Chapi.Presentation.Shared.Dialogs.DialogService.ShowTrayNotification(title, message);

    public static Task<(bool Confirmed, string TagName, string Message, bool IsRemote, bool IsLocal, string BuildAppName, string PackageId, string BuildAuthor, string BuildPlatform, string LocalPath, string FtpUrl, string FtpUser, string FtpPassword, string IconPath, string SplashPath)> ShowCreateReleaseDialog(
        string defaultAppName = "",
        string defaultPackageId = "",
        string defaultAuthor = "",
        string defaultLocalPath = "",
        string defaultFtpUrl = "",
        string defaultFtpUser = "",
        string defaultPlatform = "",
        string defaultIconPath = "",
        string defaultSplashPath = "")
        => Chapi.Presentation.Shared.Dialogs.DialogService.ShowCreateReleaseDialog(
            defaultAppName,
            defaultPackageId,
            defaultAuthor,
            defaultLocalPath,
            defaultFtpUrl,
            defaultFtpUser,
            defaultPlatform,
            defaultIconPath,
            defaultSplashPath);

    public static Task<(bool Confirmed, string ProjectPath, string DefaultBranch, string? RemoteUrl, bool CreateReadme, bool CreateGitIgnore)> ShowCreateRepositoryDialog(
        string? initialPath = null,
        string? defaultBranch = null)
        => Chapi.Presentation.Shared.Dialogs.DialogService.ShowCreateRepositoryDialog(initialPath, defaultBranch);
}
