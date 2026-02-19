using Chapi.Presentation.Views.Dialogs;
using MaterialDesignThemes.Wpf;

namespace Chapi.Infrastructure.Services
{
    public static class DialogService
    {
        public static async Task<(bool, string)> ShowInputDialog(string title, string message, string? defaultText = null)
        {
            var dialog = new InputDialog
            {
                Message = message,
                Title = title,

            };
            if (!string.IsNullOrEmpty(defaultText))
            {
                dialog.ResponseTextBox.Text = defaultText;
            }

            var result = await DialogHost.Show(dialog, App.GlobalDialogIdentifier);
            var res = bool.TryParse(result?.ToString(), out var boolResult) && boolResult;

            return (res, dialog.ResponseText ?? string.Empty);

        }

        public static async Task<bool> ShowConfirmDialog(string title, string message, DialogVariant variant = DialogVariant.Info, DialogType type = DialogType.Confirm)
        {
            var dialog = new ConfirmationDialog
            {
                Title = title,
                Message = message,
                Variant = variant,
                DialogType = type
            };

            var result = await DialogHost.Show(dialog, App.GlobalDialogIdentifier);


            return bool.TryParse(result?.ToString(), out var boolResult) && boolResult;
        }

        public static async Task<object> ShowDialog(object dialogContent)
        {
            return await DialogHost.Show(dialogContent, App.GlobalDialogIdentifier);
        }

        public static void ShowTrayNotification(string title, string message)
        {
            App.TrayIconManager.ShowNotification(title, message);
        }

        public static async Task<(bool Confirmed, string TagName, string Message, bool IsRemote, bool IsLocal, string BuildAppName, string PackageId, string BuildAuthor, string LocalPath, string FtpUrl, string FtpUser, string FtpPassword, string IconPath, string SplashPath)> ShowCreateReleaseDialog(
            string defaultAppName = "", 
            string defaultPackageId = "",
            string defaultAuthor = "", 
            string defaultLocalPath = "", 
            string defaultFtpUrl = "", 
            string defaultFtpUser = "",
            string defaultIconPath = "",
            string defaultSplashPath = "")
        {
            var dialog = new CreateReleaseDialog();
            
            // Set defaults for Build Config & Destination
            dialog.SetDefaults(defaultAppName, defaultPackageId, defaultAuthor, defaultLocalPath, defaultFtpUrl, defaultFtpUser, defaultIconPath, defaultSplashPath);

            var result = await DialogHost.Show(dialog, App.GlobalDialogIdentifier);

            if (bool.TryParse(result?.ToString(), out var boolResult) && boolResult)
            {
                // Si el usuario eligió carpeta, devolvemos la carpeta, si eligió FTP, devolvemos FTP.
                // Limpiamos lo que no se seleccionó para evitar guardar basura.
                string finalLocalPath = dialog.IsFolderTarget ? dialog.LocalPath : "";
                string finalFtpUrl = !dialog.IsFolderTarget ? dialog.FtpUrl : ""; // Asumiendo IsFolderTarget false = FTP

                return (true, 
                        dialog.TagName ?? string.Empty, 
                        dialog.Message ?? string.Empty, 
                        dialog.IsRemote, 
                        dialog.IsLocal, 
                        dialog.AppName ?? string.Empty, 
                        dialog.PackageId ?? string.Empty,
                        dialog.Author ?? string.Empty,
                        finalLocalPath,
                        finalFtpUrl,
                        dialog.FtpUser,
                        dialog.FtpPassword,
                        dialog.IconPath,
                        dialog.SplashPath);
            }

            return (false, string.Empty, string.Empty, false, false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }
    }
}
